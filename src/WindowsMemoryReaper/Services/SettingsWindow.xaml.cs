using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace WindowsMemoryReaper.Services;

/// <summary>
/// Small settings dialog. See spec section 18.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppController _controller;
    private readonly AppSettings _settings;

    public SettingsWindow()
    {
        InitializeComponent();
        _controller = null!;
        _settings = AppSettings.CreateDefaults();
        InitializeControls(_settings);
    }

    public SettingsWindow(AppController controller, AppSettings settings)
    {
        InitializeComponent();
        _controller = controller;
        _settings = settings;
        InitializeControls(settings);
    }

    private void InitializeControls(AppSettings settings)
    {
        RamMapPathBox.Text = settings.RamMapPath;
        AutoCleanCheckBox.IsChecked = settings.AutomaticCleaningEnabled;

        var found = false;
        foreach (var item in IntervalBox.Items)
        {
            if (item is ComboBoxItem { Content: string s } &&
                int.TryParse(s.Split(' ')[0], out var minutes) &&
                minutes == settings.CleaningIntervalMinutes)
            {
                IntervalBox.SelectedItem = item;
                found = true;
                break;
            }
        }

        if (!found)
        {
            IntervalBox.SelectedItem = IntervalBox.Items[^1];
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var path = RamMapPathBox.Text.Trim();
        StatusText.Text = string.IsNullOrWhiteSpace(path)
            ? "RAMMap64.exe: not configured"
            : File.Exists(path)
                ? "RAMMap64.exe found"
                : "RAMMap64.exe not found";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select RAMMap64.exe",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            FileName = string.IsNullOrWhiteSpace(RamMapPathBox.Text) ? "RAMMap64.exe" : RamMapPathBox.Text,
        };

        if (dialog.ShowDialog(this) == true)
        {
            RamMapPathBox.Text = dialog.FileName;
            UpdateStatus();
        }
    }

    private async void CleanNow_Click(object sender, RoutedEventArgs e)
    {
        SaveTemporary();
        _controller.ApplySettings(_settings);
        await _controller.OnCleanNowAsync();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveTemporary();
        _controller.OnSettingsSaved(_settings);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SaveTemporary()
    {
        _settings.RamMapPath = RamMapPathBox.Text.Trim();
        _settings.AutomaticCleaningEnabled = AutoCleanCheckBox.IsChecked == true;

        if (IntervalBox.SelectedItem is ComboBoxItem { Content: string s } &&
            int.TryParse(s.Split(' ')[0], out var minutes))
        {
            _settings.CleaningIntervalMinutes = minutes;
        }

        UpdateStatus();
    }
}