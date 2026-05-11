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

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var splash = new SplashWindow();
            desktop.MainWindow = splash;

            splash.Opened += async (_, _) =>
            {
                var progress = new Progress<(double value, string label)>(
                    p => splash.Report(p.value, p.label));
                var p = (IProgress<(double value, string label)>)progress;

                p.Report((0.25, "Creating app folders"));
                Core.AppFolders.EnsureCreated();

                p.Report((0.50, "Initializing NDI"));
                NdiAvailable = await Task.Run(() => NewTek.NDIlib.TryInitialize());
                if (!NdiAvailable)
                    System.Diagnostics.Debug.WriteLine(
                        "[App] NDI library failed to initialize — NDI outputs will not function.");

                p.Report((0.75, "Preparing workspace"));
                var vm = new MainViewModel();

                p.Report((1.00, "Starting up"));

                var mainWindow = new MainWindow { DataContext = vm };
                desktop.Exit += (_, _) => { if (NdiAvailable) NewTek.NDIlib.destroy(); };
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                splash.Close();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
