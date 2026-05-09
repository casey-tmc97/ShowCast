using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ShowCast.ViewModels;
using ShowCast.Views;

namespace ShowCast;

public class App : Application
{
    /// <summary>False if the NDI runtime library failed to load at startup.</summary>
    public static bool NdiAvailable { get; private set; } = true;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        ShowCast.Core.AppFolders.EnsureCreated();
        NdiAvailable = NewTek.NDIlib.TryInitialize();
        if (!NdiAvailable)
            System.Diagnostics.Debug.WriteLine(
                "[App] NDI library failed to initialize — NDI outputs will not function.");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel() };
            desktop.Exit += (_, _) => { if (NdiAvailable) NewTek.NDIlib.destroy(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
