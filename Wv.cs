using System.Reflection;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Lite;

public static class UI { public static void Run(Action a) => Application.Current.Dispatcher.BeginInvoke(a); }

/// <summary>One page view (a WebView2 control) with the helpers the browser shell needs: deferred navigation, zoom levels, scripting and the controller.</summary>
public class Wv : WebView2
{
    static readonly FieldInfo baseField = typeof(WebView2).GetField("m_webview2Base", BindingFlags.NonPublic | BindingFlags.Instance);
    static PropertyInfo controllerProp;

    public readonly bool Private;
    /// <summary>Address to open as soon as the engine for this view is up.</summary>
    public string Pending;
    /// <summary>The address of the navigation in progress.</summary>
    public volatile string NavUri = "";
    public bool Disposed;

    public Wv(bool priv) { Private = priv; }

    public bool Ready => CoreWebView2 != null;
    public CoreWebView2 Core => CoreWebView2;

    /// <summary>The controller is not public on the WPF control; it is needed for keyboard shortcuts and focus events.</summary>
    public CoreWebView2Controller Controller
    {
        get
        {
            try
            {
                var b = baseField?.GetValue(this); if (b == null) return null;
                controllerProp ??= b.GetType().GetProperty("CoreWebView2Controller", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                return controllerProp?.GetValue(b) as CoreWebView2Controller;
            }
            catch { return null; }
        }
    }

    public async Task InitAsync(CoreWebView2Environment env)
    {
        if (Private) { var o = env.CreateCoreWebView2ControllerOptions(); o.ProfileName = "orbit-private"; o.IsInPrivateModeEnabled = true; await EnsureCoreWebView2Async(env, o); }
        else await EnsureCoreWebView2Async(env);
    }

    public void Go(string url) { if (Core != null) { NavUri = url; Core.Navigate(url); } else Pending = url; }

    /// <summary>Chromium-style zoom level (each step is a factor of 1.2, 0 is 100%).</summary>
    public double ZoomLevel
    {
        get { try { return Math.Log(ZoomFactor) / Math.Log(1.2); } catch { return 0; } }
        set { try { ZoomFactor = Math.Pow(1.2, value); } catch { } }
    }

    public void Reload(bool ignoreCache = false)
    {
        if (Core == null) return;
        if (ignoreCache) _ = Core.CallDevToolsProtocolMethodAsync("Page.reload", "{\"ignoreCache\":true}");
        else Core.Reload();
    }

    /// <summary>Runs a script in the page and returns its result as JSON (null when the view is not ready).</summary>
    public async Task<string> EvalAsync(string js)
    {
        if (Core == null) return null;
        try
        {
            var run = Core.ExecuteScriptAsync(js);
            return await Task.WhenAny(run, Task.Delay(20000)) == run ? await run : null;   // a page that never answers must not hang the caller
        }
        catch { return null; }
    }

    /// <summary>Clicks at page coordinates the way a user would (used by the self-test).</summary>
    public async Task ClickAt(int x, int y)
    {
        if (Core == null) return;
        string Ev(string type) => $"{{\"type\":\"{type}\",\"x\":{x},\"y\":{y},\"button\":\"left\",\"clickCount\":1}}";
        await Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", Ev("mouseMoved"));
        await Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", Ev("mousePressed"));
        await Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", Ev("mouseReleased"));
    }

    public new void Dispose()
    {
        if (Disposed) return; Disposed = true;
        try { Core?.Stop(); } catch { }
        try { base.Dispose(); } catch { }
    }
}
