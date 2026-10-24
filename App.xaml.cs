using System.IO;
using System.Windows;
using CefSharp;
using CefSharp.Wpf.HwndHost;

namespace Lite;

public partial class App : Application
{
    public static readonly List<MainWindow> Windows = new();
    public static string SelfTest;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Store.Load();
        var s = new CefSettings
        {
            CachePath = Path.Combine(Store.Dir, "cache"),
            PersistSessionCookies = true,
            LogSeverity = LogSeverity.Disable,
        };
        s.CefCommandLineArgs["enable-features"] = "WebContentsForceDark" ;
        if (!Store.D.Settings.DarkPages) s.CefCommandLineArgs.Remove("enable-features");
        s.CefCommandLineArgs["enable-media-stream"] = "1";
        s.CefCommandLineArgs["autoplay-policy"] = "no-user-gesture-required";
        s.CefCommandLineArgs["enable-spell-checking"] = "1";
        s.CefCommandLineArgs["enable-print-preview"] = "1";
        Cef.Initialize(s, performDependencyCheck: false);
        foreach (var a in e.Args) if (a.StartsWith("--selftest")) SelfTest = a;
        var urls = e.Args.Where(x => !x.StartsWith("--")).ToList();
        var w = new MainWindow(false, urls.FirstOrDefault());
        w.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Store.SaveNow();
        Cef.Shutdown();
        base.OnExit(e);
    }
}
