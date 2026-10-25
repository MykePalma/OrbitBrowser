using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Lite;

/// <summary>Everything that connects a tab's WebView2 to the browser shell: navigation, downloads, dialogs, permissions and shortcuts.</summary>
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
            if (!wv.Private) await Extensions.StartAsync(wv.Core.Profile);   // (WebView2 keeps extensions out of private windows: they cannot see private tabs)   // extensions are in place before the first page loads
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
            if (e.Uri.StartsWith("http")) t.Url = e.Uri;
            if (e.IsRedirected) return;
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

        c.IsMuted = t.Muted;
    }

    static bool Down(int vk) => (GetKeyState(vk) & 0x8000) != 0;
    static string HostOf(string url) { try { return new Uri(url).Host; } catch { return ""; } }

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
        int full = 0;   // polls in a row that showed every byte received
        void Push()
        {
            long rec = op.BytesReceived, tot = (long)(op.TotalBytesToReceive ?? 0);
            // the engine now and then never reports "completed" for a download that has all its bytes: trust the bytes after a second
            bool done = op.State == CoreWebView2DownloadState.Completed || (op.State == CoreWebView2DownloadState.InProgress && full >= 3 && File.Exists(op.ResultFilePath)), cancel = op.State == CoreWebView2DownloadState.Interrupted;
            DownloadsWindow.Update(id, rec, tot, tot > 0 ? (int)(rec * 100 / tot) : 0, done, cancel, op.ResultFilePath);
        }
        op.BytesReceivedChanged += (_, _) => Push();
        op.StateChanged += (_, _) => Push();
        // belt and braces: a download that finishes very quickly can slip past the events, so also look at it while it runs
        var poll = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        poll.Tick += (_, _) => { full = op.State == CoreWebView2DownloadState.InProgress && (long)(op.TotalBytesToReceive ?? 0) > 0 && op.BytesReceived >= (long)(op.TotalBytesToReceive ?? 0) ? full + 1 : 0; Push(); if (op.State != CoreWebView2DownloadState.InProgress || full >= 3) poll.Stop(); };
        poll.Start();
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
