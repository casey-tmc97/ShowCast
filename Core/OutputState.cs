using ReactiveUI;

namespace ShowCast.Core;

/// <summary>
/// Runtime live state for a single output.
/// Each output independently owns which Package is loaded and which Page is live.
/// </summary>
public class OutputState : ReactiveObject
{
    public OutputConfig Config { get; }

    public OutputState(OutputConfig config) => Config = config;

    private Package? _activePackage;
    public Package? ActivePackage
    {
        get => _activePackage;
        set
        {
            this.RaiseAndSetIfChanged(ref _activePackage, value);
            Config.ActivePackageId = value?.Id ?? Guid.Empty;
        }
    }

    private Page? _livePage;
    public Page? LivePage
    {
        get => _livePage;
        set => this.RaiseAndSetIfChanged(ref _livePage, value);
    }

    private int _livePageIndex = -1;
    public int LivePageIndex
    {
        get => _livePageIndex;
        set => this.RaiseAndSetIfChanged(ref _livePageIndex, value);
    }

    public string     Name          => Config.Name;
    public LayerRole  Roles         => Config.RoleFilter;
    public OutputType Type          => Config.Type;
    public bool       IsDisplayType => Config.Type == OutputType.Display;

    // Set before LivePage so the viewport can read them in the LivePage change callback
    public TransitionType PendingTransitionType     { get; set; } = TransitionType.Cut;
    public int            PendingTransitionDuration { get; set; } = 500;
    public float          PendingTransitionEasing   { get; set; } = 0.5f;

    /// When true, the next OnLivePageChanged skips entry animations (loop-to-start behavior).
    public bool PendingSkipEntryAnimations { get; set; }

    private bool _isOutputWindowOpen;
    public bool IsOutputWindowOpen
    {
        get => _isOutputWindowOpen;
        set => this.RaiseAndSetIfChanged(ref _isOutputWindowOpen, value);
    }

    public void GoLive(Page page, int index,
                        TransitionType transType   = TransitionType.Cut,
                        int  durationMs            = 500,
                        float easing               = 0.5f,
                        bool skipAnimations        = false)
    {
        PendingTransitionType      = transType;
        PendingTransitionDuration  = durationMs;
        PendingTransitionEasing    = easing;
        // Set flag before LivePage fires — UI subscribers (synchronous) and the NDI
        // background thread both read it here. Next GoLive() always overwrites it fresh.
        PendingSkipEntryAnimations = skipAnimations;
        LivePage      = page;
        LivePageIndex = index;
    }

    public void Clear()
    {
        LivePage      = null;
        LivePageIndex = -1;
    }
}
