using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using ShowCast.Core;
using ShowCast.Engine;
using SkiaSharp;

namespace ShowCast.ViewModels;

public enum PageViewMode { Grid, Table }

public class MainViewModel : ViewModelBase
{
    // ── Show file ─────────────────────────────────────────────────────────────

    ShowFile _showFile = new();
    public  ShowFile ShowFile => _showFile;

    // ── Audio player ──────────────────────────────────────────────────────────

    public AudioPlayerViewModel AudioPlayer { get; } = new();

    readonly Dictionary<Guid, Package> _packageById = new();

    // ── Session persistence ───────────────────────────────────────────────────

    public async Task SaveSessionAsync(string path)
    {
        var s = _showFile.Settings;
        s.ShowGrid               = ShowGrid;
        s.ShowRulers             = ShowRulers;
        s.GridSpacing            = GridSpacing;
        s.SnapToGrid             = SnapToGrid;
        s.ShowSafeBoundaries     = ShowSafeBoundaries;
        s.ThumbSize              = ThumbSize;
        s.ViewMode               = ViewMode.ToString();
        s.NextTransitionType     = NextTransitionType.ToString();
        s.NextTransitionDuration = NextTransitionDuration;
        s.SelectedOutputId       = SelectedOutput?.Config.Id ?? Guid.Empty;
        s.SelectedShowId         = SelectedShow?.Id          ?? Guid.Empty;
        s.SelectedRundownId      = SelectedRundown?.Id       ?? Guid.Empty;
        var selectedPackage      = SelectedPackageItemIndex >= 0 && SelectedPackageItemIndex < PackageItems.Count
                                   ? PackageItems[SelectedPackageItemIndex] : null;
        s.SelectedPackageItemId    = selectedPackage?.Id ?? Guid.Empty;
        s.SelectedAudioPlaylistId  = AudioPlayer.SelectedPlaylist?.Id ?? Guid.Empty;
        AudioPlayer.PersistPlaybackState();

        // Sync AudioPlayer playlists back to the ShowFile model before saving
        _showFile.AudioPlaylists.Clear();
        foreach (var pl in AudioPlayer.Playlists)
            _showFile.AudioPlaylists.Add(pl);

        await ShowFileSerializer.SaveAsync(_showFile, path);
    }

    /// <summary>Loads a session file, optionally prompting the user for migration or surfacing errors.</summary>
    /// <param name="confirmMigration">
    /// If provided, called when the file needs migration. Return true to proceed, false to cancel.
    /// If null, migration is applied automatically (headless/test callers).
    /// </param>
    /// <param name="showError">
    /// If provided, called when an error message should be displayed to the user.
    /// If null, errors are swallowed (headless/test callers).
    /// </param>
    public async Task<bool> LoadSessionAsync(
        string path,
        Func<Task<bool>>? confirmMigration = null,
        Func<string, Task>? showError = null)
    {
        LoadResult? loadResult;
        try
        {
            loadResult = await ShowFileSerializer.LoadAsync(path);
        }
        catch (ShowFileVersionTooNewException ex)
        {
            if (showError is not null)
                await showError(ex.Message);
            return false;
        }

        if (loadResult is null) return false;

        if (loadResult.NeedsMigration)
        {
            if (confirmMigration is not null)
            {
                bool proceed = await confirmMigration();
                if (!proceed) return false;
            }
            ShowFileSerializer.ApplyMigration(loadResult.File);
        }

        PageRenderer.ClearImageCache();
        _showFile = loadResult.File;
        MigratePageNames(_showFile);
        RebuildFromShowFile();
        RestoreSettings();
        return true;
    }

    void RestoreSettings()
    {
        var s = _showFile.Settings;
        ShowGrid           = s.ShowGrid;
        ShowRulers         = s.ShowRulers;
        GridSpacing        = s.GridSpacing;
        SnapToGrid         = s.SnapToGrid;
        ShowSafeBoundaries = s.ShowSafeBoundaries;
        ThumbSize          = s.ThumbSize;
        if (Enum.TryParse<PageViewMode>(s.ViewMode, out var vm))   ViewMode           = vm;
        if (Enum.TryParse<TransitionType>(s.NextTransitionType, out var tt)) NextTransitionType = tt;
        NextTransitionDuration = s.NextTransitionDuration;

        if (s.SelectedOutputId != Guid.Empty)
            SelectedOutput = OutputStates.FirstOrDefault(o => o.Config.Id == s.SelectedOutputId)
                             ?? SelectedOutput;
        if (s.SelectedShowId != Guid.Empty)
            SelectedShow = Shows.FirstOrDefault(l => l.Id == s.SelectedShowId)
                           ?? SelectedShow;
        if (s.SelectedRundownId != Guid.Empty)
            SelectedRundown = ShowFile.Rundowns.FirstOrDefault(r => r.Id == s.SelectedRundownId);

        if (s.SelectedPackageItemId != Guid.Empty)
        {
            int idx = PackageItems.IndexOf(PackageItems.FirstOrDefault(p => p.Id == s.SelectedPackageItemId)!);
            if (idx >= 0) SelectedPackageItemIndex = idx;
        }
    }

    public void NewFile()
    {
        PageRenderer.ClearImageCache();
        _showFile = new ShowFile();
        RebuildFromShowFile();
        SeedDemoContent();
    }

    void RebuildFromShowFile()
    {
        _pageOrderHistory.Clear();
        StopPageTimer();
        StartSchedulerTimer();
        StopAllNdiSenders();
        foreach (var t in Timers) t.Dispose();
        Timers.Clear();
        foreach (var o in OutputStates) o.Clear();
        OutputStates.Clear();
        Shows.Clear();
        RundownTree.Clear();
        PackageItems.Clear();
        Pages.Clear();
        PageGroups.Clear();
        _selectedOutput  = null; this.RaisePropertyChanged(nameof(SelectedOutput));
        _selectedShow    = null; this.RaisePropertyChanged(nameof(SelectedShow));
        _selectedRundown = null; this.RaisePropertyChanged(nameof(SelectedRundown));
        SelectedPage     = null;
        IsEditorOpen     = false;
        EditingPage      = null;
        SelectedLayer    = null;
        EditingLayers.Clear();

        _packageById.Clear();
        foreach (var show in _showFile.Shows)
            foreach (var pkg in show.Packages)
                _packageById[pkg.Id] = pkg;

        foreach (var def in _showFile.Timers)
            Timers.Add(new TimerViewModel(def));

        AudioPlayer.LoadPlaylists(
            _showFile.AudioPlaylists,
            _showFile.Settings.SelectedAudioPlaylistId);

        foreach (var cfg in _showFile.Outputs)
        {
            var state = new OutputState(cfg);
            if (cfg.ActivePackageId != Guid.Empty)
                state.ActivePackage = _packageById.TryGetValue(cfg.ActivePackageId, out var activePkg) ? activePkg : null;
            OutputStates.Add(state);
        }

        foreach (var show in _showFile.Shows)
            Shows.Add(show);

        SelectedOutput = OutputStates.Count > 0 ? OutputStates[0] : null;
        SelectedShow   = Shows.Count  > 0 ? Shows[0]  : null;
        BuildRundownTree();

        foreach (var o in OutputStates)
            StartNdiFor(o);

        OutputStates.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (OutputState o in e.NewItems) StartNdiFor(o);
            if (e.OldItems is not null)
                foreach (OutputState o in e.OldItems) StopNdiFor(o);
        };
    }

    // ── Timers ────────────────────────────────────────────────────────────────

    public ObservableCollection<TimerViewModel> Timers { get; } = new();

    public void AddTimer(TimerDef def)
    {
        ShowFile.Timers.Add(def);
        Timers.Add(new TimerViewModel(def));
    }

    public void RemoveTimer(TimerViewModel tvm)
    {
        tvm.Pause();
        tvm.Dispose();
        ShowFile.Timers.Remove(tvm.Def);
        Timers.Remove(tvm);
    }

    // ── Outputs ───────────────────────────────────────────────────────────────

    public ObservableCollection<OutputState> OutputStates { get; } = new();

    // ── NDI senders ───────────────────────────────────────────────────────────

    readonly Dictionary<Guid, ShowCast.Core.NdiSender> _ndiSenders = new();

    void StartNdiFor(OutputState o)
    {
        if (o.Config.Type != OutputType.NDI || !o.Config.Enabled) return;
        if (!NewTek.NDIlib.IsAvailable) return;
        _ndiSenders[o.Config.Id] = new ShowCast.Core.NdiSender(o);
    }

    void StopNdiFor(OutputState o)
    {
        if (_ndiSenders.Remove(o.Config.Id, out var sender))
            sender.Dispose();
    }

    void StopAllNdiSenders()
    {
        foreach (var s in _ndiSenders.Values) s.Dispose();
        _ndiSenders.Clear();
    }
    public ObservableCollection<PageGroupViewModel> PageGroups { get; } = new();

    private OutputState? _selectedOutput;
    public OutputState? SelectedOutput
    {
        get => _selectedOutput;
        set { this.RaiseAndSetIfChanged(ref _selectedOutput, value); RefreshPageList(); }
    }

    // ── Page grid (center panel) ──────────────────────────────────────────────

    public ObservableCollection<PageViewModel> Pages { get; } = new();

    private PageViewModel? _selectedPage;
    public PageViewModel? SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (value == _selectedPage) return;
            if (_selectedPage is not null) _selectedPage.IsSelected = false;
            this.RaiseAndSetIfChanged(ref _selectedPage, value);
            if (_selectedPage is not null) _selectedPage.IsSelected = true;
        }
    }

    // ── Left panel: Show list ─────────────────────────────────────────────────

    public ObservableCollection<Show> Shows { get; } = new();

    private Show? _selectedShow;
    public Show? SelectedShow
    {
        get => _selectedShow;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedShow, value);
            // Shows and Rundowns are mutually exclusive selections.
            if (value is not null && _selectedRundown is not null)
            {
                _selectedRundown = null;
                this.RaisePropertyChanged(nameof(SelectedRundown));
            }
            this.RaisePropertyChanged(nameof(ItemsHeader));
            this.RaisePropertyChanged(nameof(ShowingShow));
            this.RaisePropertyChanged(nameof(ShowingRundown));
            this.RaisePropertyChanged(nameof(ShowingFlatGrid));
            RefreshPackageItems();
        }
    }

    // ── Left panel: Rundown tree ──────────────────────────────────────────────

    /// <summary>Flat ordered tree of folder and rundown nodes for the sidebar.</summary>
    public ObservableCollection<PlaylistTreeItem> RundownTree { get; } = new();

    // Keep PlaylistTree as alias for AXAML bindings
    public ObservableCollection<PlaylistTreeItem> PlaylistTree => RundownTree;

    private Rundown? _selectedRundown;
    public Rundown? SelectedRundown
    {
        get => _selectedRundown;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedRundown, value);
            // Rundowns and Shows are mutually exclusive selections.
            if (value is not null && _selectedShow is not null)
            {
                _selectedShow = null;
                this.RaisePropertyChanged(nameof(SelectedShow));
            }
            this.RaisePropertyChanged(nameof(ItemsHeader));
            this.RaisePropertyChanged(nameof(ShowingShow));
            this.RaisePropertyChanged(nameof(ShowingRundown));
            this.RaisePropertyChanged(nameof(ShowingFlatGrid));
            RefreshPackageItems();
        }
    }

    // Keep SelectedPlaylist as alias
    public Rundown? SelectedPlaylist
    {
        get => _selectedRundown;
        set => SelectedRundown = value;
    }

    // ── Items panel ───────────────────────────────────────────────────────────

    public ObservableCollection<Package> PackageItems { get; } = new();

    private int _selectedPackageItemIndex = -1;
    public int SelectedPackageItemIndex
    {
        get => _selectedPackageItemIndex;
        set => this.RaiseAndSetIfChanged(ref _selectedPackageItemIndex, value);
    }

    // Keep ShowItems and SelectedShowItemIndex as aliases for AXAML bindings
    public ObservableCollection<Package> ShowItems => PackageItems;
    public int SelectedShowItemIndex
    {
        get => SelectedPackageItemIndex;
        set => SelectedPackageItemIndex = value;
    }

    public bool ShowingShow     => _selectedShow    is not null;
    public bool ShowingRundown  => _selectedRundown is not null;
    public bool ShowingFlatGrid => !ShowingRundown;

    // Keep ShowingLibrary/ShowingPlaylist aliases for AXAML bindings
    public bool ShowingLibrary  => ShowingShow;
    public bool ShowingPlaylist => ShowingRundown;

    public string ItemsHeader =>
        _selectedRundown is not null ? _selectedRundown.Name :
        _selectedShow    is not null ? _selectedShow.Name    :
        string.Empty;

    // ── Grid view settings ────────────────────────────────────────────────────

    private double _thumbSize = 160;
    public double ThumbSize
    {
        get => _thumbSize;
        set { this.RaiseAndSetIfChanged(ref _thumbSize, value); this.RaisePropertyChanged(nameof(ThumbHeight)); }
    }

    public double ThumbHeight => ThumbSize * 9.0 / 16.0;

    private PageViewMode _viewMode = PageViewMode.Grid;
    public PageViewMode ViewMode
    {
        get => _viewMode;
        set => this.RaiseAndSetIfChanged(ref _viewMode, value);
    }

    // ── Transition mode ───────────────────────────────────────────────────────

    public IReadOnlyList<TransitionType> TransitionTypes { get; } = Enum.GetValues<TransitionType>();

    private TransitionType _nextTransitionType = TransitionType.Cut;
    public TransitionType NextTransitionType
    {
        get => _nextTransitionType;
        set => this.RaiseAndSetIfChanged(ref _nextTransitionType, value);
    }

    private int _nextTransitionDuration = 500;
    public int NextTransitionDuration
    {
        get => _nextTransitionDuration;
        set => this.RaiseAndSetIfChanged(ref _nextTransitionDuration, value);
    }

    // ── Output actions ────────────────────────────────────────────────────────

    public void LoadPackageToSelectedOutput(Package package)
    {
        if (SelectedOutput is null) return;
        SelectedOutput.ActivePackage = package;
        RefreshPageList();
    }

    public void GoLive()
    {
        if (SelectedOutput is null || SelectedPage is null) return;
        bool skip = _skipNextAnimations;
        _skipNextAnimations = false;
        int index = SelectedOutput.ActivePackage?.Pages.IndexOf(SelectedPage.Model) ?? -1;
        SelectedOutput.GoLive(
            SelectedPage.Model, index,
            NextTransitionType,
            NextTransitionDuration,
            SelectedPage.Model.Transition.Easing,
            skip);
        UpdateIsLiveFlags();
        StartPageTimer(SelectedPage.Model.DurationMs, SelectedPage.Model.LoopToStart);
        FirePageTriggerTimers(SelectedPage.Model);
    }

    void FirePageTriggerTimers(Page page)
    {
        foreach (var id in page.TriggerTimerIds)
        {
            var tvm = Timers.FirstOrDefault(t => t.Def.Id == id);
            if (tvm is null || tvm.IsRunning) continue;
            tvm.Play();
        }
    }

    /// <summary>
    /// Space-bar advance logic:
    /// - If the operator arrow-keyed to a different page (selected ≠ live), fire that cued page.
    /// - Otherwise (selected == live, or nothing selected), advance to the next page after the
    ///   currently live one — so Space never re-fires the page that's already on screen.
    /// After firing, selection moves one step ahead so the next Space is pre-loaded.
    /// </summary>
    public void GoLiveAndAdvance()
    {
        if (Pages.Count == 0) return;

        var livePage = SelectedOutput?.LivePage;

        PageViewModel? toFire;

        if (SelectedPage is not null && SelectedPage.Model != livePage)
        {
            // Operator explicitly cued this page via arrow keys — fire it.
            toFire = SelectedPage;
        }
        else
        {
            // No pre-cue (or selected == live): advance one step past the live page.
            int liveIdx = livePage is not null
                ? Pages.IndexOf(Pages.FirstOrDefault(p => p.Model == livePage)!)
                : -1;
            int nextIdx = liveIdx + 1;
            if (nextIdx >= Pages.Count) return;   // already at the last page
            toFire = Pages[nextIdx];
        }

        bool wasPreCued = SelectedPage is not null && SelectedPage.Model != livePage;

        SelectedPage = toFire;
        GoLive();

        // Only advance selection when the operator explicitly pre-cued a page.
        // For plain Space-to-advance, leave selection on the page that just went live.
        if (wasPreCued)
        {
            int firedIdx = Pages.IndexOf(toFire);
            if (firedIdx >= 0 && firedIdx < Pages.Count - 1)
                SelectedPage = Pages[firedIdx + 1];
        }
    }

    public void SelectPreviousPage()
    {
        if (Pages.Count == 0) return;
        int idx = SelectedPage is { } p ? Pages.IndexOf(p) : -1;
        if (idx > 0) SelectedPage = Pages[idx - 1];
    }

    public void SelectNextPage()
    {
        if (Pages.Count == 0) return;
        int idx = SelectedPage is { } p ? Pages.IndexOf(p) : -1;
        if (idx < Pages.Count - 1) SelectedPage = Pages[idx + 1];
    }

    public void ClearLive()                    { StopPageTimer(); SelectedOutput?.Clear(); UpdateIsLiveFlags(); }
    public void ClearOutput(OutputState output) { StopPageTimer(); output.Clear(); UpdateIsLiveFlags(); }

    // ── Page timer (auto-advance) ──────────────────────────────────────────────

    System.Timers.Timer? _pageTimer;
    bool _skipNextAnimations;

    void StartPageTimer(int durationMs, bool loopToStart = false)
    {
        StopPageTimer();
        if (durationMs <= 0) return;
        _pageTimer = new System.Timers.Timer(durationMs) { AutoReset = false };
        _pageTimer.Elapsed += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _skipNextAnimations = true;
                if (loopToStart && Pages.Count > 0)
                {
                    SelectedPage = Pages[0];
                    GoLive();
                }
                else
                {
                    GoLiveAndAdvance();
                }
            });
        _pageTimer.Start();
    }

    void StopPageTimer()
    {
        _pageTimer?.Stop();
        _pageTimer?.Dispose();
        _pageTimer = null;
    }

    /// <summary>Set or clear the auto-advance timer on a page.</summary>
    public void SetPageTimer(PageViewModel? pvm, int durationMs, bool loopToStart = false)
    {
        if (pvm is null) return;
        pvm.DurationMs    = durationMs;
        pvm.LoopToStart   = loopToStart;
        // If this page is currently live, restart the timer immediately
        if (pvm.Model == SelectedOutput?.LivePage)
            StartPageTimer(durationMs, loopToStart);
    }

    // Keep old name as alias
    public void SetSlideTimer(PageViewModel? pvm, int durationMs, bool loopToStart = false)
        => SetPageTimer(pvm, durationMs, loopToStart);

    // ── Rundown scheduler (auto-fire) ─────────────────────────────────────────

    System.Timers.Timer? _schedulerTimer;

    // Arm a one-shot timer to fire at the exact moment of the next pending event.
    public void StartSchedulerTimer()
    {
        _schedulerTimer?.Stop();
        _schedulerTimer?.Dispose();
        _schedulerTimer = null;

        var next = _showFile.ScheduledEvents
            .Where(e => e.IsEnabled && !e.HasRun && e.ScheduledAt > DateTime.Now)
            .OrderBy(e => e.ScheduledAt)
            .FirstOrDefault();

        if (next is null) return;

        double delayMs = (next.ScheduledAt - DateTime.Now).TotalMilliseconds;
        if (delayMs < 0) delayMs = 0;

        _schedulerTimer = new System.Timers.Timer(delayMs) { AutoReset = false };
        _schedulerTimer.Elapsed += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(TickScheduler);
        _schedulerTimer.Start();
    }

    void TickScheduler()
    {
        var now    = DateTime.Now;
        var window = now.AddSeconds(-5);
        foreach (var evt in _showFile.ScheduledEvents
            .Where(e => e.IsEnabled && !e.HasRun && e.ScheduledAt <= now && e.ScheduledAt > window)
            .ToList())
        {
            _packageById.TryGetValue(evt.PackageId, out var pkg);
            if (pkg is not null && pkg.Pages.Count > 0)
            {
                var rundown = _showFile.Rundowns.FirstOrDefault(r => r.Id == evt.RundownId);
                var entry   = rundown?.Entries.FirstOrDefault(e => e.PackageId == evt.PackageId);
                var output  = (entry?.SelectedOutputId != Guid.Empty
                                  ? OutputStates.FirstOrDefault(o => o.Config.Id == entry!.SelectedOutputId)
                                  : null)
                              ?? SelectedOutput
                              ?? (OutputStates.Count > 0 ? OutputStates[0] : null);
                if (output is not null)
                {
                    output.ActivePackage = pkg;
                    var targetPage = (evt.PageId.HasValue
                                        ? pkg.Pages.FirstOrDefault(p => p.Id == evt.PageId.Value)
                                        : null)
                                     ?? pkg.Pages[0];
                    int targetIdx = pkg.Pages.IndexOf(targetPage);
                    output.GoLive(targetPage, targetIdx, TransitionType.Cut, 0, 0.5f);
                }
            }

            // Fire any associated timer actions
            foreach (var action in evt.TimerActions)
            {
                var tvm = Timers.FirstOrDefault(t => t.Def.Id == action.TimerId);
                if (tvm is null) continue;
                switch (action.Action)
                {
                    case TimerActionType.Play:     tvm.Play();      break;
                    case TimerActionType.Pause:    tvm.Pause();     break;
                    case TimerActionType.Reset:    tvm.Reset();     break;
                    case TimerActionType.PlayPause: tvm.PlayPause(); break;
                }
            }

            // For recurring events, advance to the next occurrence; for one-time, mark done.
            if (evt.Repeat == RepeatType.None)
            {
                evt.HasRun = true;
            }
            else
            {
                var next = NextOccurrence(evt, now);
                if (next.HasValue)
                {
                    evt.ScheduledAt = next.Value;
                    evt.HasRun      = false;
                }
                else
                {
                    evt.HasRun = true;
                }
            }
        }

        UpdateIsLiveFlags();
        RefreshPageList();
        StartSchedulerTimer();
    }

    static DateTime? NextOccurrence(ScheduledEvent evt, DateTime after)
    {
        var time      = evt.ScheduledAt.TimeOfDay;
        var startDate = evt.ScheduledAt.Date;
        var fromDate  = after.Date.AddDays(1);

        DateTime? result = evt.Repeat switch
        {
            RepeatType.Daily => NextDaily(startDate, fromDate, evt.RepeatInterval, time),
            RepeatType.Weekly => NextWeekly(startDate, fromDate, evt.RepeatInterval, evt.RepeatWeekDays, time),
            RepeatType.Monthly => NextMonthly(startDate, fromDate, evt.RepeatInterval, time),
            _ => null
        };

        if (result.HasValue && evt.RepeatUntil.HasValue && result.Value.Date > evt.RepeatUntil.Value.Date)
            return null;
        return result;
    }

    static DateTime? NextDaily(DateTime startDate, DateTime from, int interval, TimeSpan time)
    {
        int daysSince = (int)(from - startDate).TotalDays;
        int rem       = daysSince % Math.Max(1, interval);
        var next      = from.AddDays(rem == 0 ? 0 : interval - rem);
        return next + time;
    }

    static DateTime? NextWeekly(DateTime startDate, DateTime from, int interval, int weekDays, TimeSpan time)
    {
        for (int i = 0; i <= 14; i++)
        {
            var candidate = from.AddDays(i);
            bool dayMatch = weekDays == 0
                ? candidate.DayOfWeek == startDate.DayOfWeek
                : (weekDays & (1 << (int)candidate.DayOfWeek)) != 0;
            bool weekMatch = (int)(candidate - startDate).TotalDays / 7 % Math.Max(1, interval) == 0;
            if (dayMatch && weekMatch) return candidate + time;
        }
        return null;
    }

    static DateTime? NextMonthly(DateTime startDate, DateTime from, int interval, TimeSpan time)
    {
        var base_ = new DateTime(from.Year, from.Month, 1);
        if (from.Day > startDate.Day) base_ = base_.AddMonths(1);
        int monthsSince = base_.Year * 12 + base_.Month - (startDate.Year * 12 + startDate.Month);
        int rem         = monthsSince % Math.Max(1, interval);
        var targetMonth = base_.AddMonths(rem == 0 ? 0 : interval - rem);
        int day         = Math.Min(startDate.Day, DateTime.DaysInMonth(targetMonth.Year, targetMonth.Month));
        return new DateTime(targetMonth.Year, targetMonth.Month, day) + time;
    }

    public void AddOutput(string name = "New Output")
    {
        var cfg   = new OutputConfig { Name = name, RoleFilter = LayerRole.Program };
        ShowFile.AddOutput(cfg);
        var state = new OutputState(cfg);
        OutputStates.Add(state);
        SelectedOutput ??= state;
        if (ShowingRundown) RefreshPageGroups();
    }

    public void RemoveOutput(OutputState state)
    {
        if (OutputStates.Count <= 1) return;
        OutputStates.Remove(state);
        ShowFile.RemoveOutput(state.Config.Id);
        if (SelectedOutput == state)
            SelectedOutput = OutputStates.Count > 0 ? OutputStates[0] : null;
        if (ShowingRundown) RefreshPageGroups();
    }

    public void ClearAllOutputs() { foreach (var o in OutputStates) o.Clear(); UpdateIsLiveFlags(); }

    // ── Show management ───────────────────────────────────────────────────────

    public Show AddShow(string name)
    {
        var show = ShowFile.AddShow(name);
        Shows.Add(show);
        SelectedShow = show;
        return show;
    }

    // Keep AddLibrary alias
    public void AddLibrary(string name) => AddShow(name);

    public void RemoveSelectedShow()
    {
        if (_selectedShow is null) return;
        var show = _selectedShow;
        SelectedShow = null;
        foreach (var output in OutputStates)
            if (show.Packages.Contains(output.ActivePackage!)) { output.ActivePackage = null; output.Clear(); }
        ShowFile.RemoveShow(show.Id);
        Shows.Remove(show);
        RefreshPageList();
    }

    // Keep RemoveSelectedLibrary alias
    public void RemoveSelectedLibrary() => RemoveSelectedShow();

    // ── Package management (within selected show) ─────────────────────────────

    public void AddPackageToShow(string name, Show targetShow, Rundown? targetRundown = null)
    {
        var package = targetShow.AddPackage(name);
        _packageById[package.Id] = package;

        var page = new Page { Name = "1" };
        page.AddLayer(new SlideLayer
        {
            Type = LayerType.Background, Name = "Background",
            Color = new SKColor(20, 20, 20), Roles = LayerRole.All
        });
        package.AddPage(page);

        if (targetRundown is not null)
            targetRundown.AddEntry(new RundownEntry { PackageId = package.Id });

        RefreshPackageItems();
        LoadPackageToSelectedOutput(package);
    }

    // Keep old name as alias
    public void AddShowToLibrary(string name, Show targetShow, Rundown? targetRundown = null)
        => AddPackageToShow(name, targetShow, targetRundown);

    public void RemovePackageFromShow(Package package)
    {
        if (_selectedShow is null) return;
        foreach (var output in OutputStates)
            if (output.ActivePackage == package) { output.ActivePackage = null; output.Clear(); }
        foreach (var rd in ShowFile.Rundowns)
            rd.Entries.RemoveAll(e => e.PackageId == package.Id);
        _selectedShow.RemovePackage(package.Id);
        _packageById.Remove(package.Id);
        RefreshPackageItems();
        RefreshPageList();
    }

    // Keep old name as alias
    public void RemoveShowFromLibrary(Package package) => RemovePackageFromShow(package);

    // ── Rundown management ────────────────────────────────────────────────────

    /// <summary>Add a new rundown, optionally inside a folder.</summary>
    public Rundown AddRundown(string name, Guid? folderId = null)
    {
        var rd = ShowFile.AddRundown(name, folderId);
        BuildRundownTree();
        SelectedRundown = rd;
        return rd;
    }

    // Keep AddPlaylist alias
    public void AddPlaylist(string name, Guid? folderId = null) => AddRundown(name, folderId);

    /// <summary>Add a new folder, optionally nested inside another.</summary>
    public void AddRundownFolder(string name, Guid? parentId = null)
    {
        ShowFile.AddRundownFolder(name, parentId);
        BuildRundownTree();
    }

    // Keep AddPlaylistFolder alias
    public void AddPlaylistFolder(string name, Guid? parentId = null) => AddRundownFolder(name, parentId);

    public void RenameRundown(Rundown rd, string name)
    {
        rd.Name = name;
        BuildRundownTree();
    }

    // Keep RenamePlaylist alias
    public void RenamePlaylist(Rundown rd, string name) => RenameRundown(rd, name);

    public void RenameRundownFolder(RundownFolder folder, string name)
    {
        folder.Name = name;
        BuildRundownTree();
    }

    // Keep RenamePlaylistFolder alias
    public void RenamePlaylistFolder(RundownFolder folder, string name) => RenameRundownFolder(folder, name);

    public void RemoveSelectedRundown()
    {
        if (_selectedRundown is null) return;
        var rd = _selectedRundown;
        SelectedRundown = null;
        ShowFile.RemoveRundown(rd.Id);
        BuildRundownTree();
    }

    // Keep RemoveSelectedPlaylist alias
    public void RemoveSelectedPlaylist() => RemoveSelectedRundown();

    public void RemoveRundownFolder(RundownFolder folder)
    {
        ShowFile.RemoveRundownFolder(folder.Id);
        BuildRundownTree();
    }

    // Keep RemovePlaylistFolder alias
    public void RemovePlaylistFolder(RundownFolder folder) => RemoveRundownFolder(folder);

    /// <summary>Toggle a folder's expanded state and rebuild the visible tree.</summary>
    public void ToggleFolderExpanded(RundownFolder folder)
    {
        folder.IsExpanded = !folder.IsExpanded;
        BuildRundownTree();
    }

    // ── Rundown content management (from Items panel) ─────────────────────────

    public void RemovePackageFromRundown(int index)
    {
        if (_selectedRundown is null) return;
        if (index < 0 || index >= _selectedRundown.Entries.Count) return;
        _selectedRundown.RemoveEntry(index);
        RefreshPackageItems();
    }

    // Keep old name as alias
    public void RemoveShowFromPlaylist(int index) => RemovePackageFromRundown(index);

    public void MoveRundownItem(int direction)
    {
        if (_selectedRundown is null) return;
        int from = SelectedPackageItemIndex;
        int to   = from + direction;
        if (from < 0 || from >= _selectedRundown.Entries.Count) return;
        if (to   < 0 || to   >= _selectedRundown.Entries.Count) return;
        _selectedRundown.MoveEntry(from, to);
        RefreshPackageItems();
        SelectedPackageItemIndex = to;
    }

    public void MoveRundownEntry(int from, int to)
    {
        if (_selectedRundown is null) return;
        if (from < 0 || from >= _selectedRundown.Entries.Count) return;
        if (to   < 0 || to   >= _selectedRundown.Entries.Count) return;
        if (from == to) return;
        _selectedRundown.MoveEntry(from, to);
        RefreshPackageItems();
        SelectedPackageItemIndex = to;
    }

    // Keep old name as alias
    public void MovePlaylistItem(int direction) => MoveRundownItem(direction);

    // ── Page editor ───────────────────────────────────────────────────────────

    public ObservableCollection<SlideLayer>   EditingLayers { get; } = new();
    public ObservableCollection<PageViewModel> EditorPages  { get; } = new();

    private Package? _editingPackage;

    private bool _isEditorOpen;
    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        set
        {
            this.RaiseAndSetIfChanged(ref _isEditorOpen, value);
            RaiseHistoryChanged();
        }
    }

    private Page? _editingPage;
    public Page? EditingPage
    {
        get => _editingPage;
        set
        {
            this.RaiseAndSetIfChanged(ref _editingPage, value);
            EditingPageName = value?.Name ?? string.Empty;
        }
    }

    // Keep EditingSlide alias
    public Page? EditingSlide
    {
        get => EditingPage;
        set => EditingPage = value;
    }

    private string _editingPageName = string.Empty;
    public string EditingPageName
    {
        get => _editingPageName;
        set => this.RaiseAndSetIfChanged(ref _editingPageName, value);
    }

    // Keep EditingSlideName alias for AXAML binding
    public string EditingSlideName
    {
        get => EditingPageName;
        set => EditingPageName = value;
    }

    private SlideLayer? _selectedLayer;
    public SlideLayer? SelectedLayer
    {
        get => _selectedLayer;
        set => this.RaiseAndSetIfChanged(ref _selectedLayer, value);
    }

    // ── Editor display toggles ────────────────────────────────────────────────

    private bool _showGrid = true;
    public bool ShowGrid
    {
        get => _showGrid;
        set => this.RaiseAndSetIfChanged(ref _showGrid, value);
    }

    private bool _showRulers = true;
    public bool ShowRulers
    {
        get => _showRulers;
        set => this.RaiseAndSetIfChanged(ref _showRulers, value);
    }

    private int _gridSpacing = 100;
    public int GridSpacing
    {
        get => _gridSpacing;
        set => this.RaiseAndSetIfChanged(ref _gridSpacing, value);
    }

    private bool _snapToGrid = false;
    public bool SnapToGrid
    {
        get => _snapToGrid;
        set => this.RaiseAndSetIfChanged(ref _snapToGrid, value);
    }

    private bool _showSafeBoundaries = false;
    public bool ShowSafeBoundaries
    {
        get => _showSafeBoundaries;
        set => this.RaiseAndSetIfChanged(ref _showSafeBoundaries, value);
    }

    public event Action? OutputConfigsChanged;

    public void NotifyOutputConfigsChanged()
    {
        // Reconcile NDI senders: start any that are now NDI, stop any that are no longer NDI.
        foreach (var o in OutputStates)
        {
            bool hasSender = _ndiSenders.ContainsKey(o.Config.Id);
            if (o.Config.Type == OutputType.NDI && o.Config.Enabled && !hasSender)
                StartNdiFor(o);
            else if ((o.Config.Type != OutputType.NDI || !o.Config.Enabled) && hasSender)
                StopNdiFor(o);
        }
        OutputConfigsChanged?.Invoke();
    }

    public event Action? SlideContentChanged;
    public void NotifySlideChanged()
    {
        SlideContentChanged?.Invoke();
        // _editingPageVm is the authoritative instance currently open in the editor.
        // Also rebuild any other VM instances wrapping the same Page model (flat list
        // and grouped views hold separate instances).
        _editingPageVm?.RebuildThumbnail();
        foreach (var pvm in Pages.Where(p => p.Model == EditingPage && p != _editingPageVm))
            pvm.RebuildThumbnail();
        foreach (var group in PageGroups)
            foreach (var pvm in group.Pages.Where(p => p.Model == EditingPage && p != _editingPageVm))
                pvm.RebuildThumbnail();
    }

    // ── Undo / Redo ───────────────────────────────────────────────────────────

    readonly EditorHistory    _history          = new();
    readonly PageOrderHistory _pageOrderHistory = new();

    public bool CanUndo => IsEditorOpen ? _history.CanUndo : _pageOrderHistory.CanUndo;
    public bool CanRedo => IsEditorOpen ? _history.CanRedo : _pageOrderHistory.CanRedo;

    void RaiseHistoryChanged()
    {
        this.RaisePropertyChanged(nameof(CanUndo));
        this.RaisePropertyChanged(nameof(CanRedo));
    }

    /// <summary>Push a snapshot before any mutation (called by canvas/inspector).</summary>
    public void BeginLayerEdit()
    {
        if (EditingPage is not null)
            _history.Push(EditingPage);
        RaiseHistoryChanged();
    }

    public void Undo()
    {
        if (IsEditorOpen)
        {
            if (EditingPage is null) return;
            if (_history.Undo(EditingPage))
            {
                RefreshEditorLayers();
                SelectedLayer = null;
                NotifySlideChanged();
            }
        }
        else
        {
            var pkgs = _pageOrderHistory.Undo();
            if (pkgs is not null) foreach (var pkg in pkgs) SyncPageCollectionAfterOrderChange(pkg);
        }
        RaiseHistoryChanged();
    }

    public void Redo()
    {
        if (IsEditorOpen)
        {
            if (EditingPage is null) return;
            if (_history.Redo(EditingPage))
            {
                RefreshEditorLayers();
                SelectedLayer = null;
                NotifySlideChanged();
            }
        }
        else
        {
            var pkgs = _pageOrderHistory.Redo();
            if (pkgs is not null) foreach (var pkg in pkgs) SyncPageCollectionAfterOrderChange(pkg);
        }
        RaiseHistoryChanged();
    }

    void SyncPageCollectionAfterOrderChange(Package pkg)
    {
        RenameDefaultPages(pkg);
        var selectedModel = SelectedPage?.Model;
        var livePage      = SelectedOutput?.LivePage;

        if (SelectedOutput?.ActivePackage == pkg)
        {
            Pages.Clear();
            foreach (var page in pkg.Pages)
            {
                var pvm = new PageViewModel(page, pkg);
                pvm.IsLive = page == livePage;
                Pages.Add(pvm);
            }
            SelectedPage = Pages.FirstOrDefault(p => p.Model == selectedModel)
                           ?? (Pages.Count > 0 ? Pages[0] : null);
        }

        if (ShowingRundown)
        {
            var group = PageGroups.FirstOrDefault(g => g.Package == pkg);
            if (group is not null)
            {
                var groupLive = group.SelectedOutput?.LivePage;
                group.Pages.Clear();
                foreach (var page in pkg.Pages)
                {
                    var pvm = new PageViewModel(page, pkg);
                    pvm.IsLive = page == groupLive;
                    group.Pages.Add(pvm);
                }
            }
        }
    }

    private PageViewModel? _editingPageVm;

    private PageViewModel? _selectedEditorPage;
    public PageViewModel? SelectedEditorPage
    {
        get => _selectedEditorPage;
        set => this.RaiseAndSetIfChanged(ref _selectedEditorPage, value);
    }

    public void OpenEditor(PageViewModel? pvm)
    {
        if (pvm is null) return;
        _history.Clear();
        RaiseHistoryChanged();
        _editingPackage = pvm.Owner;
        RebuildEditorPages(pvm.Model);
        IsEditorOpen        = true;
        EditingPage         = pvm.Model;
        SelectedEditorPage  = _editingPageVm;
        RefreshEditorLayers();
        SelectedLayer = EditingLayers.Count > 0 ? EditingLayers[0] : null;
        NotifySlideChanged();
    }

    // Keep alias for old code
    public void OpenEditor(PageViewModel? pvm, bool _) => OpenEditor(pvm);

    /// <summary>Switch which page is being edited without leaving the editor.</summary>
    public void SwitchEditingPage(PageViewModel pvm)
    {
        if (_editingPageVm == pvm) return;
        if (_editingPageVm is not null)
        {
            _editingPageVm.RebuildThumbnail();
            Pages.FirstOrDefault(p => p.Model == _editingPageVm.Model)?.RebuildThumbnail();
        }
        _editingPageVm     = pvm;
        SelectedEditorPage = pvm;
        EditingPage        = pvm.Model;
        _history.Clear();
        RaiseHistoryChanged();
        RefreshEditorLayers();
        SelectedLayer = EditingLayers.Count > 0 ? EditingLayers[0] : null;
        NotifySlideChanged();
    }

    // Keep alias
    public void SwitchEditingSlide(PageViewModel pvm) => SwitchEditingPage(pvm);

    public void CloseEditor()
    {
        _history.Clear();
        RaiseHistoryChanged();
        IsEditorOpen = false;
        _editingPageVm     = null;
        SelectedEditorPage = null;
        EditingPage        = null;
        _editingPackage    = null;
        EditorPages.Clear();
        SelectedLayer   = null;
        EditingLayers.Clear();
        RefreshPageList();
    }

    void RebuildEditorPages(Page? current)
    {
        EditorPages.Clear();
        _editingPageVm = null;
        if (_editingPackage is null) return;
        foreach (var page in _editingPackage.Pages)
        {
            var vm = new PageViewModel(page, _editingPackage);
            EditorPages.Add(vm);
            if (page == current)
                _editingPageVm = vm;
        }
    }

    // Keep EditorSlides alias
    public ObservableCollection<PageViewModel> EditorSlides => EditorPages;

    public void AddTextLayer()
    {
        if (EditingPage is null) return;
        int maxZ = EditingPage.Layers.Count > 0 ? EditingPage.Layers.Max(l => l.ZOrder) : 0;
        var layer = new SlideLayer
        {
            Type = LayerType.Text, Name = "Text",
            X = 0.05f, Y = 0.3f, Width = 0.9f, Height = 0.4f,
            Text = "Text", Color = SKColors.White,
            FontSize = 0.08f, ZOrder = maxZ + 1, Roles = LayerRole.All
        };
        EditingPage.AddLayer(layer);
        RefreshEditorLayers();
        SelectedLayer = layer;
        NotifySlideChanged();
    }

    public void AddImageLayer(string path = "")
    {
        if (EditingPage is null) return;
        int maxZ = EditingPage.Layers.Count > 0 ? EditingPage.Layers.Max(l => l.ZOrder) : 0;
        var layer = new SlideLayer
        {
            Type      = LayerType.Image, Name = "Image",
            X         = 0.05f, Y = 0.05f, Width = 0.9f, Height = 0.9f,
            AssetPath = path,  ZOrder = maxZ + 1, Roles = LayerRole.All
        };
        EditingPage.AddLayer(layer);
        RefreshEditorLayers();
        SelectedLayer = layer;
        NotifySlideChanged();
    }

    public void AddShapeLayer()
    {
        if (EditingPage is null) return;
        int maxZ = EditingPage.Layers.Count > 0 ? EditingPage.Layers.Max(l => l.ZOrder) : 0;
        var layer = new SlideLayer
        {
            Type = LayerType.Shape, Name = "Shape",
            X = 0.2f, Y = 0.2f, Width = 0.3f, Height = 0.3f,
            Color = new SKColor(74, 123, 200), Opacity = 1f,
            ZOrder = maxZ + 1, Roles = LayerRole.All
        };
        EditingPage.AddLayer(layer);
        RefreshEditorLayers();
        SelectedLayer = layer;
        NotifySlideChanged();
    }

    public void ToggleLayerVisibility(SlideLayer layer)
    {
        layer.Visible = !layer.Visible;
        var saved = SelectedLayer;
        RefreshEditorLayers();
        SelectedLayer = saved;
        NotifySlideChanged();
    }

    public void DeleteLayer(SlideLayer layer)
    {
        if (SelectedLayer == layer) SelectedLayer = null;
        EditingPage?.RemoveLayer(layer.Id);
        RefreshEditorLayers();
        NotifySlideChanged();
    }

    public void MoveLayerUp(SlideLayer layer)
    {
        if (EditingPage is null) return;
        var layers = EditingPage.Layers.OrderBy(l => l.ZOrder).ToList();
        int idx    = layers.IndexOf(layer);
        if (idx < 0 || idx >= layers.Count - 1) return;
        BeginLayerEdit();
        (layers[idx].ZOrder, layers[idx + 1].ZOrder) = (layers[idx + 1].ZOrder, layers[idx].ZOrder);
        var saved = SelectedLayer;
        RefreshEditorLayers();
        SelectedLayer = saved;
        NotifySlideChanged();
    }

    public void MoveLayerDown(SlideLayer layer)
    {
        if (EditingPage is null) return;
        var layers = EditingPage.Layers.OrderBy(l => l.ZOrder).ToList();
        int idx    = layers.IndexOf(layer);
        if (idx <= 0) return;
        BeginLayerEdit();
        (layers[idx].ZOrder, layers[idx - 1].ZOrder) = (layers[idx - 1].ZOrder, layers[idx].ZOrder);
        var saved = SelectedLayer;
        RefreshEditorLayers();
        SelectedLayer = saved;
        NotifySlideChanged();
    }

    public void DuplicateLayer(SlideLayer? layer)
    {
        if (EditingPage is null || layer is null) return;
        BeginLayerEdit();
        int maxZ   = EditingPage.Layers.Max(l => l.ZOrder);
        var copy   = layer.Clone(newId: true);
        copy.ZOrder = maxZ + 1;
        copy.Name  += " Copy";
        copy.X     = Math.Clamp(copy.X + 0.02f, 0f, 0.98f);
        copy.Y     = Math.Clamp(copy.Y + 0.02f, 0f, 0.98f);
        EditingPage.AddLayer(copy);
        RefreshEditorLayers();
        SelectedLayer = copy;
        NotifySlideChanged();
    }

    public void AlignLayer(SlideLayer? layer, string alignment)
    {
        if (layer is null) return;
        BeginLayerEdit();
        switch (alignment)
        {
            case "Left":    layer.X = 0f; break;
            case "CenterH": layer.X = 0.5f - layer.Width / 2f; break;
            case "Right":   layer.X = 1f - layer.Width; break;
            case "Top":     layer.Y = 0f; break;
            case "CenterV": layer.Y = 0.5f - layer.Height / 2f; break;
            case "Bottom":  layer.Y = 1f - layer.Height; break;
        }
        NotifySlideChanged();
    }

    public void MoveLayerToFront(SlideLayer layer)
    {
        if (EditingPage is null) return;
        BeginLayerEdit();
        int maxZ = EditingPage.Layers.Max(l => l.ZOrder);
        layer.ZOrder = maxZ + 1;
        var saved = SelectedLayer;
        RefreshEditorLayers();
        SelectedLayer = saved;
        NotifySlideChanged();
    }

    public void MoveLayerToBack(SlideLayer layer)
    {
        if (EditingPage is null) return;
        BeginLayerEdit();
        int minZ = EditingPage.Layers.Min(l => l.ZOrder);
        layer.ZOrder = minZ - 1;
        var saved = SelectedLayer;
        RefreshEditorLayers();
        SelectedLayer = saved;
        NotifySlideChanged();
    }

    public void RefreshEditorLayersPublic() => RefreshEditorLayers();

    void RefreshEditorLayers()
    {
        EditingLayers.Clear();
        if (EditingPage is null) return;
        foreach (var l in EditingPage.Layers.OrderByDescending(ll => ll.ZOrder))
            EditingLayers.Add(l);
    }

    // ── Page actions ──────────────────────────────────────────────────────────

    public void AddPage()
    {
        var package = SelectedOutput?.ActivePackage;
        if (package is null) return;
        var page = new Page { Name = $"{package.Pages.Count + 1}" };
        page.AddLayer(new SlideLayer
        {
            Type = LayerType.Background, Name = "Background",
            Color = new SKColor(20, 20, 20), Roles = LayerRole.All
        });
        package.AddPage(page);
        var pvm = new PageViewModel(page, package);
        Pages.Add(pvm);
        RenameDefaultPages(package);
        SelectedPage = pvm;
    }

    // Keep AddSlide alias
    public void AddSlide() => AddPage();

    public void RenamePage(PageViewModel pvm, string name)
    {
        pvm.Model.Name = name;
        pvm.Refresh();
        if (EditingPage == pvm.Model)
            EditingPageName = name;
    }

    // Keep RenameSlide alias
    public void RenameSlide(PageViewModel pvm, string name) => RenamePage(pvm, name);

    public void DuplicatePage(PageViewModel? pvm)
    {
        var package = IsEditorOpen ? _editingPackage : (pvm?.Owner ?? SelectedOutput?.ActivePackage);
        if (package is null || pvm is null) return;

        var src  = pvm.Model;
        var copy = new Page { Name = "0" };  // placeholder; RenameDefaultPages fixes it below
        copy.Transition.Type       = src.Transition.Type;
        copy.Transition.DurationMs = src.Transition.DurationMs;
        copy.DurationMs            = src.DurationMs;
        copy.LoopToStart           = src.LoopToStart;
        foreach (var l in src.Layers)
            copy.AddLayer(l.Clone(newId: true));

        int idx = package.Pages.IndexOf(src);
        package.Pages.Insert(idx + 1, copy);

        var newVm = new PageViewModel(copy, package);
        if (IsEditorOpen)
        {
            int editorIdx = EditorPages.IndexOf(pvm);
            EditorPages.Insert(editorIdx >= 0 ? editorIdx + 1 : EditorPages.Count, newVm);
            RenameDefaultPages(package);
            SwitchEditingPage(newVm);
        }
        else
        {
            int pagesIdx = Pages.IndexOf(pvm);
            if (pagesIdx >= 0)
            {
                Pages.Insert(pagesIdx + 1, newVm);
                SelectedPage = newVm;
            }
            else
            {
                // pvm is from the grouped rundown view — insert into the group
                var group = PageGroups.FirstOrDefault(g => g.Pages.Contains(pvm));
                if (group is not null)
                {
                    int groupIdx = group.Pages.IndexOf(pvm);
                    group.Pages.Insert(groupIdx >= 0 ? groupIdx + 1 : group.Pages.Count, newVm);
                }
            }
            RenameDefaultPages(package);
            if (ShowingRundown && Pages.IndexOf(pvm) >= 0) RefreshPageGroups();
        }
    }

    // Keep DuplicateSlide alias
    public void DuplicateSlide(PageViewModel? pvm) => DuplicatePage(pvm);

    public void MovePageToPackage(PageViewModel pvm, Package targetPackage)
    {
        var sourcePackage = pvm.Owner;
        if (sourcePackage is null || sourcePackage == targetPackage) return;

        _pageOrderHistory.Push(sourcePackage, targetPackage);
        var movedModel = pvm.Model;

        sourcePackage.Pages.Remove(movedModel);
        var group = PageGroups.FirstOrDefault(g => g.Pages.Contains(pvm));
        group?.Pages.Remove(pvm);
        targetPackage.Pages.Add(movedModel);

        RenameDefaultPages(sourcePackage);
        RenameDefaultPages(targetPackage);

        // Defer the VM refresh so it runs after the drag-drop event cycle fully completes.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (ShowingRundown)
                RefreshPageGroups();
            else
                RefreshPageList();

            if (SelectedOutput?.ActivePackage == targetPackage)
                SelectedPage = Pages.FirstOrDefault(p => p.Model == movedModel);

            RaiseHistoryChanged();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    // ── Page clipboard ────────────────────────────────────────────────────────

    Page? _clipboardPage;
    public bool HasClipboardPage => _clipboardPage is not null;

    public void CopyPage(PageViewModel? pvm)
    {
        if (pvm is null) return;
        _clipboardPage = pvm.Model.Clone();
    }

    public void CutPage(PageViewModel? pvm)
    {
        if (pvm is null) return;
        var package = pvm.Owner ?? SelectedOutput?.ActivePackage;
        if (package is not null) _pageOrderHistory.Push(package);
        _clipboardPage = pvm.Model.Clone();
        RemovePage(pvm);
        RaiseHistoryChanged();
    }

    public void PastePage()
    {
        if (_clipboardPage is null) return;
        var package = IsEditorOpen ? _editingPackage : SelectedOutput?.ActivePackage;
        if (package is null) return;
        if (!IsEditorOpen) _pageOrderHistory.Push(package);

        var copy = _clipboardPage.Clone();
        int idx = SelectedPage is { } sel
            ? package.Pages.IndexOf(sel.Model)
            : package.Pages.Count - 1;
        package.Pages.Insert(idx + 1, copy);

        var newVm = new PageViewModel(copy, package);
        if (IsEditorOpen)
        {
            int editorIdx = SelectedEditorPage is { } selEd ? EditorPages.IndexOf(selEd) : EditorPages.Count - 1;
            EditorPages.Insert(editorIdx + 1, newVm);
            RenameDefaultPages(package);
            SwitchEditingPage(newVm);
        }
        else
        {
            int pagesIdx = SelectedPage is { } sel2 ? Pages.IndexOf(sel2) : Pages.Count - 1;
            Pages.Insert(pagesIdx + 1, newVm);
            SelectedPage = newVm;
            RenameDefaultPages(package);
        }
        RaiseHistoryChanged();
    }

    /// <summary>
    /// Move <paramref name="src"/> to just before <paramref name="tgt"/> in both the model
    /// and the VM page list.  Called by the editor's drag-to-reorder panel.
    /// When <paramref name="tgt"/> is null, src is appended to the end of the collection.
    /// </summary>
    public void MovePage(PageViewModel src, PageViewModel? tgt)
    {
        var package    = IsEditorOpen ? _editingPackage : SelectedOutput?.ActivePackage;
        var collection = IsEditorOpen ? EditorPages      : Pages;
        if (package is null) return;
        if (!IsEditorOpen) _pageOrderHistory.Push(package);

        int from = collection.IndexOf(src);
        if (from < 0) return;

        if (tgt is null)
        {
            // Append to end
            package.Pages.RemoveAt(from);
            package.Pages.Add(src.Model);
            collection.Move(from, collection.Count - 1);
            RenameDefaultPages(package);
            if (!IsEditorOpen && (SelectedPage == src || SelectedPage is null))
                SelectedPage = src;
            return;
        }

        int to = collection.IndexOf(tgt);
        if (to < 0 || from == to) return;

        int insertAt = to > from ? to - 1 : to;

        package.Pages.RemoveAt(from);
        package.Pages.Insert(insertAt, src.Model);
        collection.Move(from, insertAt);

        // Only update selection to the dragged page if it was already the selected page.
        // If a different page is selected (e.g., the live page during a timer sequence),
        // preserve that selection — otherwise the timer's next GoLiveAndAdvance() call
        // treats the dragged page as an operator pre-cue and fires it out of order.
        if (!IsEditorOpen && (SelectedPage == src || SelectedPage is null))
            SelectedPage = src;

        RenameDefaultPages(package);
    }

    // Keep MoveSlide alias
    public void MoveSlide(PageViewModel src, PageViewModel? tgt) => MovePage(src, tgt);

    public void AddPageToGroup(PageGroupViewModel group)
    {
        var package = group.Package;
        var page = new Page { Name = $"{package.Pages.Count + 1}" };
        page.AddLayer(new SlideLayer
        {
            Type = LayerType.Background, Name = "Background",
            Color = new SKColor(20, 20, 20), Roles = LayerRole.All
        });
        package.AddPage(page);
        var groupVm = new PageViewModel(page, package);
        group.Pages.Add(groupVm);
        RenameDefaultPages(package);
    }

    // Keep AddSlideToGroup alias
    public void AddSlideToGroup(PageGroupViewModel group) => AddPageToGroup(group);

    public void MovePageInGroup(PageGroupViewModel group, PageViewModel src, PageViewModel? tgt)
    {
        _pageOrderHistory.Push(group.Package);
        int from = group.Pages.IndexOf(src);
        if (from < 0) return;

        if (tgt is null)
        {
            group.Package.Pages.RemoveAt(from);
            group.Package.Pages.Add(src.Model);
            group.Pages.Move(from, group.Pages.Count - 1);
            RenameDefaultPages(group.Package);
            return;
        }

        int to = group.Pages.IndexOf(tgt);
        if (to < 0 || from == to) return;

        int insertAt = to > from ? to - 1 : to;

        group.Package.Pages.RemoveAt(from);
        group.Package.Pages.Insert(insertAt, src.Model);
        group.Pages.Move(from, insertAt);
        RenameDefaultPages(group.Package);
    }

    // Keep MoveSlideInGroup alias
    public void MoveSlideInGroup(PageGroupViewModel group, PageViewModel src, PageViewModel? tgt)
        => MovePageInGroup(group, src, tgt);

    public void RemovePage(PageViewModel pvm)
    {
        if (IsEditorOpen)
        {
            if (_editingPackage is null) return;
            int idx  = EditorPages.IndexOf(pvm);
            PageViewModel? next = idx > 0 ? EditorPages[idx - 1]
                                 : EditorPages.Count > 1 ? EditorPages[1] : null;
            _editingPackage.Pages.Remove(pvm.Model);
            EditorPages.Remove(pvm);
            RenameDefaultPages(_editingPackage);
            if (next is not null) SwitchEditingPage(next);
            else CloseEditor();
        }
        else
        {
            var package = SelectedOutput?.ActivePackage;
            if (package is null) return;
            if (SelectedOutput?.LivePage == pvm.Model) SelectedOutput.Clear();
            package.Pages.Remove(pvm.Model);
            Pages.Remove(pvm);
            // Also remove from group view if present
            foreach (var g in PageGroups) g.Pages.Remove(pvm);
            RenameDefaultPages(package);
            if (SelectedPage == pvm)
                SelectedPage = Pages.Count > 0 ? Pages[0] : null;
            UpdateIsLiveFlags();
            if (ShowingRundown) RefreshPageGroups();
        }
    }

    // Keep RemoveSlide alias
    public void RemoveSlide(PageViewModel pvm) => RemovePage(pvm);

    // ── Init ──────────────────────────────────────────────────────────────────

    public MainViewModel() { SeedDemoContent(); StartSchedulerTimer(); }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static readonly System.Text.RegularExpressions.Regex _defaultPageName =
        new(@"^(\d+|Page \d+)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    void RenameDefaultPages(Package package)
    {
        for (int i = 0; i < package.Pages.Count; i++)
        {
            if (_defaultPageName.IsMatch(package.Pages[i].Name))
                package.Pages[i].Name = $"{i + 1}";
        }
        // Notify every VM instance in every collection so all views update immediately.
        foreach (var pvm in Pages)       pvm.RefreshName();
        foreach (var pvm in EditorPages) pvm.RefreshName();
        foreach (var g in PageGroups)
            foreach (var pvm in g.Pages) pvm.RefreshName();
    }

    static void MigratePageNames(ShowFile file)
    {
        foreach (var show in file.Shows)
            foreach (var pkg in show.Packages)
                for (int i = 0; i < pkg.Pages.Count; i++)
                    if (_defaultPageName.IsMatch(pkg.Pages[i].Name))
                        pkg.Pages[i].Name = $"{i + 1}";
    }

    void RefreshPageList()
    {
        Pages.Clear();
        SelectedPage = null;
        var package = SelectedOutput?.ActivePackage;
        if (package is null) return;
        var livePage = SelectedOutput?.LivePage;
        foreach (var page in package.Pages)
        {
            var pvm = new PageViewModel(page, package);
            pvm.IsLive = page == livePage;
            Pages.Add(pvm);
        }
        if (Pages.Count > 0) SelectedPage = Pages[0];
    }

    // Keep Slides alias for AXAML bindings
    public ObservableCollection<PageViewModel> Slides => Pages;

    // Keep SelectedSlide alias for code-behind
    public PageViewModel? SelectedSlide
    {
        get => SelectedPage;
        set => SelectedPage = value;
    }

    // Keep SlideGroups alias for AXAML bindings
    public ObservableCollection<PageGroupViewModel> SlideGroups => PageGroups;

    void UpdateIsLiveFlags()
    {
        var livePage = SelectedOutput?.LivePage;
        foreach (var pvm in Pages)
            pvm.IsLive = pvm.Model == livePage;
        foreach (var group in PageGroups)
        {
            var groupLive = group.SelectedOutput?.LivePage;
            foreach (var pvm in group.Pages)
                pvm.IsLive = pvm.Model == groupLive;
        }
    }

    /// <summary>
    /// Rebuild the flat ordered tree that the rundown sidebar displays.
    /// Walks the folder hierarchy depth-first; collapsed folders hide their children.
    /// </summary>
    void BuildRundownTree()
    {
        RundownTree.Clear();
        BuildFolderLevel(null, 0);
    }

    void BuildFolderLevel(Guid? parentId, int depth)
    {
        foreach (var folder in ShowFile.RundownFolders.Where(f => f.ParentId == parentId))
        {
            RundownTree.Add(new PlaylistTreeItem(folder, depth));
            if (folder.IsExpanded)
                BuildFolderLevel(folder.Id, depth + 1);
        }
        foreach (var rd in ShowFile.Rundowns.Where(r => r.FolderId == parentId))
            RundownTree.Add(new PlaylistTreeItem(rd, depth));
    }

    void RefreshPackageItems()
    {
        PackageItems.Clear();
        if (_selectedRundown is not null)
        {
            foreach (var entry in _selectedRundown.Entries)
            {
                _packageById.TryGetValue(entry.PackageId, out var pkg);
                if (pkg is not null) PackageItems.Add(pkg);
            }
        }
        else if (_selectedShow is not null)
        {
            foreach (var p in _selectedShow.Packages) PackageItems.Add(p);
        }
        RefreshPageGroups();
    }

    void RefreshPageGroups()
    {
        PageGroups.Clear();
        if (_selectedRundown is null) return;
        foreach (var entry in _selectedRundown.Entries)
        {
            _packageById.TryGetValue(entry.PackageId, out var pkg);
            if (pkg is null) continue;
            var defaultOutput = entry.SelectedOutputId != Guid.Empty
                ? OutputStates.FirstOrDefault(o => o.Config.Id == entry.SelectedOutputId) ?? SelectedOutput
                : SelectedOutput;
            var group = new PageGroupViewModel(pkg, OutputStates, defaultOutput,
                                               defaultOutput?.LivePage,
                                               entry.DefaultTransitionType,
                                               entry.DefaultTransitionDuration);
            group.WhenAnyValue(g => g.DefaultTransitionType).Subscribe(tt =>
                entry.DefaultTransitionType = tt);
            group.WhenAnyValue(g => g.DefaultTransitionDuration).Subscribe(ms =>
                entry.DefaultTransitionDuration = ms);
            OutputState? prevOutput = defaultOutput;
            bool firstFire = true;
            group.WhenAnyValue(g => g.SelectedOutput).Subscribe(newOutput =>
            {
                entry.SelectedOutputId = newOutput?.Config.Id ?? Guid.Empty;

                if (!firstFire && newOutput is not null && newOutput != prevOutput)
                {
                    var livePage  = prevOutput?.LivePage;
                    var liveIdx   = prevOutput?.LivePageIndex ?? -1;

                    // Route the active package to the new output
                    newOutput.ActivePackage = group.Package;

                    if (livePage is not null)
                    {
                        // Move live content: clear old, go live on new
                        prevOutput!.Clear();
                        newOutput.GoLive(livePage, liveIdx, TransitionType.Cut, 0, 0.5f);
                    }

                    SelectedOutput = newOutput;
                }

                prevOutput = newOutput;
                firstFire = false;
                UpdateIsLiveFlags();
            });
            PageGroups.Add(group);
        }
    }

    // Set active package + selected page for the group's output without going live.
    public void SelectFromGroup(PageViewModel pvm)
    {
        var group = PageGroups.FirstOrDefault(g => g.Pages.Contains(pvm));
        if (group?.SelectedOutput is null) return;
        group.SelectedOutput.ActivePackage = group.Package;
        if (_selectedPage is not null) _selectedPage.IsSelected = false;
        _selectedPage = pvm;
        this.RaisePropertyChanged(nameof(SelectedPage));
        this.RaisePropertyChanged(nameof(SelectedSlide));
        if (_selectedPage is not null) _selectedPage.IsSelected = true;
    }

    // Go live with a page on that group's chosen output.
    public void GoLiveFromGroup(PageViewModel pvm)
    {
        var group = PageGroups.FirstOrDefault(g => g.Pages.Contains(pvm));
        if (group?.SelectedOutput is null) return;
        var output = group.SelectedOutput;
        output.ActivePackage = group.Package;
        if (_selectedPage is not null) _selectedPage.IsSelected = false;
        _selectedPage = pvm;
        this.RaisePropertyChanged(nameof(SelectedPage));
        this.RaisePropertyChanged(nameof(SelectedSlide));
        if (_selectedPage is not null) _selectedPage.IsSelected = true;
        int index = output.ActivePackage.Pages.IndexOf(pvm.Model);
        output.GoLive(pvm.Model, index, group.DefaultTransitionType, group.DefaultTransitionDuration,
                      pvm.Model.Transition.Easing);
        UpdateIsLiveFlags();
        StartPageTimer(pvm.Model.DurationMs, pvm.Model.LoopToStart);
        FirePageTriggerTimers(pvm.Model);
    }

    void SeedDemoContent()
    {
        // ── Outputs ───────────────────────────────────────────────────────────
        var progConfig = new OutputConfig
        {
            Name = "Program", Type = OutputType.Display,
            RoleFilter = LayerRole.Program | LayerRole.Overlay
        };
        ShowFile.AddOutput(progConfig);
        var progState = new OutputState(progConfig);
        OutputStates.Add(progState);

        var monitorConfig = new OutputConfig
        {
            Name = "Confidence Monitor", Type = OutputType.Display,
            RoleFilter = LayerRole.Stage
        };
        ShowFile.AddOutput(monitorConfig);
        var monitorState = new OutputState(monitorConfig);
        OutputStates.Add(monitorState);

        var ndiConfig = new OutputConfig
        {
            Name = "NDI Stream 1", Type = OutputType.NDI,
            RoleFilter = LayerRole.Overlay, NdiStreamName = "ShowCast-Overlay"
        };
        ShowFile.AddOutput(ndiConfig);
        var ndiState = new OutputState(ndiConfig);
        OutputStates.Add(ndiState);

        // ── Default show ──────────────────────────────────────────────────────
        var defaultShow = ShowFile.AddShow("Default");
        Shows.Add(defaultShow);

        // ── Default audio playlist ────────────────────────────────────────────
        AudioPlayer.CreatePlaylist("Default");

        // ── Default state ─────────────────────────────────────────────────────
        SelectedOutput = progState;
        SelectedShow   = defaultShow;
    }
}
