using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ShowCast.Core;

namespace ShowCast.Views;

public class UpdateDownloadDialog : Window
{
    public enum DownloadDialogResult { None, InstallAndRestart, CloseApp }

    public DownloadDialogResult Result { get; private set; } = DownloadDialogResult.None;

    readonly UpdateInfo    _info;
    readonly Func<Task>    _beforeClose;
    CancellationTokenSource _cts = new();

    readonly ProgressBar _progressBar;
    readonly TextBlock   _statusText;
    readonly StackPanel  _downloadingButtons;
    readonly StackPanel  _completeButtons;
    readonly StackPanel  _errorButtons;
    readonly TextBlock   _errorText;

    public UpdateDownloadDialog(UpdateInfo info, Func<Task> beforeClose)
    {
        _info        = info;
        _beforeClose = beforeClose;

        Title                 = $"Downloading ShowCast {info.Version}";
        Width                 = 460;
        SizeToContent         = SizeToContent.Height;
        CanResize             = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _progressBar = new ProgressBar { Minimum = 0, Maximum = 1, Value = 0, Height = 8, CornerRadius = new CornerRadius(4) };
        _statusText  = new TextBlock { Text = "Starting download…", Foreground = SolidColorBrush.Parse("#aaaaaa"), FontSize = 12 };
        _errorText   = new TextBlock { Foreground = SolidColorBrush.Parse("#e07050"), FontSize = 12, TextWrapping = TextWrapping.Wrap };

        var cancelBtn  = MakeButton("Cancel",             "#3a3a3a",  80);
        var installBtn = MakeButton("Install && Restart", "#2e7d32", 140);
        var closeBtn   = MakeButton("Close App",          "#555555",  90);
        var retryBtn   = MakeButton("Try Again",          "#555555",  90);
        var errorClose = MakeButton("Close",              "#3a3a3a",  80);

        cancelBtn.Click  += (_, _) => { _cts.Cancel(); };
        installBtn.Click += async (_, _) =>
        {
            installBtn.IsEnabled = false;
            closeBtn.IsEnabled   = false;
            await _beforeClose();
            Result = DownloadDialogResult.InstallAndRestart;
            Close();
        };
        closeBtn.Click   += (_, _) => { Result = DownloadDialogResult.CloseApp; Close(); };
        retryBtn.Click   += (_, _) => { _ = StartDownloadAsync(); };
        errorClose.Click += (_, _) => Close();

        _downloadingButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Children    = { cancelBtn }
        };
        _completeButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing     = 8, Children = { closeBtn, installBtn }, IsVisible = false
        };
        _errorButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing     = 8, Children = { errorClose, retryBtn }, IsVisible = false
        };

        Content = new Border
        {
            Background = SolidColorBrush.Parse("#2d2d2d"),
            Padding    = new Thickness(20),
            Child      = new StackPanel
            {
                Spacing  = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text       = $"Downloading ShowCast {info.Version}…",
                        Foreground = Brushes.White,
                        FontSize   = 14,
                        FontWeight = FontWeight.Bold
                    },
                    _progressBar,
                    _statusText,
                    _errorText,
                    _downloadingButtons,
                    _completeButtons,
                    _errorButtons
                }
            }
        };
    }

    static Button MakeButton(string label, string bg, double width) => new()
    {
        Content                    = label,
        Width                      = width,
        Height                     = 35,
        CornerRadius               = new CornerRadius(5),
        Background                 = SolidColorBrush.Parse(bg),
        Foreground                 = Brushes.White,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment   = VerticalAlignment.Center
    };

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _ = StartDownloadAsync();
    }

    async Task StartDownloadAsync()
    {
        _cts = new CancellationTokenSource();
        SetState(State.Downloading);

        try
        {
            var progress = new Progress<double>(p =>
            {
                _progressBar.Value = p;
                _statusText.Text   = $"{p:P0} downloaded";
            });
            await UpdateChecker.DownloadAsync(_info, progress, _cts.Token);
            SetState(State.Complete);
        }
        catch (OperationCanceledException)
        {
            if (File.Exists(_info.InstallerPath))
                File.Delete(_info.InstallerPath);
            Close();
        }
        catch (Exception ex)
        {
            SetState(State.Error, ex.Message);
        }
    }

    enum State { Downloading, Complete, Error }

    void SetState(State state, string? errorMessage = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _downloadingButtons.IsVisible = state == State.Downloading;
            _completeButtons.IsVisible    = state == State.Complete;
            _errorButtons.IsVisible       = state == State.Error;
            _errorText.IsVisible          = state == State.Error;

            if (state == State.Complete)
            {
                _progressBar.Value = 1;
                _statusText.Text   = "Download complete.";
                Title              = $"ShowCast {_info.Version} Ready";
            }
            else if (state == State.Error)
            {
                _errorText.Text  = $"Download failed: {errorMessage}";
                _statusText.Text = "";
            }
        });
    }
}
