using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ShowCast.Views;

public class UpdateAvailableDialog : Window
{
    public enum UpdateChoice { OK, RemindLater, SkipVersion, Dismissed }

    UpdateChoice _result = UpdateChoice.Dismissed;

    UpdateAvailableDialog(string latestVersion, string currentVersion)
    {
        Title                 = "Update Available";
        Width                 = 460;
        SizeToContent         = SizeToContent.Height;
        CanResize             = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var skip   = MakeButton("Skip This Version", "#3a3a3a", 130);
        var remind = MakeButton("Remind Me Later",   "#3a3a3a", 120);
        var ok     = MakeButton("OK",                "#555555",  80);

        skip.Click   += (_, _) => { _result = UpdateChoice.SkipVersion;  Close(); };
        remind.Click += (_, _) => { _result = UpdateChoice.RemindLater;  Close(); };
        ok.Click     += (_, _) => { _result = UpdateChoice.OK;           Close(); };

        Content = new Border
        {
            Background = SolidColorBrush.Parse("#2d2d2d"),
            Padding    = new Thickness(20),
            Child      = new StackPanel
            {
                Spacing  = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text       = $"ShowCast {latestVersion} is available",
                        Foreground = Brushes.White,
                        FontSize   = 15,
                        FontWeight = FontWeight.Bold
                    },
                    new TextBlock
                    {
                        Text         = $"You have version {currentVersion}. Would you like to download and install the update?",
                        Foreground   = Brushes.White,
                        FontSize     = 13,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation         = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing             = 8,
                        Children            = { skip, remind, ok }
                    }
                }
            }
        };
    }

    static Button MakeButton(string label, string bg, double width) => new()
    {
        Content                    = label,
        Width                      = width,
        Height                     = 35,
        CornerRadius               = new CornerRadius(5),
        Background                 = SolidColorBrush.Parse(bg),
        Foreground                 = Brushes.White,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment   = VerticalAlignment.Center
    };

    Task<UpdateChoice> ShowAsync(Window owner)
    {
        var tcs = new TaskCompletionSource<UpdateChoice>();
        Closed += (_, _) => tcs.SetResult(_result);
        ShowDialog(owner);
        return tcs.Task;
    }

    public static Task<UpdateChoice> ShowAsync(Window owner, string latestVersion, string currentVersion)
        => new UpdateAvailableDialog(latestVersion, currentVersion).ShowAsync(owner);
}
