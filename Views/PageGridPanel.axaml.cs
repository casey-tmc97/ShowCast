using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Threading.Tasks;
using Avalonia.VisualTree;
using ShowCast.Core;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class PageGridPanel : UserControl
{
    const string DragKey      = "ShowCast.Page";
    const string DragGroupKey = "ShowCast.PageGroup";
    const double DragThreshold = 8;

    PageViewModel?      _dragSrc;
    PageGroupViewModel? _dragSrcGroup;
    Point               _dragStart;

    // Flat list drop state
    PageViewModel? _flatDropPage;
    bool           _flatDropAfter;
    PageViewModel? _flatInsertTarget;

    // Grouped view drop state
    PageViewModel?      _groupDropPage;
    bool                _groupDropAfter;
    PageViewModel?      _groupInsertTarget;
    PageGroupViewModel? _groupInsertGroup;

    public PageGridPanel()
    {
        InitializeComponent();
        GroupedView.AddHandler(Button.ClickEvent, OnGroupedButtonClicked, RoutingStrategies.Bubble);

        PageList.AddHandler(DragDrop.DragOverEvent,  OnPageDragOver);
        PageList.AddHandler(DragDrop.DragLeaveEvent, (_, _) => { ClearFlatDrop(); _flatInsertTarget = null; });
        PageList.AddHandler(DragDrop.DropEvent,      OnPageDrop);

        GroupedView.AddHandler(DragDrop.DragOverEvent,  OnGroupedDragOver);
        GroupedView.AddHandler(DragDrop.DragLeaveEvent, (_, _) => { ClearGroupDrop(); });
        GroupedView.AddHandler(DragDrop.DropEvent,      OnGroupedDrop);
    }

    // ── Drag start — flat grid ────────────────────────────────────────────────

    void OnPageListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(null).Properties;
        if (props.IsRightButtonPressed)
        {
            // Pre-select on right mouse-down so the compositor renders the selection
            // border before ContextRequested fires at pointer-up.
            var rPos = e.GetPosition(PageList);
            var hitVisual = PageList.InputHitTest(rPos) as Visual ?? e.Source as Visual;
            var pvm = FindPageViewModel(hitVisual);
            if (pvm is not null)
            {
                PageList.SelectedItem = pvm;
                if (VM is { } vm) vm.SelectedPage = pvm;
                PageList.Focus();
            }
            return;
        }
        if (!props.IsLeftButtonPressed) return;
        _dragSrc      = FindPageViewModel(e.Source as Control);
        _dragSrcGroup = null;
        _dragStart    = e.GetPosition(PageList);
    }

    async void OnPageListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSrc is null) return;
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) { _dragSrc = null; return; }
        var pos = e.GetPosition(PageList);
        if (Math.Abs(pos.X - _dragStart.X) < DragThreshold &&
            Math.Abs(pos.Y - _dragStart.Y) < DragThreshold) return;

        var src = _dragSrc;
        _dragSrc = null;
        var data = new DataObject();
        data.Set(DragKey, src);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        ClearDropIndicator();
    }

    void OnPageListPointerReleased(object? sender, PointerReleasedEventArgs e) => _dragSrc = null;

    // ── Drag start — grouped view ─────────────────────────────────────────────

    void OnGroupedViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(null).Properties;
        if (props.IsRightButtonPressed)
        {
            var rPos = e.GetPosition(GroupedView);
            var hitVisual = GroupedView.InputHitTest(rPos) as Visual ?? e.Source as Visual;
            var pvm = FindPageViewModel(hitVisual);
            if (pvm is not null)
            {
                if (VM is { } vm) vm.SelectFromGroup(pvm);
                GroupedView.Focus();
            }
            return;
        }
        if (!props.IsLeftButtonPressed) return;
        _dragSrc      = FindPageViewModel(e.Source as Control);
        _dragSrcGroup = FindGroupViewModel(e.Source as Visual);
        _dragStart    = e.GetPosition(GroupedView);
    }

    async void OnGroupedViewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSrc is null || _dragSrcGroup is null) return;
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) { _dragSrc = null; return; }
        var pos = e.GetPosition(GroupedView);
        if (Math.Abs(pos.X - _dragStart.X) < DragThreshold &&
            Math.Abs(pos.Y - _dragStart.Y) < DragThreshold) return;

        var src      = _dragSrc;
        var srcGroup = _dragSrcGroup;
        _dragSrc      = null;
        _dragSrcGroup = null;
        var data = new DataObject();
        data.Set(DragKey,      src);
        data.Set(DragGroupKey, srcGroup);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        ClearDropIndicator();
    }

    void OnGroupedViewPointerReleased(object? sender, PointerReleasedEventArgs e) => _dragSrc = null;

    // ── Drag over / drop — flat grid ──────────────────────────────────────────

    void OnPageDragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DragKey)) { e.DragEffects = DragDropEffects.None; return; }
        e.DragEffects = DragDropEffects.Move;

        var hoveredPvm = FindPageViewModel(e.Source as Control);
        if (hoveredPvm is null) { ClearFlatDrop(); return; }

        var vm = VM;
        if (vm is null) { ClearFlatDrop(); return; }

        // Determine left vs right half of the hovered item
        var container = FindListBoxItem(e.Source as Visual);
        bool leftHalf = true;
        if (container is not null)
        {
            var pos = e.GetPosition(container);
            leftHalf = pos.X < container.Bounds.Width / 2.0;
        }

        PageViewModel? newDropPage;
        bool           newDropAfter;
        PageViewModel? newInsertTarget;

        if (leftHalf)
        {
            newDropPage     = hoveredPvm;
            newDropAfter    = false;
            newInsertTarget = hoveredPvm;
        }
        else
        {
            int idx = vm.Pages.IndexOf(hoveredPvm);
            if (idx >= 0 && idx < vm.Pages.Count - 1)
            {
                newDropPage     = vm.Pages[idx + 1];
                newDropAfter    = false;
                newInsertTarget = vm.Pages[idx + 1];
            }
            else
            {
                // Right half of last page → append
                newDropPage     = hoveredPvm;
                newDropAfter    = true;
                newInsertTarget = null;
            }
        }

        if (_flatDropPage != newDropPage || _flatDropAfter != newDropAfter)
        {
            ClearFlatDrop();
            _flatDropPage  = newDropPage;
            _flatDropAfter = newDropAfter;
            if (newDropPage is not null)
            {
                if (newDropAfter) newDropPage.ShowInsertAfter  = true;
                else              newDropPage.ShowInsertBefore = true;
            }
        }
        _flatInsertTarget = newInsertTarget;

        e.Handled = true;
    }

    void OnPageDrop(object? sender, DragEventArgs e)
    {
        var target = _flatInsertTarget;
        ClearFlatDrop();
        _flatInsertTarget = null;

        if (VM is null || !e.Data.Contains(DragKey)) return;
        if (e.Data.Get(DragKey) is not PageViewModel src) return;
        if (target == src) return;

        VM.MovePage(src, target);
        e.Handled = true;
    }

    // ── Drag over / drop — grouped view ───────────────────────────────────────

    void OnGroupedDragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DragKey)) { e.DragEffects = DragDropEffects.None; return; }
        e.DragEffects = DragDropEffects.Move;

        var hoveredPvm = FindPageViewModel(e.Source as Control);
        if (hoveredPvm is null) { ClearGroupDrop(); return; }

        var vm = VM;
        if (vm is null) { ClearGroupDrop(); return; }

        var tgtGroup = vm.PageGroups.FirstOrDefault(g => g.Pages.Contains(hoveredPvm));
        if (tgtGroup is null) { ClearGroupDrop(); return; }

        // Only allow reorder within same group
        if (e.Data.Get(DragGroupKey) is PageGroupViewModel srcGroup && srcGroup != tgtGroup)
        {
            ClearGroupDrop();
            return;
        }

        // Determine left vs right half using the page card border
        var card = FindPageCardBorder(e.Source as Visual);
        bool leftHalf = true;
        if (card is not null)
        {
            var pos = e.GetPosition(card);
            leftHalf = pos.X < card.Bounds.Width / 2.0;
        }

        PageViewModel? newDropPage;
        bool           newDropAfter;
        PageViewModel? newInsertTarget;

        if (leftHalf)
        {
            newDropPage     = hoveredPvm;
            newDropAfter    = false;
            newInsertTarget = hoveredPvm;
        }
        else
        {
            int idx = tgtGroup.Pages.IndexOf(hoveredPvm);
            if (idx >= 0 && idx < tgtGroup.Pages.Count - 1)
            {
                newDropPage     = tgtGroup.Pages[idx + 1];
                newDropAfter    = false;
                newInsertTarget = tgtGroup.Pages[idx + 1];
            }
            else
            {
                newDropPage     = hoveredPvm;
                newDropAfter    = true;
                newInsertTarget = null;
            }
        }

        if (_groupDropPage != newDropPage || _groupDropAfter != newDropAfter)
        {
            ClearGroupDrop();
            _groupDropPage  = newDropPage;
            _groupDropAfter = newDropAfter;
            if (newDropPage is not null)
            {
                if (newDropAfter) newDropPage.ShowInsertAfter  = true;
                else              newDropPage.ShowInsertBefore = true;
            }
        }
        _groupInsertTarget = newInsertTarget;
        _groupInsertGroup  = tgtGroup;

        e.Handled = true;
    }

    void OnGroupedDrop(object? sender, DragEventArgs e)
    {
        var group  = _groupInsertGroup;
        var target = _groupInsertTarget;
        ClearGroupDrop();

        if (VM is null || !e.Data.Contains(DragKey)) return;
        if (e.Data.Get(DragKey) is not PageViewModel src) return;
        if (group is null || target == src) return;

        VM.MovePageInGroup(group, src, target);
        e.Handled = true;
    }

    // ── Drop indicator ────────────────────────────────────────────────────────

    void ClearDropIndicator() { ClearFlatDrop(); ClearGroupDrop(); }

    void ClearFlatDrop()
    {
        if (_flatDropPage is not null)
        {
            if (_flatDropAfter) _flatDropPage.ShowInsertAfter  = false;
            else                _flatDropPage.ShowInsertBefore = false;
            _flatDropPage = null;
        }
    }

    void ClearGroupDrop()
    {
        if (_groupDropPage is not null)
        {
            if (_groupDropAfter) _groupDropPage.ShowInsertAfter  = false;
            else                 _groupDropPage.ShowInsertBefore = false;
            _groupDropPage = null;
        }
        _groupInsertTarget = null;
        _groupInsertGroup  = null;
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    static ListBoxItem? FindListBoxItem(Visual? v)
    {
        while (v is not null)
        {
            if (v is ListBoxItem lbi) return lbi;
            v = v.GetVisualParent();
        }
        return null;
    }

    static Border? FindPageCardBorder(Visual? v)
    {
        while (v is not null)
        {
            if (v is Border b && b.Classes.Contains("page-card")) return b;
            v = v.GetVisualParent();
        }
        return null;
    }

    MainViewModel? VM => DataContext as MainViewModel;

    void OnClear(object? sender, RoutedEventArgs e) => VM?.ClearLive();

    void OnPageTapped(object? sender, TappedEventArgs e)
    {
        VM?.GoLive();
        PageList.Focus();
    }

    void OnPageContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (VM is null) return;

        var hitVisual = e.TryGetPosition(PageList, out var pos)
            ? PageList.InputHitTest(pos) as Visual
            : e.Source as Visual;

        var pvm = FindPageViewModel(hitVisual);
        if (pvm is null) { e.Handled = true; return; }

        e.Handled = true;
        var anchor = e.Source as Control ?? (Control)sender!;

        PageList.SelectedItem = pvm;
        VM.SelectedPage = pvm;
        PageList.Focus();

        _ = ShowPageContextMenuAsync(pvm, anchor);
    }

    async Task ShowPageContextMenuAsync(PageViewModel pvm, Control anchor)
    {
        // Task.Delay yields the UI thread so Avalonia's compositor thread can render
        // the selection border before the menu popup opens. Dispatcher.Post at Background
        // priority is not sufficient because the compositor runs asynchronously.
        await Task.Delay(16);

        var editItem = new MenuItem { Header = "Edit" };
        editItem.Click += (_, _) => VM?.OpenEditor(pvm);

        var goLiveItem = new MenuItem { Header = "Go Live" };
        goLiveItem.Click += (_, _) => { if (VM is { } vm) { vm.SelectedPage = pvm; vm.GoLive(); } };

        var copyItem = new MenuItem { Header = "Copy", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) };
        copyItem.Click += (_, _) => VM?.CopyPage(pvm);

        var cutItem = new MenuItem { Header = "Cut", InputGesture = new KeyGesture(Key.X, KeyModifiers.Control) };
        cutItem.Click += (_, _) => VM?.CutPage(pvm);

        var pasteItem = new MenuItem
        {
            Header = "Paste",
            InputGesture = new KeyGesture(Key.V, KeyModifiers.Control),
            IsEnabled = VM?.HasClipboardPage == true
        };
        pasteItem.Click += (_, _) => VM?.PastePage();

        var duplicateItem = new MenuItem { Header = "Duplicate" };
        duplicateItem.Click += (_, _) => VM?.DuplicatePage(pvm);

        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) => VM?.RemovePage(pvm);

        var setTimerItem = new MenuItem
        {
            Header = pvm.HasTimer
                ? $"Change Timer ({pvm.TimerLabel})…"
                : "Set Go-to-Next Timer…"
        };
        setTimerItem.Click += async (_, _) =>
        {
            var prefill = pvm.Model.DurationMs > 0
                ? (pvm.Model.DurationMs / 1000.0).ToString("F1")
                : "";

            // Snapshot scroll positions before the modal dialog steals focus.
            var flatSv    = PageList.FindDescendantOfType<ScrollViewer>();
            var groupedSv = GroupedView.FindDescendantOfType<ScrollViewer>();
            double flatOffset    = flatSv?.Offset.Y    ?? 0;
            double groupedOffset = groupedSv?.Offset.Y ?? 0;

            var dialog = new GoToNextTimerDialog(prefill, pvm.Model.LoopToStart);
            var result = await dialog.ShowAsync(TopLevel.GetTopLevel(this) as Window);

            // Restore scroll positions after the dialog closes.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                flatSv?.SetCurrentValue(ScrollViewer.OffsetProperty,
                    new Avalonia.Vector(0, flatOffset));
                groupedSv?.SetCurrentValue(ScrollViewer.OffsetProperty,
                    new Avalonia.Vector(0, groupedOffset));
            }, Avalonia.Threading.DispatcherPriority.Background);

            if (result is { } r && r.Duration is not null &&
                double.TryParse(r.Duration,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double secs) && secs >= 0)
                VM?.SetPageTimer(pvm, (int)(secs * 1000), r.LoopToStart);
        };

        var removeTimerItem = new MenuItem
        {
            Header = "Remove Timer",
            IsEnabled = pvm.HasTimer
        };
        removeTimerItem.Click += (_, _) => VM?.SetPageTimer(pvm, 0);

        var transItem = new MenuItem { Header = "Default Transition" };
        foreach (TransitionType tt in Enum.GetValues<TransitionType>())
        {
            var tItem  = new MenuItem { Header = tt.ToString() };
            var ttCopy = tt;
            tItem.Click += (_, _) =>
            {
                if (pvm.Model is { } page) page.Transition.Type = ttCopy;
            };
            transItem.Items.Add(tItem);
        }

        // Trigger → Timer submenu
        var triggerTimerItem = new MenuItem { Header = "Timer" };
        var availableTimers  = VM?.ShowFile.Timers ?? new System.Collections.Generic.List<ShowCast.Core.TimerDef>();
        if (availableTimers.Count == 0)
        {
            triggerTimerItem.Items.Add(new MenuItem { Header = "(no timers)", IsEnabled = false });
        }
        else
        {
            foreach (var def in availableTimers)
            {
                bool isChecked = pvm.Model.TriggerTimerIds.Contains(def.Id);
                var tItem = new MenuItem
                {
                    Header = (isChecked ? "✓ " : "   ") + def.Name
                };
                var defCopy = def;
                tItem.Click += (_, _) =>
                {
                    if (pvm.Model.TriggerTimerIds.Contains(defCopy.Id))
                        pvm.Model.TriggerTimerIds.Remove(defCopy.Id);
                    else
                        pvm.Model.TriggerTimerIds.Add(defCopy.Id);
                };
                triggerTimerItem.Items.Add(tItem);
            }
        }
        // Trigger → Audio → Playlist submenu
        var triggerAudioPlaylistItem = new MenuItem { Header = "Playlist" };
        var availablePlaylists = VM?.AudioChannels
            .SelectMany(c => c.Player.Playlists)
            .ToList();
        if (availablePlaylists is null || availablePlaylists.Count == 0)
        {
            triggerAudioPlaylistItem.Items.Add(new MenuItem { Header = "(no playlists)", IsEnabled = false });
        }
        else
        {
            foreach (var playlist in availablePlaylists)
            {
                bool playlistChecked = pvm.Model.TriggerAudioPlaylistId == playlist.Id;
                var pItem = new MenuItem
                {
                    Header = (playlistChecked ? "✓ " : "   ") + playlist.Name
                };
                var playlistCopy = playlist;

                // "(any track)" — assigns playlist only, clears specific track
                bool anyTrackChecked = playlistChecked && pvm.Model.TriggerAudioTrackId == Guid.Empty;
                var anyTrackItem = new MenuItem
                {
                    Header = (anyTrackChecked ? "✓ " : "   ") + "(any track)"
                };
                anyTrackItem.Click += (_, _) =>
                {
                    if (pvm.Model.TriggerAudioPlaylistId == playlistCopy.Id &&
                        pvm.Model.TriggerAudioTrackId == Guid.Empty)
                    {
                        // Clicking checked item clears all
                        pvm.Model.TriggerAudioPlaylistId = Guid.Empty;
                        pvm.Model.TriggerAudioTrackId    = Guid.Empty;
                    }
                    else
                    {
                        pvm.Model.TriggerAudioPlaylistId = playlistCopy.Id;
                        pvm.Model.TriggerAudioTrackId    = Guid.Empty;
                    }
                };
                pItem.Items.Add(anyTrackItem);

                // Individual tracks
                foreach (var track in playlistCopy.Tracks)
                {
                    bool trackChecked = playlistChecked && pvm.Model.TriggerAudioTrackId == track.Id;
                    var trackCopy = track;
                    var tItem = new MenuItem
                    {
                        Header = (trackChecked ? "✓ " : "   ") + trackCopy.Title
                    };
                    tItem.Click += (_, _) =>
                    {
                        if (pvm.Model.TriggerAudioPlaylistId == playlistCopy.Id &&
                            pvm.Model.TriggerAudioTrackId == trackCopy.Id)
                        {
                            // Clicking checked item clears all
                            pvm.Model.TriggerAudioPlaylistId = Guid.Empty;
                            pvm.Model.TriggerAudioTrackId    = Guid.Empty;
                        }
                        else
                        {
                            pvm.Model.TriggerAudioPlaylistId = playlistCopy.Id;
                            pvm.Model.TriggerAudioTrackId    = trackCopy.Id;
                        }
                    };
                    pItem.Items.Add(tItem);
                }

                triggerAudioPlaylistItem.Items.Add(pItem);
            }
        }

        var triggerAudioItem = new MenuItem { Header = "Audio" };
        triggerAudioItem.Items.Add(triggerAudioPlaylistItem);

        var triggerItem = new MenuItem { Header = "Trigger" };
        triggerItem.Items.Add(triggerTimerItem);
        triggerItem.Items.Add(triggerAudioItem);

        var menu = new ContextMenu();
        menu.Items.Add(editItem);
        menu.Items.Add(goLiveItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(copyItem);
        menu.Items.Add(cutItem);
        menu.Items.Add(pasteItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(duplicateItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(setTimerItem);
        menu.Items.Add(removeTimerItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(triggerItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(transItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(deleteItem);
        menu.Open(anchor);
    }

    static PageViewModel? FindPageViewModel(Visual? v)
    {
        while (v is not null)
        {
            if (v is Control c && c.DataContext is PageViewModel pvm) return pvm;
            v = v.GetVisualParent();
        }
        return null;
    }

    static PageGroupViewModel? FindGroupViewModel(Visual? v)
    {
        while (v is not null)
        {
            if (v is Control c && c.DataContext is PageGroupViewModel g) return g;
            v = v.GetVisualParent();
        }
        return null;
    }

    void OnGroupedButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (VM is null || e.Source is not Button btn) return;
        var group = FindGroupViewModel(btn);
        if (group is null) return;

        switch (btn.Tag as string)
        {
            case "add-page":
                VM.AddPageToGroup(group);
                break;
            case "edit-package":
                var firstPage = group.Pages.FirstOrDefault();
                if (firstPage is not null) VM.OpenEditor(firstPage);
                break;
            case "active":
                if (group.SelectedOutput?.Config.Type != OutputType.NDI)
                    (TopLevel.GetTopLevel(this) as MainWindow)?.ToggleOutputWindowFor(group.SelectedOutput);
                break;
            case "clear":
                if (group.SelectedOutput is not null)
                    VM.ClearOutput(group.SelectedOutput);
                break;
        }
        e.Handled = true;
    }

    void OnGroupedPageTapped(object? sender, TappedEventArgs e)
    {
        if (VM is null) return;
        // Button clicks bubble separately; ignore taps originating inside a button.
        if ((e.Source as Visual)?.FindAncestorOfType<Button>() is not null) return;
        var pvm = FindPageViewModel(e.Source as Control);
        if (pvm is null) return;
        VM.GoLiveFromGroup(pvm);
        GroupedView.Focus();
    }

    void OnGroupedPageContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (VM is null) return;

        var hitVisual = e.TryGetPosition(GroupedView, out var pos)
            ? GroupedView.InputHitTest(pos) as Visual
            : e.Source as Visual;

        var pvm = FindPageViewModel(hitVisual);
        if (pvm is null) { e.Handled = true; return; }

        e.Handled = true;
        var anchor = e.Source as Control ?? (Control)sender!;

        VM.SelectFromGroup(pvm);
        GroupedView.Focus();

        _ = ShowPageContextMenuAsync(pvm, anchor);
    }

    void OnPageKeyDown(object? sender, KeyEventArgs e)
    {
        if (VM is null) return;
        switch (e.Key)
        {
            case Key.Return:
                VM.GoLive();
                e.Handled = true;
                break;
            case Key.Space:
                VM.GoLiveAndAdvance();
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Up:
            {
                var pages = VM.Pages;
                int idx = VM.SelectedPage is { } p ? pages.IndexOf(p) : -1;
                if (idx > 0) VM.SelectedPage = pages[idx - 1];
                e.Handled = true;
                break;
            }
            case Key.Right:
            case Key.Down:
            {
                var pages = VM.Pages;
                int idx = VM.SelectedPage is { } p ? pages.IndexOf(p) : -1;
                if (idx >= 0 && idx < pages.Count - 1) VM.SelectedPage = pages[idx + 1];
                e.Handled = true;
                break;
            }
            case Key.Delete:
            case Key.Back:
                if (VM.SelectedPage is { } pvm)
                    VM.RemovePage(pvm);
                e.Handled = true;
                break;
        }
    }
}
