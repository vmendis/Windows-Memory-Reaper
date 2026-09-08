using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace WindowsMemoryReaper.Services;

/// <summary>
/// Executes the five RAMMap memory-cleaning operations sequentially.
/// Per spec sections 5, 6, 15 and 17.
/// </summary>
public sealed class RamMapService
{
    /// <summary>Per-operation timeout. See spec section 15.</summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Total timeout guard for an entire five-operation cycle.</summary>
    public TimeSpan CycleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    private static readonly string[] s_operations = ["-Ew", "-Es", "-Em", "-Et", "-E0"];

    private readonly object _gate = new();
    private bool _running;

    /// <summary>True while a cleanup cycle is executing.</summary>
    public bool IsRunning
    {
        get { lock (_gate) return _running; }
    }

    /// <summary>
    /// Runs a full cleanup cycle, launching each RAMMap operation and waiting for
    /// it to complete before starting the next. Never overlaps another cycle.
    /// </summary>
    public async Task<CleanupResult> RunCleanupAsync(string? ramMapPath, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_running)
            {
                return new CleanupResult(CleanupResultKind.AlreadyRunning, 0, null);
            }
            _running = true;
        }

        try
        {
            return await RunCoreAsync(ramMapPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _running = false;
            }
        }
    }

    private async Task<CleanupResult> RunCoreAsync(string? ramMapPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ramMapPath))
        {
            return new CleanupResult(CleanupResultKind.RamMapNotConfigured, 0, "RAMMap64.exe is not configured.");
        }

        if (!File.Exists(ramMapPath))
        {
            return new CleanupResult(CleanupResultKind.RamMapNotFound, 0, "RAMMap64.exe could not be found.");
        }

        using var cycleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cycleCts.CancelAfter(CycleTimeout);

        var completed = 0;
        try
        {
            foreach (var operation in s_operations)
            {
                var outcome = await RunOperationAsync(ramMapPath, operation, cycleCts.Token).ConfigureAwait(false);
                if (outcome != OperationOutcome.Succeeded)
                {
                    return outcome switch
                    {
                        OperationOutcome.TimedOut => new CleanupResult(CleanupResultKind.TimedOut, completed,
                            $"Operation {operation} timed out."),
                        OperationOutcome.LaunchFailed => new CleanupResult(CleanupResultKind.RamMapLaunchFailed, completed,
                            $"Operation {operation} could not be launched."),
                        _ => new CleanupResult(CleanupResultKind.Failed, completed,
                            $"Operation {operation} failed."),
                    };
                }

                completed++;

                // Allow cancellation between operations (e.g. application exit).
                if (cycleCts.IsCancellationRequested)
                {
                    return new CleanupResult(CleanupResultKind.TimedOut, completed,
                        "Cleanup cycle was cancelled or exceeded the cycle timeout.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new CleanupResult(CleanupResultKind.TimedOut, completed, "Cleanup cycle was cancelled.");
        }

        return new CleanupResult(CleanupResultKind.Completed, completed, null);
    }

    private async Task<OperationOutcome> RunOperationAsync(string ramMapPath, string operation, CancellationToken ct)
    {
        ProcessStartInfo psi = new()
        {
            FileName = ramMapPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        // Each argument passed individually -- never a shell string. Spec section 17.
        psi.ArgumentList.Add(operation);

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return OperationOutcome.LaunchFailed;
        }

        if (process is null)
        {
            return OperationOutcome.LaunchFailed;
        }

        using var registration = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
        });

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(OperationTimeout);

            try
            {
                // Ignore output here; we render our own console-hidden windows.
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cycle/outer cancellation: treat as timed out, not a RAMMap failure.
                return OperationOutcome.TimedOut;
            }
            catch (OperationCanceledException)
            {
                // Per-operation timeout with no outer cancellation.
                try { process.Kill(entireProcessTree: true); } catch { }
                return OperationOutcome.TimedOut;
            }

            return process.ExitCode == 0
                ? OperationOutcome.Succeeded
                : OperationOutcome.Failed;
        }
        finally
        {
            process.Dispose();
        }
    }

    private enum OperationOutcome
    {
        Succeeded,
        LaunchFailed,
        TimedOut,
        Failed,
    }
}
