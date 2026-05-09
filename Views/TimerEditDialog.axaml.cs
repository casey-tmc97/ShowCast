using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ShowCast.Core;

namespace ShowCast.Views;

public partial class TimerEditDialog : Window
{
    public TimerDef? Result { get; private set; }

    readonly TimerDef? _existing;
    TimerType _type = TimerType.Counter;

    public TimerEditDialog(TimerDef? existing = null)
    {
        InitializeComponent();
        _existing = existing;

        if (existing is not null)
            LoadFrom(existing);
        else
            SetType(TimerType.Counter);
    }

    void LoadFrom(TimerDef def)
    {
        NameBox.Text = def.Name;
        SetType(def.Type);

        if (def.Type == TimerType.Counter)
        {
            FromBox.Text        = FormatMSS(def.StartSeconds);
            ToBox.Text          = FormatMSS(def.EndSeconds);
            WarnCheck.IsChecked = def.WarnEnabled;
            WarnOffsetBox.Text  = def.WarnOffset.ToString();
            OverflowCheck.IsChecked = def.OverflowEnabled;
        }
        else
        {
            ClockTimeBox.Text = def.ClockTime;
        }
    }

    void SetType(TimerType type)
    {
        _type = type;
        CounterPanel.IsVisible = type == TimerType.Counter;
        ClockPanel.IsVisible   = type == TimerType.Clock;

        var active   = new SolidColorBrush(Color.Parse("#4da6ff"));
        var inactive = new SolidColorBrush(Color.Parse("#3a3a3a"));
        TypeCounterBtn.Background = type == TimerType.Counter ? active : inactive;
        TypeClockBtn.Background   = type == TimerType.Clock   ? active : inactive;
    }

    void OnTypeCounter(object? sender, RoutedEventArgs e) => SetType(TimerType.Counter);
    void OnTypeClock  (object? sender, RoutedEventArgs e) => SetType(TimerType.Clock);

    void OnSave(object? sender, RoutedEventArgs e)
    {
        var def    = _existing ?? new TimerDef();
        def.Name   = NameBox.Text?.Trim() is { Length: > 0 } n ? n : "Timer";
        def.Type   = _type;

        if (_type == TimerType.Counter)
        {
            def.StartSeconds    = ParseMSS(FromBox.Text ?? "5:00");
            def.EndSeconds      = ParseMSS(ToBox.Text   ?? "0:00");
            def.WarnEnabled     = WarnCheck.IsChecked == true;
            def.WarnOffset      = int.TryParse(WarnOffsetBox.Text, out int wo) ? Math.Max(0, wo) : 30;
            def.OverflowEnabled = OverflowCheck.IsChecked == true;
        }
        else
        {
            def.ClockTime = ClockTimeBox.Text?.Trim() ?? "12:00";
        }

        Result = def;
        Close();
    }

    void OnCancel(object? sender, RoutedEventArgs e) => Close();

    void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    static int ParseMSS(string s)
    {
        s = s.Trim();
        var parts = s.Split(':');
        if (parts.Length == 2
            && int.TryParse(parts[0], out int m)
            && int.TryParse(parts[1], out int sec))
            return Math.Abs(m) * 60 + Math.Abs(sec);
        return int.TryParse(s, out int total) ? Math.Abs(total) : 0;
    }

    static string FormatMSS(int seconds)
    {
        int abs = Math.Abs(seconds);
        return $"{abs / 60}:{abs % 60:00}";
    }
}
