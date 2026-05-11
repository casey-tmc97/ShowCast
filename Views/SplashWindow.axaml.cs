using Avalonia.Controls;

namespace ShowCast.Views;

public partial class SplashWindow : Window
{
    public SplashWindow() => InitializeComponent();

    public void Report(double value, string label)
    {
        SplashProgressBar.Value = value;
        SplashStatusLabel.Text  = label;
    }
}
