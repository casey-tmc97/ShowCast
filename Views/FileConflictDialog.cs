using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System.Threading.Tasks;
using ShowCast.ViewModels;

namespace ShowCast.Views;

/// <summary>
/// Shown when an imported file already exists in the media folder.
/// Returns Reuse, Replace, or Skip.
/// </summary>
public class FileConflictDialog : Window
{
    FileConflictChoice _result = FileConflictChoice.Skip;

    public FileConflictDialog(string fileName)
    {
        Title     = "File Already Exists";
        Width     = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // ── Reuse button (primary) ───────────────────────────────────────────
        var reuse = new Button
        {
            Content    = "Reuse",
            Width      = 80, Height = 35, CornerRadius = new CornerRadius(5),
            Background = Avalonia.Media.SolidColorBrush.Parse("#3b82f6"),
            Foreground = Avalonia.Media.Brushes.White,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment   = VerticalAlignment.Center
        };
        reuse.Click += (_, _) => { _result = FileConflictChoice.Reuse; Close(); };

        // ── Replace button ───────────────────────────────────────────────────
        var replace = new Button
        {
            Content    = "Replace",
            Width      = 80, Height = 35, CornerRadius = new CornerRadius(5),
            Margin     = new Thickness(0, 0, 8, 0),
            Background = Avalonia.Media.SolidColorBrush.Parse("#c0392b"),
            Foreground = Avalonia.Media.Brushes.White,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment   = VerticalAlignment.Center
        };
        replace.Click += (_, _) => { _result = FileConflictChoice.Replace; Close(); };

        // ── Skip button ──────────────────────────────────────────────────────
        var skip = new Button
        {
            Content    = "Skip",
            Width      = 80, Height = 35, CornerRadius = new CornerRadius(5),
            Margin     = new Thickness(0, 0, 8, 0),
            Background = Avalonia.Media.SolidColorBrush.Parse("#3a3a3a"),
            Foreground = Avalonia.Media.Brushes.White,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment   = VerticalAlignment.Center
        };
        skip.Click += (_, _) => Close(); // _result stays Skip

        var btnRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing             = 0,
            Children            = { skip, replace, reuse }
        };

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
                        Text         = $"“{fileName}” already exists in the media folder.",
                        Foreground   = Avalonia.Media.Brushes.White,
                        FontSize     = 14,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text       = "Reuse — add a track pointing to the existing file\n" +
                                     "Replace — overwrite the existing file with the new one\n" +
                                     "Skip — do not add this file",
                        Foreground = Avalonia.Media.SolidColorBrush.Parse("#aaaaaa"),
                        FontSize   = 11,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    btnRow
                }
            }
        };
    }

    public Task<FileConflictChoice> ShowAsync(Window owner)
    {
        var tcs = new TaskCompletionSource<FileConflictChoice>();
        Closed += (_, _) => tcs.SetResult(_result);
        ShowDialog(owner);
        return tcs.Task;
    }
}
