using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreenUtility.Theming;
using ScreenUtility.ViewModels;

namespace ScreenUtility.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly ThemeService _theme;

    public SettingsWindow(SettingsViewModel viewModel, ThemeService theme)
    {
        _viewModel = viewModel;
        _theme = theme;
        DataContext = viewModel;
        InitializeComponent();

        VersionText.Text = $"Version {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";
        Loaded += OnLoaded;
        SourceInitialized += (_, _) => ThemeService.ApplyWindowTheme(this, _theme.IsDark);
        _theme.Changed += OnThemeChanged;
        Closed += (_, _) => _theme.Changed -= OnThemeChanged;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        Keyboard.ClearFocus();
        base.OnClosing(e);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeService.ApplyWindowTheme(this, _theme.IsDark);
        ShowPage("displays");

        try
        {
            await ReloadDisplaysAsync();
        }
        catch
        {
            // Display enumeration failures leave the page empty; the user can retry.
        }
    }

    public Task ReloadDisplaysAsync() => _viewModel.LoadDisplaysAsync();

    private void OnThemeChanged(object? sender, EventArgs e) =>
        ThemeService.ApplyWindowTheme(this, _theme.IsDark);

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not RadioButton { Tag: string page })
        {
            return;
        }

        ShowPage(page);
    }

    private void ShowPage(string page)
    {
        DisplaysPage.Visibility = page == "displays" ? Visibility.Visible : Visibility.Collapsed;
        PreferencesPage.Visibility = page == "preferences" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == "about" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SettingsDisplayItem item })
        {
            _viewModel.MoveDisplay(item, -1);
        }
    }

    private void OnMoveDownClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SettingsDisplayItem item })
        {
            _viewModel.MoveDisplay(item, 1);
        }
    }

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not RadioButton { IsChecked: true, Tag: string tag } || !int.TryParse(tag, out var index))
        {
            return;
        }

        _viewModel.ThemeIndex = index;
    }

    private void OnOpenSettingsFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _viewModel.SettingsPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (!string.IsNullOrWhiteSpace(directory))
            {
                Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            }
        }
        catch
        {
            // Opening Explorer is a convenience only.
        }
    }
}
