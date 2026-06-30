using Android.App;
using Android.Runtime;

namespace FanController;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Android.Util.Log.Error("FanController", $"UNHANDLED: {ex}");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Android.Util.Log.Error("FanController", $"UNOBSERVED TASK: {e.Exception}");
            e.SetObserved();
        };
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
