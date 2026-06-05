using System;
using System.Threading.Tasks;
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

    /// <summary>False if the DeckLink driver is not installed on this machine.</summary>
    public static bool BlackmagicAvailable { get; private set; } = false;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            desktop.Exit += (_, _) => { if (NdiAvailable) NewTek.NDIlib.destroy(); };

            splash.Opened += async (_, _) =>
            {
                try
                {
                    IProgress<(double value, string label)> progress = new Progress<(double value, string label)>(
                        update => splash.Report(update.value, update.label));

                    progress.Report((0.25, "Creating app folders"));
                    Core.AppFolders.EnsureCreated();

                    progress.Report((0.40, "Initializing NDI"));
                    NdiAvailable = await Task.Run(() => NewTek.NDIlib.TryInitialize());
                    if (!NdiAvailable)
                        System.Diagnostics.Debug.WriteLine(
                            "[App] NDI library not found — NDI outputs disabled.");

                    progress.Report((0.55, "Initializing Blackmagic"));
                    BlackmagicAvailable = await Task.Run(() => Blackmagic.DeckLinkApi.TryInitialize());
                    if (!BlackmagicAvailable)
                        System.Diagnostics.Debug.WriteLine(
                            "[App] DeckLink driver not found — Blackmagic outputs disabled.");

                    progress.Report((0.65, "Initializing AJA"));
                    await Task.Run(() => Core.AjaApi.TryInitialize());

                    progress.Report((0.80, "Preparing workspace"));
                    var vm = new MainViewModel();

                    progress.Report((1.00, "Starting up"));

                    var mainWindow = new MainWindow { DataContext = vm };
                    desktop.MainWindow = mainWindow;
                    mainWindow.Show();
                    splash.Close();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[App] Startup failed: {ex}");
                    splash.Close(); // no MainWindow opened — OnLastWindowClose shuts the process down cleanly
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
