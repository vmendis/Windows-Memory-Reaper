using System.Windows;
using WindowsMemoryReaper.Services;

namespace WindowsMemoryReaper;

/// <summary>
/// Interaction logic for App.xaml. Dispatches between the tray mode (default)
/// and the elevated cleanup-worker mode (launched with <c>--worker</c> by the
/// tray process via the <c>runas</c> verb).
/// </summary>
public partial class App : Application
{
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args;
        if (args.Length > 0 && string.Equals(args[0], "--worker", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunWorkerAsync(args);
            return;
        }

        _controller = new AppController();
    }

    private async Task RunWorkerAsync(string[] args)
    {
        var pipeName = GetArgumentValue(args, "--pipe");
        var trayPid = GetArgumentValue(args, "--traypid") is { } pidText && int.TryParse(pidText, out var pid)
            ? pid
            : -1;

        try
        {
            if (pipeName is null)
            {
                Environment.ExitCode = 2;
                return;
            }

            using var cts = new CancellationTokenSource();
            var exitCode = await CleanupWorker.RunAsync(pipeName, trayPid, cts.Token).ConfigureAwait(true);
            Environment.ExitCode = exitCode;
        }
        catch
        {
            Environment.ExitCode = 1;
        }
        finally
        {
            // Re-enters on the dispatcher thread (captured via ConfigureAwait(true)),
            // so this cleanly ends the WPF message loop of the worker process.
            Application.Current?.Dispatcher.InvokeShutdown();
        }
    }

    private static string? GetArgumentValue(string[] args, string name)
    {
        var prefix = name + "=";
        foreach (var arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[prefix.Length..];
            }
        }

        return null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        base.OnExit(e);
    }
}