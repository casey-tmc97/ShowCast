using System;
using System.Collections.Generic;
using ReactiveUI;
using ShowCast.Core;

namespace ShowCast.ViewModels;

/// <summary>Editable mirror of an OutputConfig — used exclusively by ScreenConfigDialog.</summary>
public class OutputEditViewModel : ViewModelBase
{
    // ── Static option lists (used as ItemsSource in the dialog XAML) ─────────

    public static readonly string[] TypeLabels       = { "Display", "NDI", "Preview" };
    public static readonly string[] FrameRateLabels  = { "23.976", "24", "25", "29.97", "30", "50", "59.94", "60" };
    public static readonly double[] FrameRateValues  = { 23.976,   24.0, 25.0, 29.97,  30.0, 50.0, 59.94,  60.0 };
    static readonly OutputType[]    TypeValues       = { OutputType.Display, OutputType.NDI, OutputType.Preview };

    // ── Name ─────────────────────────────────────────────────────────────────

    private string _name = "";
    public string Name
    {
        get => _name;
        set
        {
            var old = _name;
            this.RaiseAndSetIfChanged(ref _name, value);
            // Keep auto-generated NDI stream name in sync with the output name
            if (IsNDI && NdiStreamName == AutoNdiName(old))
                NdiStreamName = AutoNdiName(value);
        }
    }

    static string AutoNdiName(string outputName) =>
        $"ShowCast-{(string.IsNullOrWhiteSpace(outputName) ? "Output" : outputName.Trim())}";

    // ── Type ─────────────────────────────────────────────────────────────────

    private int _typeIndex;
    public int TypeIndex
    {
        get => _typeIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref _typeIndex, value);
            this.RaisePropertyChanged(nameof(IsDisplay));
            this.RaisePropertyChanged(nameof(IsNDI));
            // Auto-populate stream name when switching to NDI
            if (IsNDI && string.IsNullOrWhiteSpace(NdiStreamName))
                NdiStreamName = AutoNdiName(Name);
        }
    }

    public bool IsDisplay => TypeIndex == 0;
    public bool IsNDI     => TypeIndex == 1;

    // ── Monitor ───────────────────────────────────────────────────────────────

    private List<string> _availableMonitors = new();
    public List<string> AvailableMonitors
    {
        get => _availableMonitors;
        set => this.RaiseAndSetIfChanged(ref _availableMonitors, value);
    }

    private int _displayIndex;
    public int DisplayIndex { get => _displayIndex; set => this.RaiseAndSetIfChanged(ref _displayIndex, value); }

    // ── NDI ───────────────────────────────────────────────────────────────────

    private string _ndiStreamName = "";
    public string NdiStreamName { get => _ndiStreamName; set => this.RaiseAndSetIfChanged(ref _ndiStreamName, value); }

    // ── Resolution ────────────────────────────────────────────────────────────

    private decimal _width  = 1920;
    private decimal _height = 1080;
    public decimal Width  { get => _width;  set => this.RaiseAndSetIfChanged(ref _width,  value); }
    public decimal Height { get => _height; set => this.RaiseAndSetIfChanged(ref _height, value); }

    // ── Frame rate ────────────────────────────────────────────────────────────

    private int _frameRateIndex = 6; // 59.94
    public int FrameRateIndex { get => _frameRateIndex; set => this.RaiseAndSetIfChanged(ref _frameRateIndex, value); }

    // ── Layer role flags ──────────────────────────────────────────────────────

    private bool _program = true, _stage, _overlay, _ndiKey, _ndiFill, _confidence, _preview;
    public bool FlagProgram    { get => _program;    set => this.RaiseAndSetIfChanged(ref _program,    value); }
    public bool FlagStage      { get => _stage;      set => this.RaiseAndSetIfChanged(ref _stage,      value); }
    public bool FlagOverlay    { get => _overlay;    set => this.RaiseAndSetIfChanged(ref _overlay,    value); }
    public bool FlagNDIKey     { get => _ndiKey;     set => this.RaiseAndSetIfChanged(ref _ndiKey,     value); }
    public bool FlagNDIFill    { get => _ndiFill;    set => this.RaiseAndSetIfChanged(ref _ndiFill,    value); }
    public bool FlagConfidence { get => _confidence; set => this.RaiseAndSetIfChanged(ref _confidence, value); }
    public bool FlagPreview    { get => _preview;    set => this.RaiseAndSetIfChanged(ref _preview,    value); }

    // ── Options ───────────────────────────────────────────────────────────────

    private bool _fullscreen;
    private bool _enabled = true;
    public bool Fullscreen { get => _fullscreen; set => this.RaiseAndSetIfChanged(ref _fullscreen, value); }
    public bool Enabled    { get => _enabled;    set => this.RaiseAndSetIfChanged(ref _enabled,    value); }

    // ── Load / Write ──────────────────────────────────────────────────────────

    public void LoadFrom(OutputConfig cfg, int monitorCount)
    {
        Name          = cfg.Name;
        TypeIndex     = Math.Max(0, Array.IndexOf(TypeValues, cfg.Type));
        DisplayIndex  = Math.Clamp(cfg.DisplayIndex, 0, Math.Max(0, monitorCount - 1));
        NdiStreamName = cfg.NdiStreamName;
        Width         = cfg.Width;
        Height        = cfg.Height;

        int fpsIdx = Array.FindIndex(FrameRateValues, f => Math.Abs(f - cfg.FrameRate) < 0.01);
        FrameRateIndex = fpsIdx >= 0 ? fpsIdx : 6;

        FlagProgram    = (cfg.RoleFilter & LayerRole.Program)    != 0;
        FlagStage      = (cfg.RoleFilter & LayerRole.Stage)      != 0;
        FlagOverlay    = (cfg.RoleFilter & LayerRole.Overlay)    != 0;
        FlagNDIKey     = (cfg.RoleFilter & LayerRole.NDIKey)     != 0;
        FlagNDIFill    = (cfg.RoleFilter & LayerRole.NDIFill)    != 0;
        FlagConfidence = (cfg.RoleFilter & LayerRole.Confidence) != 0;
        FlagPreview    = (cfg.RoleFilter & LayerRole.Preview)    != 0;

        Fullscreen = cfg.Fullscreen;
        Enabled    = cfg.Enabled;
    }

    public void WriteTo(OutputConfig cfg)
    {
        cfg.Name          = Name;
        cfg.Type          = TypeIndex >= 0 && TypeIndex < TypeValues.Length
                            ? TypeValues[TypeIndex] : OutputType.Display;
        cfg.DisplayIndex  = DisplayIndex;
        cfg.NdiStreamName = NdiStreamName;
        cfg.Width         = (int)Width;
        cfg.Height        = (int)Height;
        cfg.FrameRate     = FrameRateIndex >= 0 && FrameRateIndex < FrameRateValues.Length
                            ? FrameRateValues[FrameRateIndex] : 59.94;
        cfg.RoleFilter    = BuildRoles();
        cfg.Fullscreen    = Fullscreen;
        cfg.Enabled       = Enabled;
    }

    LayerRole BuildRoles()
    {
        var r = LayerRole.None;
        if (FlagProgram)    r |= LayerRole.Program;
        if (FlagStage)      r |= LayerRole.Stage;
        if (FlagOverlay)    r |= LayerRole.Overlay;
        if (FlagNDIKey)     r |= LayerRole.NDIKey;
        if (FlagNDIFill)    r |= LayerRole.NDIFill;
        if (FlagConfidence) r |= LayerRole.Confidence;
        if (FlagPreview)    r |= LayerRole.Preview;
        return r;
    }
}
