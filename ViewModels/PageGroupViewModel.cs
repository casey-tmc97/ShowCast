using System;
using System.Collections.ObjectModel;
using ReactiveUI;
using ShowCast.Core;

namespace ShowCast.ViewModels;

public class PageGroupViewModel : ViewModelBase
{
    public Package Package       { get; }
    public string  Name          => Package.Name;
    public ObservableCollection<OutputState>   OutputStates  { get; }
    public ObservableCollection<PageViewModel> Pages         { get; } = new();
    public IReadOnlyList<TransitionType>       TransitionTypes { get; } = Enum.GetValues<TransitionType>();

    private OutputState? _selectedOutput;
    public OutputState? SelectedOutput
    {
        get => _selectedOutput;
        set => this.RaiseAndSetIfChanged(ref _selectedOutput, value);
    }

    private TransitionType _defaultTransitionType = TransitionType.Cut;
    public TransitionType DefaultTransitionType
    {
        get => _defaultTransitionType;
        set
        {
            this.RaiseAndSetIfChanged(ref _defaultTransitionType, value);
            this.RaisePropertyChanged(nameof(TransitionLabel));
        }
    }

    private int _defaultTransitionDuration = 500;
    public int DefaultTransitionDuration
    {
        get => _defaultTransitionDuration;
        set
        {
            this.RaiseAndSetIfChanged(ref _defaultTransitionDuration, value);
            this.RaisePropertyChanged(nameof(TransitionLabel));
        }
    }

    public string TransitionLabel
    {
        get
        {
            if (DefaultTransitionType == TransitionType.Cut) return "Cut";
            double secs = DefaultTransitionDuration / 1000.0;
            string dur  = secs % 1 == 0 ? $"{(int)secs}s" : $"{secs:F1}s";
            return $"{DefaultTransitionType} · {dur}";
        }
    }

    public PageGroupViewModel(Package package, ObservableCollection<OutputState> outputStates,
                               OutputState? defaultOutput, Page? livePage,
                               TransitionType defaultTransitionType = TransitionType.Cut,
                               int defaultTransitionDuration = 500)
    {
        Package                    = package;
        OutputStates               = outputStates;
        _selectedOutput            = defaultOutput;
        _defaultTransitionType     = defaultTransitionType;
        _defaultTransitionDuration = defaultTransitionDuration;
        foreach (var page in package.Pages)
        {
            var pvm = new PageViewModel(page);
            pvm.IsLive = page == livePage;
            Pages.Add(pvm);
        }
    }
}
