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
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex) when (IsChannelFailure(ex))
        {
            return 1;
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