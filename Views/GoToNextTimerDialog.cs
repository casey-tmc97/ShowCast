using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ShowCast.Views;

/// <summary>Dialog for configuring a slide's auto-advance timer with optional loop-to-start.</summary>
public class GoToNextTimerDialog : Window
{
    private readonly TextBox  _input;
    private readonly CheckBox _loopCheckBox;
    private (string? Duration, bool LoopToStart)? _result;

    public GoToNextTimerDialog(string prefill = "", bool loopToStart = false)
    {
        Title     = "Go to Next Timer";
        Width     = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Avalonia.Media.Brushes.Transparent;

        _input = new TextBox
        {
            Watermark = "Duration in seconds (e.g. 5 or 2.5, 0 = off)",
            Text = prefill,
            Margin = new Thickness(0, 0, 0, 10),
            Background = Avalonia.Media.SolidColorBrush.Parse("#3a3a3a"),
            Foreground = Avalonia.Media.Brushes.White,
            BorderBrush = Avalonia.Media.SolidColorBrush.Parse("#555555"),
            FontSize = 14,
            Height = 40,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        _loopCheckBox = new CheckBox
        {
            Content = "Loop to beginning of show",
            IsChecked = loopToStart,
            Foreground = Avalonia.Media.Brushes.White,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 14)
        };

        var ok = new Button
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Avalonia.Media.SolidColorBrush.Parse("#555555"),
            Foreground = Avalonia.Media.Brushes.White,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Width = 80, Height = 35, CornerRadius = new CornerRadius(5),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var cancel = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 8, 0),
            Background = Avalonia.Media.SolidColorBrush.Parse("#3a3a3a"),
            Foreground = Avalonia.Media.Brushes.White,
            Width = 80, Height = 35, CornerRadius = new CornerRadius(5),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        ok.Click     += (_, _) => { _result = (_input.Text, _loopCheckBox.IsChecked == true); Close(); };
        cancel.Click += (_, _) => Close();
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                _result = (_input.Text, _loopCheckBox.IsChecked == true);
                Close();
                e.Handled = true;
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                _result = (_input.Text, _loopCheckBox.IsChecked == true);
                Close();
                e.Handled = true;
            }
        };

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, ok }
        };

        Content = new Border
        {
            Background = Avalonia.Media.SolidColorBrush.Parse("#2d2d2d"),
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "Duration in seconds (e.g. 5 or 2.5, 0 = off):",
                        Foreground = Avalonia.Media.Brushes.White,
                        FontSize = 14,
                        Margin = new Thickness(0, 0, 0, 8)
                    },
                    _input,
                    _loopCheckBox,
                    btnRow
                }
            }
        };
    }

    public Task<(string? Duration, bool LoopToStart)?> ShowAsync(Window? owner)
    {
        var tcs = new TaskCompletionSource<(string? Duration, bool LoopToStart)?>();
        Closed += (_, _) => tcs.SetResult(_result);
        if (owner is not null) ShowDialog(owner); else Show();
        return tcs.Task;
    }
}
