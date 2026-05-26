using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ShowCast.Core;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class AudioSettingsDialog : Window
{
    readonly AudioSettingsViewModel _vm;

    // Radio cells: key = (channelVm, destinationId), value = the cell Border
    readonly Dictionary<(AudioChannelViewModel, Guid), RadioCell> _cells = new();

    public AudioSettingsDialog(MainViewModel mainVm)
    {
        InitializeComponent();
        _vm = new AudioSettingsViewModel(mainVm);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _vm.RefreshDevices();
        BuildMatrix();
    }

    // ── Matrix build ──────────────────────────────────────────────────────────

    void BuildMatrix()
    {
        _cells.Clear();
        MatrixGrid.Children.Clear();
        MatrixGrid.ColumnDefinitions.Clear();
        MatrixGrid.RowDefinitions.Clear();

        var channels     = _vm.Channels.ToList();
        var destinations = _vm.Destinations.ToList();

        if (channels.Count == 0 || destinations.Count == 0)
        {
            MatrixGrid.Children.Add(new TextBlock
            {
                Text       = destinations.Count == 0
                    ? "No audio devices found — click Refresh Devices."
                    : "No audio channels defined.",
                Foreground = new SolidColorBrush(Color.Parse("#666666")),
                FontSize   = 11,
                Margin     = new Thickness(16)
            });
            return;
        }

        // Column definitions: label column + one per destination
        MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition(120, GridUnitType.Pixel));
        foreach (var _ in destinations)
            MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition(90, GridUnitType.Pixel));

        // Row definitions: section header + device header + one per channel
        MatrixGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // HARDWARE/NDI section header
        MatrixGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // device name headers
        foreach (var _ in channels)
            MatrixGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        // ── Section header row (row 0) ────────────────────────────────────────
        int hwCount  = destinations.Count(d => d.Type == AudioRouteType.Hardware);
        int ndiCount = destinations.Count(d => d.Type == AudioRouteType.Ndi);

        if (hwCount > 0)
        {
            var hwHeader = MakeSectionLabel("HARDWARE", "#aaaaaa");
            Grid.SetRow(hwHeader,    0);
            Grid.SetColumn(hwHeader, 1);
            Grid.SetColumnSpan(hwHeader, hwCount);
            MatrixGrid.Children.Add(hwHeader);
        }
        if (ndiCount > 0)
        {
            var ndiHeader = MakeSectionLabel("NDI", "#4a9eff");
            Grid.SetRow(ndiHeader,    0);
            Grid.SetColumn(ndiHeader, 1 + hwCount);
            Grid.SetColumnSpan(ndiHeader, ndiCount);
            MatrixGrid.Children.Add(ndiHeader);
        }

        // ── Device name headers (row 1) ───────────────────────────────────────
        for (int col = 0; col < destinations.Count; col++)
        {
            var  dest      = destinations[col];
            bool isNdi     = dest.Type == AudioRouteType.Ndi;
            var  nameColor = isNdi ? Color.Parse("#4a9eff") : Color.Parse("#aaaaaa");
            var  sysColor  = Color.Parse("#555555");

            // Editable display name
            var nameBox = new TextBox
            {
                Text                        = dest.DisplayName,
                Background                  = Brushes.Transparent,
                BorderThickness             = new Thickness(0),
                Foreground                  = new SolidColorBrush(nameColor),
                FontSize                    = 10,
                IsReadOnly                  = isNdi,
                Padding                     = new Thickness(4, 2),
                HorizontalContentAlignment  = HorizontalAlignment.Center,
            };
            nameBox.Tag        = dest;
            nameBox.LostFocus += OnDestNameCommit;
            nameBox.KeyDown   += OnDestNameKeyDown;

            // System name label (read-only, below)
            var sysLabel = new TextBlock
            {
                Text          = dest.SystemName,
                Foreground    = new SolidColorBrush(sysColor),
                FontSize      = 8,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                Margin        = new Thickness(0, 0, 0, 4),
            };

            var headerStack = new StackPanel
            {
                Spacing             = 0,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            headerStack.Children.Add(nameBox);
            headerStack.Children.Add(sysLabel);

            var headerBorder = new Border
            {
                Child           = headerStack,
                Background      = new SolidColorBrush(Color.Parse("#1a1a1a")),
                BorderBrush     = new SolidColorBrush(Color.Parse("#333333")),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding         = new Thickness(4, 6),
            };
            Grid.SetRow(headerBorder,    1);
            Grid.SetColumn(headerBorder, col + 1);
            MatrixGrid.Children.Add(headerBorder);
        }

        // ── Channel rows ──────────────────────────────────────────────────────
        for (int row = 0; row < channels.Count; row++)
        {
            var ch = channels[row];

            // Channel label
            var label = new TextBlock
            {
                Text                = ch.Name,
                Foreground          = Brushes.White,
                FontSize            = 11,
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin              = new Thickness(10, 0),
            };
            var labelBorder = new Border
            {
                Child           = label,
                Background      = new SolidColorBrush(Color.Parse("#111111")),
                BorderBrush     = new SolidColorBrush(Color.Parse("#222222")),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Height          = 40,
            };
            Grid.SetRow(labelBorder,    row + 2);
            Grid.SetColumn(labelBorder, 0);
            MatrixGrid.Children.Add(labelBorder);

            // Radio cells
            for (int col = 0; col < destinations.Count; col++)
            {
                var  dest   = destinations[col];
                bool isNdi  = dest.Type == AudioRouteType.Ndi;
                bool active = ch.Model.ActiveDestinationId == dest.Id;

                var cell = new RadioCell(active);
                var cellBorder = new Border
                {
                    Child           = cell,
                    Background      = new SolidColorBrush(Color.Parse("#111111")),
                    BorderBrush     = new SolidColorBrush(Color.Parse("#222222")),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    IsEnabled       = !isNdi,
                    Opacity         = isNdi ? 0.35 : 1.0,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Height          = 40,
                    Cursor          = isNdi ? new Cursor(StandardCursorType.No)
                                           : new Cursor(StandardCursorType.Hand),
                };
                if (isNdi)
                    ToolTip.SetTip(cellBorder, "NDI audio routing available in next phase");

                var capturedCh   = ch;
                var capturedDest = dest;
                cellBorder.PointerPressed += (_, _) =>
                {
                    _vm.SetRoute(capturedCh, capturedDest);
                    RefreshCellStates(capturedCh);
                };

                _cells[(ch, dest.Id)] = cell;
                Grid.SetRow(cellBorder,    row + 2);
                Grid.SetColumn(cellBorder, col + 1);
                MatrixGrid.Children.Add(cellBorder);
            }
        }
    }

    /// <summary>Refreshes the filled/empty state of all cells in a channel's row.</summary>
    void RefreshCellStates(AudioChannelViewModel ch)
    {
        foreach (var kvp in _cells.Where(k => k.Key.Item1 == ch))
        {
            bool active = ch.Model.ActiveDestinationId == kvp.Key.Item2;
            kvp.Value.Update(active);
        }
    }

    static Border MakeSectionLabel(string text, string colorHex) => new()
    {
        Child = new TextBlock
        {
            Text              = text,
            Foreground        = new SolidColorBrush(Color.Parse(colorHex)),
            FontSize          = 9,
            FontWeight        = FontWeight.Bold,
            LetterSpacing     = 1,
            TextAlignment     = Avalonia.Media.TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
        Background          = new SolidColorBrush(Color.Parse("#1a1a1a")),
        BorderBrush         = new SolidColorBrush(Color.Parse("#333333")),
        BorderThickness     = new Thickness(0, 0, 0, 1),
        Padding             = new Thickness(4, 6),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    // ── Column header rename ──────────────────────────────────────────────────

    void OnDestNameCommit(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is AudioDestination dest)
            dest.DisplayName = tb.Text ?? dest.SystemName;
    }

    void OnDestNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && tb.Tag is AudioDestination dest)
        {
            dest.DisplayName = tb.Text ?? dest.SystemName;
            tb.IsReadOnly = true;  // blur effect
            tb.IsReadOnly = false;
        }
    }

    // ── Toolbar ───────────────────────────────────────────────────────────────

    void OnRefresh(object? sender, RoutedEventArgs e)
    {
        _vm.RefreshDevices();
        BuildMatrix();
    }

    void OnClose(object? sender, RoutedEventArgs e) => Close();
}

// ── Helper: radio-button circle ───────────────────────────────────────────────

internal class RadioCell : Border
{
    public RadioCell(bool active)
    {
        Width               = 16;
        Height              = 16;
        CornerRadius        = new Avalonia.CornerRadius(8);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment   = VerticalAlignment.Center;
        Update(active);
    }

    public void Update(bool active)
    {
        Background      = active
            ? new SolidColorBrush(Color.Parse("#e07050"))
            : Brushes.Transparent;
        BorderBrush     = new SolidColorBrush(active
            ? Color.Parse("#e07050")
            : Color.Parse("#444444"));
        BorderThickness = new Thickness(1);
    }
}
