using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace WindowsMemoryReaper.Services;

/// <summary>
/// Elevated side of the pipe. Receives cleanup requests from the tray process
/// and runs the RAMMap operations, reporting the outcome back over the pipe.
/// Runs headless: no tray icon and no window.
/// </summary>
public static class CleanupWorker
{
    /// <summary>Runs the worker message loop; returns a process exit code.</summary>
    public static async Task<int> RunAsync(string pipeName, int trayPid, CancellationToken cancellationToken)
    {
        try
        {
            using var watchdogCts = new CancellationTokenSource();
            var watchdog = StartTrayWatchdog(trayPid, watchdogCts.Token);
            try
            {
                await using var client = new NamedPipeClientStream(".", pipeName,
                    PipeDirection.InOut, PipeOptions.None);
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

                var ramMap = new RamMapService();

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (trayPid > 0 && !IsProcessAlive(trayPid))
                    {
                        break;
                    }

                    CleanRequest? request;
                    try
                    {
                        var requestBytes = await PipeProtocol.ReadChunkAsync(client, cancellationToken).ConfigureAwait(false);
                        request = PipeProtocol.Deserialize(requestBytes, PipeJsonContext.Default.CleanRequest);
                    }
                    catch (Exception ex) when (IsChannelFailure(ex))
                    {
                        break;
                    }

                    if (request is null)
                    {
                        continue;
                    }

                    var result = await ramMap.RunCleanupAsync(request.RamMapPath, cancellationToken).ConfigureAwait(false);
                    var reply = new CleanReply
                    {
                        Kind = result.Kind.ToString(),
                        CompletedOperations = result.CompletedOperations,
                        Detail = result.Detail,
                    };

                    try
                    {
                        var replyBytes = PipeProtocol.Serialize(reply, PipeJsonContext.Default.CleanReply);
                        await PipeProtocol.WriteChunkAsync(client, replyBytes, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (IsChannelFailure(ex))
                    {
                        break;
                    }
                }

                return 0;
            }
            finally
            {
                watchdogCts.Cancel();
                try
                {
                    await watchdog.ConfigureAwait(false);
                }
                catch
                {
                    // The watchdog may already have terminated this process.
                }
            }
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex) when (IsChannelFailure(ex))
        {
            return 1;
        }
    }

    /// <summary>
    /// Polls whether the tray process that spawned this worker is still alive and
    /// terminates the worker immediately if it is gone. This closes the gap where
    /// the worker stays inside a long RAMMap cycle and only re-checks the tray at
    /// the top of its loop, which would otherwise let an orphaned elevated worker
    /// linger (and keep the single-file EXE locked).
    /// </summary>
    private static async Task StartTrayWatchdog(int trayPid, CancellationToken cancellationToken)
    {
        if (trayPid <= 0)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            if (!IsProcessAlive(trayPid))
            {
                Environment.Exit(1);
            }
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsChannelFailure(Exception ex)
        => ex is IOException or OperationCanceledException or InvalidDataException
            or EndOfStreamException or TimeoutException;
}