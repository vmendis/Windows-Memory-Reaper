using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace WindowsMemoryReaper.Services;

/// <summary>
/// Tray-process side of the elevation architecture. Spawns the elevated cleanup
/// worker (once-only UAC via the <c>runas</c> verb), connects to it over a named
/// pipe, sends cleanup requests and maps the replies back to
/// <see cref="CleanupResult"/>. Provides the "no repeated per-operation UAC
/// prompts" behaviour by keeping a single persistent worker alive.
/// </summary>
public sealed class WorkerBridge : IDisposable
{
    private static readonly TimeSpan s_connectTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan s_replyTimeout = TimeSpan.FromMinutes(6);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _pipeName;
    private NamedPipeServerStream? _server;

    public WorkerBridge()
    {
        _pipeName = "WindowsMemoryReaper-" + Guid.NewGuid().ToString("N");
    }

    /// <summary>True when the elevated worker is currently connected.</summary>
    public bool IsConnected => _server is { IsConnected: true };

    /// <summary>
    /// Ensures a worker is available, sends a cleanup request and returns the
    /// result. Fails fast when the user declines the elevation prompt. Cleanup
    /// requests are serialized so two cleanups can never overlap.
    /// </summary>
    public async Task<CleanupResult> RunCleanupAsync(string ramMapPath, string[]? operations,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await EnsureWorkerAsync(cancellationToken).ConfigureAwait(false))
            {
                return new CleanupResult(CleanupResultKind.ElevationDeclined, 0,
                    "Administrator approval for the cleanup worker was not granted.");
            }

            if (_server is null)
            {
                return new CleanupResult(CleanupResultKind.WorkerDisconnected, 0,
                    "The cleanup worker is not connected.");
            }

            var request = new CleanRequest { RamMapPath = ramMapPath ?? string.Empty, Operations = operations };
            var requestBytes = PipeProtocol.Serialize(request, PipeJsonContext.Default.CleanRequest);
            await PipeProtocol.WriteChunkAsync(_server, requestBytes, cancellationToken).ConfigureAwait(false);

            using var replyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            replyCts.CancelAfter(s_replyTimeout);
            var replyBytes = await PipeProtocol.ReadChunkAsync(_server, replyCts.Token).ConfigureAwait(false);
            var reply = PipeProtocol.Deserialize(replyBytes, PipeJsonContext.Default.CleanReply);

            return MapReply(reply);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException
            or InvalidDataException or EndOfStreamException)
        {
            MarkDisconnected();
            return new CleanupResult(CleanupResultKind.WorkerDisconnected, 0,
                "The cleanup worker is no longer available.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> EnsureWorkerAsync(CancellationToken cancellationToken)
    {
        if (_server is { IsConnected: true })
        {
            return true;
        }

        MarkDisconnected();

        _server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut,
            maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        var arguments = $"--worker --pipe={_pipeName} --traypid={Environment.ProcessId}";
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "WindowsMemoryReaper.exe"),
            UseShellExecute = true,
            Verb = "runas",
            Arguments = arguments,
        };

        try
        {
            Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            // The user declined the elevation prompt.
            MarkDisconnected();
            return false;
        }

        try
        {
            await _server.WaitForConnectionAsync(cancellationToken)
                .WaitAsync(s_connectTimeout, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException
            or TimeoutException or InvalidOperationException)
        {
            MarkDisconnected();
            return false;
        }
    }

    private CleanupResult MapReply(CleanReply? reply)
    {
        if (reply is null)
        {
            return new CleanupResult(CleanupResultKind.WorkerDisconnected, 0, "No reply from the cleanup worker.");
        }

        return Enum.TryParse<CleanupResultKind>(reply.Kind, out var kind)
            ? new CleanupResult(kind, reply.CompletedOperations, reply.Detail)
            : new CleanupResult(CleanupResultKind.WorkerDisconnected, 0, "Unknown reply from the cleanup worker.");
    }

    private void MarkDisconnected()
    {
        _server?.Dispose();
        _server = null;
    }

    public void Dispose()
    {
        _gate.Dispose();
        _server?.Dispose();
        _server = null;
    }
}