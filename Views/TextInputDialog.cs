using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ShowCast.Views;

/// <summary>Simple modal text-input dialog.</summary>
public class TextInputDialog : Window
{
    private readonly TextBox _input;
    private string? _result;

    public TextInputDialog(string title, string prompt, string prefill = "")
    {
        Title     = title;
        Width     = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Avalonia.Media.Brushes.Transparent;

        _input = new TextBox
        {
            Watermark = prompt,
            Text = prefill,
            Margin = new Thickness(0, 0, 0, 12),
            Background = Avalonia.Media.SolidColorBrush.Parse("#3a3a3a"),
            Foreground = Avalonia.Media.Brushes.White,
            BorderBrush = Avalonia.Media.SolidColorBrush.Parse("#555555"),
            FontSize = 14,
            Height = 40,
            VerticalContentAlignment = VerticalAlignment.Center
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

        ok.Click     += (_, _) => { _result = _input.Text; Close(); };
        cancel.Click += (_, _) => Close();
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                _result = _input.Text;
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
                        Text = prompt,
                        Foreground = Avalonia.Media.Brushes.White,
                        FontSize = 14,
                        Margin = new Thickness(0, 0, 0, 8)
                    },
                    _input,
                    btnRow
                }
            }
        };
    }

    public Task<string?> ShowAsync(Window? owner)
    {
        var tcs = new TaskCompletionSource<string?>();
        Closed += (_, _) => tcs.SetResult(_result);
        if (owner is not null) ShowDialog(owner); else Show();
        return tcs.Task;
    }
}
