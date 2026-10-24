using KyzWheel.App.Wheel;
using Microsoft.UI.Xaml;

namespace KyzWheel.App;

public partial class App : Application
{
    private WheelWindow? _wheel;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _wheel = new WheelWindow();
        _wheel.ShowAtCursor();
    }
}
