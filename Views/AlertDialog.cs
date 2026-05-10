using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System.Threading.Tasks;

namespace ShowCast.Views;

public class AlertDialog : Window
{
    bool _confirmed;

    AlertDialog(string title, string message, bool hasCancel)
    {
        Title     = title;
        Width     = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var ok = new Button
        {
            Content     = hasCancel ? "Yes" : "OK",
            Width       = 80,
            Height      = 35,
            CornerRadius = new CornerRadius(5),
            Background  = Avalonia.Media.SolidColorBrush.Parse("#555555"),
            Foreground  = Avalonia.Media.Brushes.White,
            FontWeight  = Avalonia.Media.FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment   = VerticalAlignment.Center
        };

        ok.Click += (_, _) => { _confirmed = true; Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        if (hasCancel)
        {
            var cancel = new Button
            {
                Content     = "Cancel",
                Width       = 80,
                Height      = 35,
                CornerRadius = new CornerRadius(5),
                Background  = Avalonia.Media.SolidColorBrush.Parse("#3a3a3a"),
                Foreground  = Avalonia.Media.Brushes.White,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment   = VerticalAlignment.Center
            };
            cancel.Click += (_, _) => Close();
            buttons.Children.Add(cancel);
        }

        buttons.Children.Add(ok);

        Content = new Border
        {
            Background = Avalonia.Media.SolidColorBrush.Parse("#2d2d2d"),
            Padding    = new Thickness(20),
            Child      = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text       = message,
                        Foreground = Avalonia.Media.Brushes.White,
                        FontSize   = 14,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    buttons
                }
            }
        };
    }

    Task<bool> ShowAsync(Window owner)
    {
        var tcs = new TaskCompletionSource<bool>();
        Closed += (_, _) => tcs.SetResult(_confirmed);
        ShowDialog(owner);
        return tcs.Task;
    }

    public static Task ShowError(Window owner, string title, string message) =>
        new AlertDialog(title, message, hasCancel: false).ShowAsync(owner);

    public static Task<bool> ShowConfirm(Window owner, string title, string message) =>
        new AlertDialog(title, message, hasCancel: true).ShowAsync(owner);
}
