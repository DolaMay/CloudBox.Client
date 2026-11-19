using BackgroundTasks;
using CloudBox.Client.Platforms.iOS;
using Foundation;
using UIKit;

namespace CloudBox.Client;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        IOSBackgroundTaskManager.Register();
        return base.FinishedLaunching(application, launchOptions);
    }
}