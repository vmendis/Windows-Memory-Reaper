using System.Windows;

namespace WindowsMemoryReaper;

/// <summary>
/// Hidden host window that keeps the WPF dispatcher alive for the tray
/// application. No conventional main window is shown (spec section 8).
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ShowInTaskbar = false;
    }
}
