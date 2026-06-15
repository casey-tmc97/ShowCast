using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ShowCast.Core;
using ShowCast.Engine;
using ShowCast.ViewModels;
using static ShowCast.Core.AltCodes;
using SkiaSharp;

namespace ShowCast.Views;

public sealed class CanvasTextEditor
{
    // ── Dependencies ──────────────────────────────────────────────────────────
    readonly SlideLayer              _layer;
    readonly Canvas                  _overlay;
    readonly Action                  _rebuildSlide;
    readonly Action<SpanFormatInfo>  _spanFormatChanged;
    readonly MainViewModel           _vm;

    // ── Saved originals for Cancel() ─────────────────────────────────────────
    readonly List<TextSpan> _origSpans;
    readonly string         _origText;

    // ── Editor state ─────────────────────────────────────────────────────────
    Rect  _imageRect;
    int   _cursorIndex;
    int   _selStart  = -1;
    int   _selEnd    = -1;
    bool  _pointerDown;

    // ── Layout ───────────────────────────────────────────────────────────────
    SpanLayoutResult? _layout;

    // ── Overlay visuals ───────────────────────────────────────────────────────
    readonly Line            _cursorLine;
    readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromMilliseconds(530) };
    readonly List<Rectangle> _selRects   = new();
    bool                     _blinkOn    = true;

    // ── IME input ─────────────────────────────────────────────────────────────
    TextBox? _imeBox;

    // ── Alt code accumulation (Windows Alt+NumPad character entry) ────────────
    bool _altAccumulating;
    bool _altExtended;          // leading-zero prefix → CP1252; otherwise → CP437
    int  _altCode;
    int  _altDigitCount;
    bool _suppressNextTextInput; // swallow the OS WM_CHAR that duplicates our insertion

    public CanvasTextEditor(
        SlideLayer              layer,
        Canvas                  overlay,
        Rect                    imageRect,
        Action                  rebuildSlide,
        Action<SpanFormatInfo>  spanFormatChanged,
        MainViewModel           vm)
    {
        _layer             = layer;
        _overlay           = overlay;
        _imageRect         = imageRect;
        _rebuildSlide      = rebuildSlide;
        _spanFormatChanged = spanFormatChanged;
        _vm                = vm;

        _origText  = layer.Text;
        _origSpans = layer.Spans.Select(s => new TextSpan
        {
            Text          = s.Text,          Bold          = s.Bold,
            Italic        = s.Italic,        FontSize      = s.FontSize,
            FontFamily    = s.FontFamily,    Color         = s.Color,
            Underline     = s.Underline,     Strikethrough = s.Strikethrough,
            Baseline      = s.Baseline,      Kerning       = s.Kerning,
        }).ToList();

        _cursorLine = new Line
        {
            Stroke           = new SolidColorBrush(Colors.White),
            StrokeThickness  = 2,
            IsHitTestVisible = false,
            IsVisible        = false,
        };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkOn = !_blinkOn;
            _cursorLine.IsVisible = _blinkOn;
        };
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Open(Point? clickPosition = null)
    {
        // Guard against double-open — clean up any existing state first
        if (_imeBox is not null) Cleanup();
        _vm.BeginLayerEdit();
        _overlay.Children.Add(_cursorLine);

        _layout      = SpanLayout.Compute(_layer, _imageRect);
        _cursorIndex = clickPosition.HasValue
            ? _layout.HitTest((float)clickPosition.Value.X, (float)clickPosition.Value.Y)
            : _layer.EffectiveText.Length;
        _selStart = _selEnd = -1;

        // IME box: off-screen TextBox purely for system IME composition
        _imeBox = new TextBox
        {
            Width            = 1, Height = 1, Opacity = 0.01,
            IsHitTestVisible = false,
            Background       = Brushes.Transparent,
            BorderThickness  = new Thickness(0),
            Padding          = new Thickness(0),
        };
        Canvas.SetLeft(_imeBox, -200);
        Canvas.SetTop (_imeBox, -200);
        _imeBox.AddHandler(InputElement.KeyDownEvent,   OnImeKeyDown,   RoutingStrategies.Bubble, handledEventsToo: true);
        _imeBox.AddHandler(InputElement.KeyUpEvent,     OnImeKeyUp,     RoutingStrategies.Bubble, handledEventsToo: true);
        _imeBox.AddHandler(InputElement.TextInputEvent, OnImeTextInput, RoutingStrategies.Bubble, handledEventsToo: true);
        _overlay.Children.Add(_imeBox);
        Dispatcher.UIThread.Post(() => _imeBox?.Focus());

        _blinkOn = true;
        _blinkTimer.Start();
        UpdateOverlayVisuals();
        FireSpanFormatChanged();
    }

    public void Commit()
    {
        _vm.NotifySlideChanged();
        Cleanup();
    }

    public void Cancel()
    {
        _layer.Text = _origText;
        _layer.Spans.Clear();
        foreach (var s in _origSpans) _layer.Spans.Add(s);
        _vm.NotifySlideChanged();   // close the BeginLayerEdit snapshot cleanly
        Cleanup();
        _rebuildSlide();
    }

    void Cleanup()
    {
        _blinkTimer.Stop();
        _layout?.Dispose();
        _layout = null;

        _overlay.Children.Remove(_cursorLine);
        try { foreach (var r in _selRects) _overlay.Children.Remove(r); }
        finally { _selRects.Clear(); }

        if (_imeBox is not null)
        {
            _imeBox.RemoveHandler(InputElement.KeyDownEvent,   OnImeKeyDown);
            _imeBox.RemoveHandler(InputElement.KeyUpEvent,     OnImeKeyUp);
            _imeBox.RemoveHandler(InputElement.TextInputEvent, OnImeTextInput);
            _overlay.Children.Remove(_imeBox);
            _imeBox = null;
        }

        _altAccumulating       = false;
        _altExtended           = false;
        _altCode               = 0;
        _altDigitCount         = 0;
        _suppressNextTextInput = false;
    }

    public void UpdateImageRect(Rect imageRect)
    {
        if (_imeBox is null) return;  // editor is closed; nothing to update
        _imageRect = imageRect;
        _layout?.Dispose();
        _layout = null;
        UpdateOverlayVisuals();
    }

    public SpanFormatInfo GetFormatAtCursor()
    {
        // When a selection is active, reflect the format of the selection start so inspector
        // buttons show the correct state after ApplyFormat runs.
        int pos = HasSelection() ? Math.Min(_selStart, _selEnd) : _cursorIndex;
        var f = SpanEditor.GetFormatAt(_layer, pos);
        return new SpanFormatInfo(f.bold, f.italic, f.fontSize, f.fontFamily,
                                  f.underline, f.strikethrough, f.baseline, f.kerning, f.color);
    }

    // ── Text mutation ─────────────────────────────────────────────────────────

    public void ApplyFormat(
        bool? bold = null, bool? italic = null, float? fontSize = null,
        string? fontFamily = null, bool? underline = null, bool? strikethrough = null,
        float? baseline = null, float? kerning = null, SKColor? color = null)
    {
        if (!HasSelection()) return;
        int start = Math.Min(_selStart, _selEnd);
        int end   = Math.Max(_selStart, _selEnd);
        SpanEditor.ApplyFormat(_layer, start, end, bold, italic, fontSize, fontFamily,
                               underline, strikethrough, baseline, kerning, color);
        RefreshAfterChange();
    }

    void InsertText(string text)
    {
        if (HasSelection()) DeleteSelection(fireRefresh: false);
        string oldText = _layer.EffectiveText;
        string newText = oldText.Insert(_cursorIndex, text);
        SpanEditor.ReconcileSpans(_layer, oldText, newText);
        _cursorIndex += text.Length;
        ClearSelection();
        RefreshAfterChange();
    }

    void DeleteBackward()
    {
        if (HasSelection()) { DeleteSelection(); return; }
        if (_cursorIndex == 0) return;
        string oldText = _layer.EffectiveText;
        string newText = oldText.Remove(_cursorIndex - 1, 1);
        _cursorIndex--;
        SpanEditor.ReconcileSpans(_layer, oldText, newText);
        RefreshAfterChange();
    }

    void DeleteForward()
    {
        if (HasSelection()) { DeleteSelection(); return; }
        string oldText = _layer.EffectiveText;
        if (_cursorIndex >= oldText.Length) return;
        string newText = oldText.Remove(_cursorIndex, 1);
        SpanEditor.ReconcileSpans(_layer, oldText, newText);
        RefreshAfterChange();
    }

    void DeleteSelection(bool fireRefresh = true)
    {
        if (!HasSelection()) return;
        int start = Math.Min(_selStart, _selEnd);
        int end   = Math.Max(_selStart, _selEnd);
        string oldText = _layer.EffectiveText;
        string newText = oldText.Remove(start, end - start);
        SpanEditor.ReconcileSpans(_layer, oldText, newText);
        _cursorIndex = start;
        ClearSelection();
        if (fireRefresh) RefreshAfterChange();
    }

    // ── Cursor movement ───────────────────────────────────────────────────────

    void MoveCursor(int newIndex, bool extending)
    {
        newIndex = Math.Clamp(newIndex, 0, _layer.EffectiveText.Length);
        if (extending)
        {
            if (!HasSelection()) _selStart = _cursorIndex;
            _selEnd = newIndex;
        }
        else
        {
            ClearSelection();
        }
        _cursorIndex = newIndex;
        ResetBlink();
        UpdateOverlayVisuals();
        FireSpanFormatChanged();
    }

    void MoveByLine(int direction, bool extending)
    {
        EnsureLayout();
        if (_layout!.Lines.Count == 0) return;
        int curLine    = _layout.GetLineIndex(_cursorIndex);
        int targetLine = curLine + direction;
        if (targetLine < 0)
        {
            MoveCursor(0, extending);
            return;
        }
        if (targetLine >= _layout.Lines.Count)
        {
            MoveCursor(_layer.EffectiveText.Length, extending);
            return;
        }
        var r   = _layout.GetCharRect(_cursorIndex);
        int idx = _layout.HitTest(r.Left,
            (float)(_layout.Lines[targetLine].Top + _layout.Lines[targetLine].Height / 2f));
        MoveCursor(idx, extending);
    }

    void SelectAll()
    {
        _selStart    = 0;
        _selEnd      = _layer.EffectiveText.Length;
        _cursorIndex = _selEnd;
        ResetBlink();
        UpdateOverlayVisuals();
        FireSpanFormatChanged();
    }

    void ToggleBold()
    {
        if (!HasSelection()) return;
        var f = SpanEditor.GetFormatAt(_layer, Math.Min(_selStart, _selEnd));
        ApplyFormat(bold: f.bold == true ? (bool?)false : true);
    }

    void ToggleItalic()
    {
        if (!HasSelection()) return;
        var f = SpanEditor.GetFormatAt(_layer, Math.Min(_selStart, _selEnd));
        ApplyFormat(italic: f.italic == true ? (bool?)false : true);
    }

    void ToggleUnderline()
    {
        if (!HasSelection()) return;
        var f = SpanEditor.GetFormatAt(_layer, Math.Min(_selStart, _selEnd));
        ApplyFormat(underline: f.underline == true ? (bool?)false : true);
    }

    // ── Pointer events ────────────────────────────────────────────────────────

    public void OnPointerPressed(Point pt)
    {
        EnsureLayout();
        int idx = _layout!.HitTest((float)pt.X, (float)pt.Y);
        _cursorIndex = idx;
        _selStart    = idx;
        _selEnd      = idx;
        _pointerDown = true;
        ResetBlink();
        UpdateOverlayVisuals();
        FireSpanFormatChanged();
        // Re-focus the IME box after cursor repositioning — Avalonia may have moved
        // focus to the canvas (which is Focusable=true) as part of pointer handling.
        Dispatcher.UIThread.Post(() => _imeBox?.Focus());
    }

    public void OnPointerMoved(Point pt, bool isDown)
    {
        if (!isDown || !_pointerDown) return;
        EnsureLayout();
        int idx = _layout!.HitTest((float)pt.X, (float)pt.Y);
        _selEnd      = idx;
        _cursorIndex = idx;
        UpdateOverlayVisuals();
    }

    public void OnPointerReleased()
    {
        _pointerDown = false;
        if (_selStart == _selEnd) ClearSelection();
        FireSpanFormatChanged();
    }

    // ── Keyboard ──────────────────────────────────────────────────────────────

    void OnImeKeyDown(object? sender, KeyEventArgs e) => OnKeyDown(e);

    void OnImeKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.LeftAlt && e.Key != Key.RightAlt) return;
        if (!_altAccumulating) return;

        // Extended mode needs at least the leading zero plus one more digit
        bool valid = _altExtended ? _altDigitCount > 1 : _altDigitCount > 0;
        if (valid)
        {
            char? c = ToChar(_altCode, _altExtended);
            if (c.HasValue)
            {
                InsertText(c.Value.ToString());
                _suppressNextTextInput = true;
            }
        }

        _altAccumulating = false;
        _altExtended     = false;
        _altCode         = 0;
        _altDigitCount   = 0;
        e.Handled        = true;
    }

    void OnImeTextInput(object? sender, TextInputEventArgs e)
    {
        if (_suppressNextTextInput)
        {
            _suppressNextTextInput = false;
            e.Handled = true;
            return;
        }
        if (string.IsNullOrEmpty(e.Text)) return;
        InsertText(e.Text);
        e.Handled = true;
    }

    public void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl  = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool alt   = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        // Alt+NumPad digit accumulation for Windows Alt codes
        if (alt && e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
        {
            int digit = (int)(e.Key - Key.NumPad0);
            if (!_altAccumulating)
            {
                _altAccumulating = true;
                _altExtended     = digit == 0;   // leading zero → CP1252
                _altCode         = digit;
                _altDigitCount   = 1;
            }
            else
            {
                _altCode = _altCode * 10 + digit;
                _altDigitCount++;
            }
            e.Handled = true;
            return;
        }

        // Non-numpad key while Alt is held cancels accumulation
        if (alt && _altAccumulating && e.Key != Key.LeftAlt && e.Key != Key.RightAlt)
        {
            _altAccumulating = false;
            _altExtended     = false;
            _altCode         = 0;
            _altDigitCount   = 0;
        }

        switch (e.Key)
        {
            case Key.Escape:
                Cancel();
                e.Handled = true;
                return;
            case Key.Back:
                DeleteBackward();
                e.Handled = true;
                return;
            case Key.Delete:
                DeleteForward();
                e.Handled = true;
                return;
            case Key.Enter:
                InsertText("\n");
                e.Handled = true;
                return;
            case Key.Left:
                EnsureLayout();
                MoveCursor(ctrl ? _layout!.GetWordStart(_cursorIndex) : _cursorIndex - 1, shift);
                e.Handled = true;
                return;
            case Key.Right:
                EnsureLayout();
                MoveCursor(ctrl ? _layout!.GetWordEnd(_cursorIndex) : _cursorIndex + 1, shift);
                e.Handled = true;
                return;
            case Key.Up:
                MoveByLine(-1, shift);
                e.Handled = true;
                return;
            case Key.Down:
                MoveByLine(+1, shift);
                e.Handled = true;
                return;
            case Key.Home:
                EnsureLayout();
                MoveCursor(ctrl ? 0 : _layout!.GetLineStart(_layout.GetLineIndex(_cursorIndex)), shift);
                e.Handled = true;
                return;
            case Key.End:
                EnsureLayout();
                MoveCursor(ctrl ? _layer.EffectiveText.Length
                               : _layout!.GetLineEnd(_layout.GetLineIndex(_cursorIndex)), shift);
                e.Handled = true;
                return;
            case Key.A when ctrl:
                SelectAll();
                e.Handled = true;
                return;
            case Key.B when ctrl:
                ToggleBold();
                e.Handled = true;
                return;
            case Key.I when ctrl:
                ToggleItalic();
                e.Handled = true;
                return;
            case Key.U when ctrl:
                ToggleUnderline();
                e.Handled = true;
                return;
        }
    }

    // ── Overlay visuals ───────────────────────────────────────────────────────

    void EnsureLayout()
    {
        _layout ??= SpanLayout.Compute(_layer, _imageRect);
    }

    void RefreshAfterChange()
    {
        _layout?.Dispose();
        _layout = null;
        _rebuildSlide();
        UpdateOverlayVisuals();
        FireSpanFormatChanged();
    }

    void UpdateOverlayVisuals()
    {
        EnsureLayout();
        UpdateCursorVisual();
        UpdateSelectionVisuals();
    }

    void UpdateCursorVisual()
    {
        if (_layout == null) { _cursorLine.IsVisible = false; return; }
        var r = _layout.GetCharRect(_cursorIndex);
        _cursorLine.StartPoint = new Point(r.Left, r.Top);
        _cursorLine.EndPoint   = new Point(r.Left, r.Bottom);
        _cursorLine.IsVisible  = _blinkOn;
    }

    void UpdateSelectionVisuals()
    {
        foreach (var r in _selRects) _overlay.Children.Remove(r);
        _selRects.Clear();
        if (!HasSelection() || _layout == null) return;

        int start = Math.Min(_selStart, _selEnd);
        int end   = Math.Max(_selStart, _selEnd);

        foreach (var rect in _layout.GetSelectionRects(start, end))
        {
            var vis = new Rectangle
            {
                Width            = Math.Max(4f, rect.Width),
                Height           = rect.Height,
                Fill             = new SolidColorBrush(Color.FromArgb(80, 59, 130, 246)),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(vis, rect.Left);
            Canvas.SetTop (vis, rect.Top);
            _overlay.Children.Insert(0, vis);
            _selRects.Add(vis);
        }
    }

    void ResetBlink()
    {
        _blinkTimer.Stop();
        _blinkOn              = true;
        _cursorLine.IsVisible = true;
        _blinkTimer.Start();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    bool HasSelection() => _selStart >= 0 && _selEnd >= 0 && _selStart != _selEnd;
    void ClearSelection() { _selStart = _selEnd = -1; }

    void FireSpanFormatChanged() => _spanFormatChanged(GetFormatAtCursor());

}
