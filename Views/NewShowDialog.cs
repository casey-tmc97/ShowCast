using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ShowCast.Core;

namespace ShowCast.Views;

public record NewShowResult(string Name, Show? TargetShow, Rundown? TargetRundown,
                            string? NewRundownName = null, string? NewShowFolderName = null);

/// <summary>Dialog for creating a new Package with show folder and optional rundown pickers.</summary>
public class NewShowDialog : Window
{
    private readonly TextBox   _nameInput;
    private readonly ComboBox  _showPicker;
    private readonly TextBox   _newShowFolderNameBox;
    private readonly Border    _newShowFolderRow;
    private readonly ComboBox  _rundownPicker;
    private readonly TextBox   _newRundownNameBox;
    private readonly Border    _newRundownRow;
    private static readonly string NewShowFolderSentinel = "+ New show folder...";
    private static readonly string NewRundownSentinel    = "+ New rundown...";
    private NewShowResult?     _result;

    public NewShowDialog(IEnumerable<Show> shows, IEnumerable<Rundown> rundowns,
                         Show? preselectedShow = null)
    {
        Title                 = "New Package";
        Width                 = 380;
        SizeToContent         = SizeToContent.Height;
        CanResize             = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background            = Brushes.Transparent;

        _nameInput = new TextBox
        {
            Watermark = "Package name",
            Margin    = new Thickness(0, 0, 0, 16),
            Background  = SolidColorBrush.Parse("#3a3a3a"),
            Foreground  = Brushes.White,
            BorderBrush = SolidColorBrush.Parse("#555555"),
            FontSize    = 14,
            Height      = 40,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        // Show folder picker — last item creates a new folder
        var showItems = new List<object>(shows.Cast<object>()) { NewShowFolderSentinel };
        _showPicker = new ComboBox
        {
            ItemsSource         = showItems,
            SelectedIndex       = showItems.Count > 1 ? 0 : showItems.Count - 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin              = new Thickness(0, 0, 0, 8),
            Background          = SolidColorBrush.Parse("#3a3a3a"),
            Foreground          = Brushes.White,
            BorderBrush         = SolidColorBrush.Parse("#555555"),
            Height              = 36
        };
        _showPicker.ItemTemplate = new FuncDataTemplate<object>((item, _) =>
            new TextBlock
            {
                Text       = item is Show s ? s.Name : item?.ToString() ?? "",
                Foreground = item is string str && str == NewShowFolderSentinel
                             ? SolidColorBrush.Parse("#e07050")
                             : Brushes.White,
                FontSize   = 13,
                VerticalAlignment = VerticalAlignment.Center
            });
        if (preselectedShow is not null && showItems.Contains(preselectedShow))
            _showPicker.SelectedItem = preselectedShow;

        _newShowFolderNameBox = new TextBox
        {
            Watermark   = "Show folder name",
            Background  = SolidColorBrush.Parse("#3a3a3a"),
            Foreground  = Brushes.White,
            BorderBrush = SolidColorBrush.Parse("#e07050"),
            FontSize    = 13,
            Height      = 34,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _newShowFolderRow = new Border
        {
            IsVisible = showItems.Count == 1, // auto-expand when no existing shows
            Margin    = new Thickness(0, 0, 0, 12),
            Child     = _newShowFolderNameBox
        };
        _showPicker.SelectionChanged += (_, _) =>
        {
            bool isNew = _showPicker.SelectedItem is string sentinel && sentinel == NewShowFolderSentinel;
            _newShowFolderRow.IsVisible = isNew;
            _showPicker.Margin = new Thickness(0, 0, 0, isNew ? 4 : 8);
        };

        // Rundown picker (optional — last item creates a new rundown)
        var rundownItems = new List<object> { "(none)" };
        foreach (var rd in rundowns) rundownItems.Add(rd);
        rundownItems.Add(NewRundownSentinel);
        _rundownPicker = new ComboBox
        {
            ItemsSource      = rundownItems,
            SelectedIndex    = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin           = new Thickness(0, 0, 0, 8),
            Background       = SolidColorBrush.Parse("#3a3a3a"),
            Foreground       = Brushes.White,
            BorderBrush      = SolidColorBrush.Parse("#555555"),
            Height           = 36
        };
        _rundownPicker.ItemTemplate = new FuncDataTemplate<object>((item, _) =>
            new TextBlock
            {
                Text       = item is Rundown rd ? rd.Name : item?.ToString() ?? "(none)",
                Foreground = item is string s && s == NewRundownSentinel
                             ? SolidColorBrush.Parse("#e07050")
                             : Brushes.White,
                FontSize   = 13,
                VerticalAlignment = VerticalAlignment.Center
            });

        _newRundownNameBox = new TextBox
        {
            Watermark   = "New rundown name",
            Background  = SolidColorBrush.Parse("#3a3a3a"),
            Foreground  = Brushes.White,
            BorderBrush = SolidColorBrush.Parse("#e07050"),
            FontSize    = 13,
            Height      = 34,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _newRundownRow = new Border
        {
            IsVisible = false,
            Margin    = new Thickness(0, 0, 0, 20),
            Child     = _newRundownNameBox
        };
        _rundownPicker.SelectionChanged += (_, _) =>
        {
            bool isNew = _rundownPicker.SelectedItem is string sentinel && sentinel == NewRundownSentinel;
            _newRundownRow.IsVisible = isNew;
            // Shrink bottom margin on picker when the name row is visible
            _rundownPicker.Margin = new Thickness(0, 0, 0, isNew ? 4 : 8);
        };

        var ok = new Button
        {
            Content = "Create",
            HorizontalAlignment = HorizontalAlignment.Right,
            Background  = SolidColorBrush.Parse("#c0392b"),
            Foreground  = Brushes.White,
            FontWeight  = FontWeight.Bold,
            Width = 90, Height = 35, CornerRadius = new CornerRadius(5),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment   = VerticalAlignment.Center
        };
        var cancel = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin      = new Thickness(0, 0, 8, 0),
            Background  = SolidColorBrush.Parse("#3a3a3a"),
            Foreground  = Brushes.White,
            Width = 80, Height = 35, CornerRadius = new CornerRadius(5),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment   = VerticalAlignment.Center
        };

        void CommitOk()
        {
            var name = _nameInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            // Resolve show folder
            bool isNewShowFolder = _showPicker.SelectedItem is string sf && sf == NewShowFolderSentinel;
            Show? targetShow = null;
            string? newShowFolderName = null;
            if (isNewShowFolder)
            {
                newShowFolderName = _newShowFolderNameBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(newShowFolderName)) { _newShowFolderNameBox.Focus(); return; }
            }
            else
            {
                targetShow = _showPicker.SelectedItem as Show;
                if (targetShow is null) return;
            }

            // Resolve rundown
            bool isNewRundown = _rundownPicker.SelectedItem is string sr && sr == NewRundownSentinel;
            Rundown? targetRundown = null;
            string? newRundownName = null;
            if (isNewRundown)
            {
                newRundownName = _newRundownNameBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(newRundownName)) { _newRundownNameBox.Focus(); return; }
            }
            else
            {
                targetRundown = _rundownPicker.SelectedItem as Rundown;
            }

            _result = new NewShowResult(name, targetShow, targetRundown, newRundownName, newShowFolderName);
            Close();
        }

        ok.Click     += (_, _) => CommitOk();
        cancel.Click += (_, _) => Close();
        _nameInput.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                CommitOk();
                e.Handled = true;
            }
        };

        Content = new Border
        {
            Background = SolidColorBrush.Parse("#2d2d2d"),
            Padding    = new Thickness(20),
            Child      = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Package name:", Foreground = Brushes.White,
                        FontSize = 13, Margin = new Thickness(0, 0, 0, 6) },
                    _nameInput,
                    new TextBlock { Text = "Show folder:", Foreground = SolidColorBrush.Parse("#aaaaaa"),
                        FontSize = 12, Margin = new Thickness(0, 0, 0, 6) },
                    _showPicker,
                    _newShowFolderRow,
                    new TextBlock { Text = "Add to rundown:", Foreground = SolidColorBrush.Parse("#aaaaaa"),
                        FontSize = 12, Margin = new Thickness(0, 0, 0, 6) },
                    _rundownPicker,
                    _newRundownRow,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancel, ok }
                    }
                }
            }
        };
    }

    public Task<NewShowResult?> ShowAsync(Window? owner)
    {
        var tcs = new TaskCompletionSource<NewShowResult?>();
        Closed += (_, _) => tcs.SetResult(_result);
        if (owner is not null) ShowDialog(owner); else Show();
        return tcs.Task;
    }
}
