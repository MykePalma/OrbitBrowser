using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Lite;

public partial class App : Application
{
    /// <summary>Open browser windows (named to avoid hiding Application.Windows).</summary>
    public static readonly List<MainWindow> Browsers = new();
    public static string SelfTest;
    /// <summary>The shared WebView2 environment (one browser process group for every window).</summary>
    public static Task<CoreWebView2Environment> EnvTask;
    /// <summary>Version of the Chromium engine in use, e.g. 155.0.3912.0.</summary>
    public static string EngineVersion = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Store.Load();
        // a verified update downloaded earlier is installed now, before the browser engine starts
        if (!Store.IsTestRun && Store.D.Settings.AutoUpdate && Updater.Staged() is StagedUpdate staged && Updater.CanWriteInstall())
        {
            Updater.LaunchInstaller(staged); Shutdown(); return;
        }
        if (!Store.IsTestRun) RemoveOldEngineFiles();
        string runtime = null;
        try { runtime = CoreWebView2Environment.GetAvailableBrowserVersionString(); } catch { }
        if (string.IsNullOrEmpty(runtime))
        {
            var r = MessageBox.Show("Orbit needs the Microsoft Edge WebView2 Runtime, which this computer doesn't have yet.\n\nOpen the download page now?", "Orbit", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (r == MessageBoxResult.Yes) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true }); } catch { } }
            Shutdown(); return;
        }
        EngineVersion = runtime;
        EnvTask = CreateEnvironment();
        foreach (var a in e.Args) if (a.StartsWith("--selftest")) SelfTest = a;
        var urls = e.Args.Where(x => !x.StartsWith("--")).ToList();
        var w = new MainWindow(false, urls.FirstOrDefault());
        w.Show();
    }

    /// <summary>Orbit 1.0.x shipped its own copy of Chromium (about 250 MB). An update installs over the old folder without deleting it, so the leftovers are cleared out here.</summary>
    static void RemoveOldEngineFiles()
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            foreach (var f in new[] { "libcef.dll", "chrome_elf.dll", "icudtl.dat", "snapshot_blob.bin", "v8_context_snapshot.bin", "resources.pak", "chrome_100_percent.pak", "chrome_200_percent.pak", "d3dcompiler_47.dll", "libEGL.dll", "libGLESv2.dll", "vk_swiftshader.dll", "vk_swiftshader_icd.json", "vulkan-1.dll", "dxcompiler.dll", "dxil.dll", "CefSharp.dll", "CefSharp.Core.dll", "CefSharp.Core.Runtime.dll", "CefSharp.Wpf.HwndHost.dll", "CefSharp.BrowserSubprocess.exe", "CefSharp.BrowserSubprocess.Core.dll", "CefSharp.BrowserSubprocess.dll", "CefSharp.Wpf.dll", "CefSharp.Core.xml", "CefSharp.xml", "CefSharp.Wpf.HwndHost.xml", "bootstrapper.exe" })
                try { var p = Path.Combine(dir, f); if (File.Exists(p)) File.Delete(p); } catch { }
            foreach (var d in new[] { "locales", "swiftshader" })
                try { var p = Path.Combine(dir, d); if (Directory.Exists(p)) Directory.Delete(p, true); } catch { }
        }
        catch { }
    }

    static Task<CoreWebView2Environment> CreateEnvironment()
    {
        var args = new List<string> { "--autoplay-policy=no-user-gesture-required" };
        if (Store.D.Settings.DarkPages) args.Add("--enable-features=WebContentsForceDark");
        var opts = new CoreWebView2EnvironmentOptions
        {
            AreBrowserExtensionsEnabled = true,
            AdditionalBrowserArguments = string.Join(" ", args),
        };
        return CoreWebView2Environment.CreateAsync(null, Path.Combine(Store.Dir, "webview2"), opts);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Store.SaveNow();
        base.OnExit(e);
    }
}
