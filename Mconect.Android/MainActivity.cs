using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using MconectApp = Mconect.Portable.App.App;

namespace Mconect.Android;

[Activity(
    Label = "Mconect",
    Theme = "@style/MconectTheme.NoActionBar",
    Icon = "@drawable/Icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity<MconectApp>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder);
}
