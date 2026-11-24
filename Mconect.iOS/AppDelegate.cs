using Avalonia;
using Avalonia.iOS;
using Foundation;
using MconectApp = Mconect.Portable.App.App;

namespace Mconect.iOS;

[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<MconectApp>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder);
}
