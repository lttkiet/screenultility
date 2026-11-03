using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreenUtility.Models;
using ScreenUtility.ViewModels;

namespace ScreenUtility.Controls;

public partial class MonitorCard : UserControl
{
    public MonitorCard()
    {
        InitializeComponent();
    }

    private void OnBrightnessWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not MonitorViewModel monitor)
        {
            return;
        }

        var flyout = Window.GetWindow(this)?.DataContext as FlyoutViewModel;
        var step = flyout?.ScrollStep ?? 2;
        var direction = Math.Sign(e.Delta) * (flyout?.InvertScroll == true ? -1 : 1);
        monitor.NudgeBrightness(direction * step);
        e.Handled = true;
    }

    private void OnContrastWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not MonitorViewModel monitor || !monitor.SupportsContrast)
        {
            return;
        }

        var flyout = Window.GetWindow(this)?.DataContext as FlyoutViewModel;
        var step = flyout?.ScrollStep ?? 2;
        var direction = Math.Sign(e.Delta) * (flyout?.InvertScroll == true ? -1 : 1);
        monitor.Contrast = Math.Clamp(monitor.Contrast + direction * step, 0, 100);
        e.Handled = true;
    }

    private void OnPercentGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private void OnPercentKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape && sender is TextBox textBox)
        {
            if (e.Key == Key.Enter)
            {
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
            else
            {
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }

            e.Handled = true;
        }
    }

    private void OnOrientationClick(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { DataContext: MonitorViewModel monitor, Tag: string tag } ||
            !Enum.TryParse<DisplayOrientation>(tag, out var orientation))
        {
            return;
        }

        monitor.SelectedOrientation = orientation;
    }
}
