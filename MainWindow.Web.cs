using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Lite;

/// <summary>Everything that connects a tab's WebView2 to the browser shell: navigation, downloads, dialogs, permissions, shortcuts, the ad blocker.</summary>
public partial class MainWindow
{
    [DllImport("user32.dll")] static extern short GetKeyState(int vk);
    static int dlCounter;

    /// <summary>Starts the engine for a tab's view; once it is up the pending address is opened.</summary>
    async Task InitTab(Tab t, Wv wv)
    {
        try
        {
            var env = await App.EnvTask;
            if (wv.Disposed) return;
            await wv.InitAsync(env);
            if (wv.Disposed || wv.Core == null) return;
            Wire(t, wv);
            if (!wv.Private) await Extensions.StartAsync(wv.Core.Profile);   // extensions are in place before the first page loads
            if (wv.Disposed) return;
            if (wv.Pending != null) { var u = wv.Pending; wv.Pending = null; wv.Go(u); }
        }
        catch (Exception ex) { Debug.WriteLine("WebView2 init failed: " + ex); }
    }

    void Wire(Tab t, Wv wv)
    {
        var c = wv.Core; var ctl = wv.Controller;
        var s = c.Settings;
        s.AreDefaultScriptDialogsEnabled = false;     // Orbit draws its own alert / confirm / prompt boxes
        s.IsStatusBarEnabled = false;                 // the status text is shown by the shell
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsSwipeNavigationEnabled = false;

        c.NavigationStarting += (_, e) =>
        {
            wv.NavUri = e.Uri;
            if (e.Uri.StartsWith("http")) t.Url = e.Uri;      // the ad blocker needs the new page's host before its first request goes out
            if (e.IsRedirected) return;
            lock (t.BlockedUrls) t.BlockedUrls.Clear(); t.BlockedCount = 0; UpdateShieldFor(t);   // a new page: start counting again
            t.Loading = true; OnProgress(t, 0.3);
            if (t == Cur) UpdateToolbar();
        };
        c.NavigationCompleted += (_, e) =>
        {
            t.Loading = false; t.CanBack = c.CanGoBack; t.CanFwd = c.CanGoForward;
            AddHistory(t); if (t == Cur) { Prog.Visibility = Visibility.Hidden; UpdateToolbar(); }
        };
        c.SourceChanged += (_, _) => { t.Url = c.Source; if (t == Cur) UpdateToolbar(); };
        c.HistoryChanged += (_, _) => { t.CanBack = c.CanGoBack; t.CanFwd = c.CanGoForward; if (t == Cur) UpdateToolbar(); };
        c.DocumentTitleChanged += (_, _) => OnTitle(t, c.DocumentTitle);
        c.StatusBarTextChanged += (_, _) => OnStatus(t, c.StatusBarText);
        c.ContainsFullScreenElementChanged += (_, _) => SetHtmlFullscreen(c.ContainsFullScreenElement);
        c.FaviconChanged += async (_, _) =>
        {
            try
            {
                if (string.IsNullOrEmpty(c.FaviconUri)) { t.Favicon = null; return; }
                using var st = await c.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png); using var ms = new MemoryStream(); await st.CopyToAsync(ms);
                t.SetFavicon(ms.ToArray(), b64 => OnFavicon(t, b64));
            }
            catch { }
        };
        c.NewWindowRequested += (_, e) => { e.Handled = true; NewTab(e.Uri, true, t.Private, afterTab: t); };
        c.DownloadStarting += (_, e) => OnDownload(e);
        c.PermissionRequested += (_, e) =>
        {
            var d = e.GetDeferral();
            var r = MessageBox.Show(this, $"{HostOf(e.Uri)} wants to use: {e.PermissionKind}\n\nAllow?", "Site permission", MessageBoxButton.YesNo, MessageBoxImage.Question);
            e.State = r == MessageBoxResult.Yes ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny; d.Complete();
        };
        c.ServerCertificateErrorDetected += (_, e) =>
        {
            var d = e.GetDeferral();
            var r = MessageBox.Show(this, "The security certificate for this site is not trusted (" + e.ErrorStatus + ").\n\n" + e.RequestUri + "\n\nProceed anyway? This is unsafe.", "Certificate problem", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            e.Action = r == MessageBoxResult.Yes ? CoreWebView2ServerCertificateErrorAction.AlwaysAllow : CoreWebView2ServerCertificateErrorAction.Cancel; d.Complete();
        };
        c.BasicAuthenticationRequested += (_, e) =>
        {
            var d = e.GetDeferral();
            var a = new AuthWindow(HostOf(e.Uri) + (e.Challenge != "" ? " – " + e.Challenge : ""));
            if (a.ShowDialog() == true) { e.Response.UserName = a.User; e.Response.Password = a.Pass; } else e.Cancel = true;
            d.Complete();
        };
        c.ScriptDialogOpening += (_, e) =>
        {
            var d = e.GetDeferral();
            var title = HostOf(e.Uri) is { Length: > 0 } h ? h + " says" : "Page says";
            switch (e.Kind)
            {
                case CoreWebView2ScriptDialogKind.Alert: MessageBox.Show(this, e.Message, title, MessageBoxButton.OK); break;
                case CoreWebView2ScriptDialogKind.Confirm: if (MessageBox.Show(this, e.Message, title, MessageBoxButton.OKCancel) == MessageBoxResult.OK) e.Accept(); break;
                case CoreWebView2ScriptDialogKind.Beforeunload: if (MessageBox.Show(this, "This page asks you to confirm that you want to leave.", "Leave site?", MessageBoxButton.OKCancel) == MessageBoxResult.OK) e.Accept(); break;
                default: var p = new PromptWindow(title, e.Message, e.DefaultText) { Owner = this }; if (p.ShowDialog() == true) { e.ResultText = p.Value; e.Accept(); } break;
            }
            d.Complete();
        };
        c.ContextMenuRequested += (_, e) => OnContextMenu(t, wv, e);

        // keyboard shortcuts: the page owns the keyboard, so shortcuts are picked off before it sees them
        if (ctl != null)
        {
            ctl.AcceleratorKeyPressed += (_, e) =>
            {
                if (e.KeyEventKind != CoreWebView2KeyEventKind.KeyDown && e.KeyEventKind != CoreWebView2KeyEventKind.SystemKeyDown) return;
                int vk = (int)e.VirtualKey; bool ctrl = Down(0x11), shift = Down(0x10), alt = Down(0x12);
                if (!IsShortcut(vk, ctrl, shift, alt)) return;
                e.Handled = true; UI.Run(() => HandleShortcut(vk, ctrl, shift, alt));
            };
            ctl.GotFocus += (_, _) => OnPaneFocus(t);
            ctl.ZoomFactorChanged += (_, _) => UpdateZoomLabel();
        }

        // ad blocker: requests that match the filter lists are answered with an empty 403 instead of being sent
        c.AddWebResourceRequestedFilter("http://*", CoreWebView2WebResourceContext.All);
        c.AddWebResourceRequestedFilter("https://*", CoreWebView2WebResourceContext.All);
        c.WebResourceRequested += (_, e) => FilterRequest(t, wv, e);
        // element hiding: the page asks for the style sheet for its host, the shell answers (see CosmeticScript)
        _ = c.AddScriptToExecuteOnDocumentCreatedAsync(CosmeticScript);
        c.WebMessageReceived += (_, e) => OnCosmeticRequest(t, c, e);

        c.IsMuted = t.Muted;
    }

    static bool Down(int vk) => (GetKeyState(vk) & 0x8000) != 0;
    static string HostOf(string url) { try { return new Uri(url).Host; } catch { return ""; } }
    void UpdateShieldFor(Tab t) { UI.Run(() => OnBlocked(t)); }

    // ---------------------------------------------------------------- ad blocker
    static ReqType Map(CoreWebView2WebResourceContext c) => c switch
    {
        CoreWebView2WebResourceContext.Document => ReqType.SubDocument,
        CoreWebView2WebResourceContext.Stylesheet => ReqType.Stylesheet,
        CoreWebView2WebResourceContext.Script => ReqType.Script,
        CoreWebView2WebResourceContext.Image => ReqType.Image,
        CoreWebView2WebResourceContext.Font => ReqType.Font,
        CoreWebView2WebResourceContext.Media => ReqType.Media,
        CoreWebView2WebResourceContext.XmlHttpRequest or CoreWebView2WebResourceContext.Fetch => ReqType.Xhr,
        CoreWebView2WebResourceContext.Ping => ReqType.Ping,
        CoreWebView2WebResourceContext.Websocket => ReqType.WebSocket,
        _ => ReqType.Other
    };

    void FilterRequest(Tab t, Wv wv, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            if (!Store.D.Settings.AdBlock) return;
            var url = e.Request.Uri;
            if (e.ResourceContext == CoreWebView2WebResourceContext.Document && (url == wv.NavUri || url == t.Url)) return;   // never block the page being opened
            var top = t.Host; if (AdBlocker.IsPaused(top)) return;
            var type = Map(e.ResourceContext);
            bool hit = AdBlocker.Engine.ShouldBlock(url, top, type);
            AdBlocker.Trace?.Enqueue((hit ? "BLOCK " : "allow ") + e.ResourceContext + " " + top + " <- " + url);
            if (!hit) return;
            e.Response = wv.Core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "Content-Type: text/plain");
            bool added; lock (t.BlockedUrls) { added = t.BlockedUrls.Add(url); t.BlockedCount = t.BlockedUrls.Count; }
            if (added && Interlocked.Exchange(ref t.BlockedDirty, 1) == 0) UI.Run(() => { t.BlockedDirty = 0; OnBlocked(t); });   // at most one UI update in flight
        }
        catch { }
    }

    /// <summary>Runs at the start of every page: asks the shell for the element-hiding CSS of this host and adds it as soon as the document exists.</summary>
    const string CosmeticScript = @"(function(){
if(window.top!==window||!/^https?:$/.test(location.protocol)||!window.chrome||!window.chrome.webview)return;
var host=location.hostname,done=false;
function add(css){var r=document.documentElement;if(!r)return false;var s=document.createElement('style');s.setAttribute('data-orbit-adblock','1');s.textContent=css;r.appendChild(s);return true}
function put(css){if(done||!css)return;done=true;if(!add(css)){var o=new MutationObserver(function(){if(add(css))o.disconnect()});o.observe(document,{childList:true})}}
window.chrome.webview.addEventListener('message',function(e){var d=e.data;if(d&&d.o==='orbit-css'&&d.h===host)put(d.css)});
window.chrome.webview.postMessage('orbit-css:'+host);
})();";

    void OnCosmeticRequest(Tab t, CoreWebView2 c, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var m = e.TryGetWebMessageAsString(); if (m == null || !m.StartsWith("orbit-css:")) return;
            if (!Store.D.Settings.AdBlock) return;
            var host = m[10..]; if (host != HostOf(e.Source)) return;
            if (AdBlocker.IsPaused(t.Host) || AdBlocker.IsPaused(host)) return;
            var css = AdBlocker.Engine.CosmeticCss(host); if (css.Length == 0) return;
            c.PostWebMessageAsJson(JsonSerializer.Serialize(new { o = "orbit-css", h = host, css }));
        }
        catch { }
    }

    // ---------------------------------------------------------------- downloads
    void OnDownload(CoreWebView2DownloadStartingEventArgs e)
    {
        var op = e.DownloadOperation;
        var dir = Store.D.Settings.DownloadDir;
        try { Directory.CreateDirectory(dir); } catch { }
        var name = Path.GetFileName(e.ResultFilePath); if (string.IsNullOrEmpty(name)) name = "download";
        var path = Path.Combine(dir, name);
        for (int i = 1; File.Exists(path); i++) path = Path.Combine(dir, Path.GetFileNameWithoutExtension(name) + $" ({i})" + Path.GetExtension(name));
        e.ResultFilePath = path;
        e.Handled = !Store.D.Settings.AskDownload;
        int id = ++dlCounter;
        DownloadsWindow.Add(id, op.Uri, path, () => op.Pause(), () => op.Resume(), () => op.Cancel());
        void Push()
        {
            long rec = op.BytesReceived, tot = (long)(op.TotalBytesToReceive ?? 0);
            bool done = op.State == CoreWebView2DownloadState.Completed, cancel = op.State == CoreWebView2DownloadState.Interrupted;
            DownloadsWindow.Update(id, rec, tot, tot > 0 ? (int)(rec * 100 / tot) : 0, done, cancel, op.ResultFilePath);
        }
        op.BytesReceivedChanged += (_, _) => Push();
        op.StateChanged += (_, _) => Push();
        ShowDownloads();
    }

    // ---------------------------------------------------------------- context menu
    void OnContextMenu(Tab t, Wv wv, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var env = wv.Core.Environment; var tg = e.ContextMenuTarget; var items = e.MenuItems;
        CoreWebView2ContextMenuItem Mk(string label, Action a)
        {
            var it = env.CreateContextMenuItem(label, null, CoreWebView2ContextMenuItemKind.Command);
            it.CustomItemSelected += (_, _) => UI.Run(a);
            return it;
        }
        int at = 0;
        if (!string.IsNullOrEmpty(tg.LinkUri))
        {
            var link = tg.LinkUri;
            for (int i = 0; i < items.Count; i++) if (items[i].Name == "openLinkInNewTab" || items[i].Name == "openLinkInNewWindow") at = i + 1;
            items.Insert(at, Mk("Open link in private window", () => OpenPrivateWindow(link)));
        }
        else if (tg.Kind == CoreWebView2ContextMenuTargetKind.Page && !tg.IsEditable && !tg.HasSelection)
        {
            items.Add(Mk("Save page as PDF…", SavePdf));
        }
    }
}
