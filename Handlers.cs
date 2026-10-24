using System.Windows;
using CefSharp;
using CefSharp.Handler;

namespace Lite;

static class UI { public static void Run(Action a) => Application.Current.Dispatcher.BeginInvoke(a); }

class TabDisplay : DisplayHandler
{
    readonly Tab t; readonly MainWindow w;
    public TabDisplay(Tab t, MainWindow w) { this.t = t; this.w = w; }
    protected override void OnFaviconUrlChange(IWebBrowser b, IBrowser br, IList<string> urls)
    {
        if (br.IsPopup) return;
        var u = urls?.FirstOrDefault(x => !x.Contains(".svg")) ?? urls?.FirstOrDefault();
        UI.Run(() => { if (u == null) t.Favicon = null; else t.LoadFavicon(u, b64 => w.OnFavicon(t, b64)); });
    }
    protected override void OnAddressChanged(IWebBrowser b, AddressChangedEventArgs e) { if (e.Browser.IsPopup) return; var u = e.Address; UI.Run(() => { t.Url = u; if (t == w.Cur) w.UpdateToolbar(); }); }
    protected override void OnTitleChanged(IWebBrowser b, TitleChangedEventArgs e) { if (e.Browser.IsPopup) return; var s = e.Title; UI.Run(() => w.OnTitle(t, s)); }
    protected override void OnStatusMessage(IWebBrowser b, StatusMessageEventArgs e) { if (e.Browser.IsPopup) return; var s = e.Value; UI.Run(() => w.OnStatus(t, s)); }
    protected override void OnFullscreenModeChange(IWebBrowser b, IBrowser br, bool fs) => UI.Run(() => w.SetHtmlFullscreen(fs));
    protected override void OnLoadingProgressChange(IWebBrowser b, IBrowser br, double p) => UI.Run(() => w.OnProgress(t, p));
}

class TabLifeSpan : LifeSpanHandler
{
    readonly Tab t; readonly MainWindow w;
    public TabLifeSpan(Tab t, MainWindow w) { this.t = t; this.w = w; }
    protected override bool OnBeforePopup(IWebBrowser b, IBrowser br, IFrame f, string url, string frame, WindowOpenDisposition d, bool gesture, IPopupFeatures pf, IWindowInfo wi, IBrowserSettings bs, ref bool nojs, out IWebBrowser nb)
    {
        nb = null;
        bool bg = d == WindowOpenDisposition.NewBackgroundTab;
        UI.Run(() => w.NewTab(url, !bg, t.Private, afterTab: t));
        return true;
    }
}

class TabDownload : DownloadHandler
{
    readonly MainWindow w;
    public TabDownload(MainWindow w) { this.w = w; }
    protected override bool CanDownload(IWebBrowser b, IBrowser br, string url, string method) => true;
    protected override bool OnBeforeDownload(IWebBrowser b, IBrowser br, DownloadItem item, IBeforeDownloadCallback cb)
    {
        var dir = Store.D.Settings.DownloadDir;
        try { System.IO.Directory.CreateDirectory(dir); } catch { }
        var name = string.IsNullOrEmpty(item.SuggestedFileName) ? "download" : item.SuggestedFileName;
        var path = System.IO.Path.Combine(dir, name);
        for (int i = 1; System.IO.File.Exists(path); i++)
            path = System.IO.Path.Combine(dir, System.IO.Path.GetFileNameWithoutExtension(name) + $" ({i})" + System.IO.Path.GetExtension(name));
        UI.Run(() => DownloadsWindow.Add(item.Id, item.Url, path));
        using (cb) cb.Continue(path, Store.D.Settings.AskDownload);
        UI.Run(() => w.ShowDownloads());
        return true;
    }
    protected override void OnDownloadUpdated(IWebBrowser b, IBrowser br, DownloadItem item, IDownloadItemCallback cb)
    {
        var id = item.Id; var rec = item.ReceivedBytes; var tot = item.TotalBytes; var pct = item.PercentComplete;
        var done = item.IsComplete; var cancel = item.IsCancelled; var path = item.FullPath;
        UI.Run(() => DownloadsWindow.Update(id, rec, tot, pct, done, cancel, path, cb));
    }
}

class TabKeyboard : KeyboardHandler
{
    readonly MainWindow w;
    public TabKeyboard(MainWindow w) { this.w = w; }
    protected override bool OnPreKeyEvent(IWebBrowser b, IBrowser br, KeyType type, int vk, int native, CefEventFlags mods, bool isSystem, ref bool isShortcut)
    {
        if (type != KeyType.RawKeyDown) return false;
        bool ctrl = mods.HasFlag(CefEventFlags.ControlDown), shift = mods.HasFlag(CefEventFlags.ShiftDown), alt = mods.HasFlag(CefEventFlags.AltDown);
        if (!MainWindow.IsShortcut(vk, ctrl, shift, alt)) return false;
        UI.Run(() => w.HandleShortcut(vk, ctrl, shift, alt));
        return true;
    }
}

class TabContextMenu : ContextMenuHandler
{
    readonly Tab t; readonly MainWindow w;
    const int OpenLink = (int)CefMenuCommand.UserFirst + 1, CopyLink = OpenLink + 1, OpenPriv = OpenLink + 2, OpenImg = OpenLink + 3, SaveImg = OpenLink + 4, CopyImg = OpenLink + 5,
        Search = OpenLink + 6, Source = OpenLink + 7, Inspect = OpenLink + 8, SavePdf = OpenLink + 9, Print = OpenLink + 10, SaveLink = OpenLink + 11, Spell0 = OpenLink + 20;
    public TabContextMenu(Tab t, MainWindow w) { this.t = t; this.w = w; }

    protected override void OnBeforeContextMenu(IWebBrowser b, IBrowser br, IFrame f, IContextMenuParams p, IMenuModel m)
    {
        m.Clear();
        bool edit = p.MediaType == ContextMenuMediaType.None && p.TypeFlags.HasFlag(ContextMenuType.Editable);
        if (!string.IsNullOrEmpty(p.MisspelledWord) && p.DictionarySuggestions != null)
        {
            int i = 0; foreach (var s in p.DictionarySuggestions.Take(4)) m.AddItem((CefMenuCommand)(Spell0 + i++), s);
            if (i > 0) m.AddSeparator();
        }
        if (!string.IsNullOrEmpty(p.LinkUrl))
        {
            m.AddItem((CefMenuCommand)OpenLink, "Open link in new tab");
            m.AddItem((CefMenuCommand)OpenPriv, "Open link in private window");
            m.AddItem((CefMenuCommand)SaveLink, "Save link as…");
            m.AddItem((CefMenuCommand)CopyLink, "Copy link address");
            m.AddSeparator();
        }
        if (p.MediaType == ContextMenuMediaType.Image && !string.IsNullOrEmpty(p.SourceUrl))
        {
            m.AddItem((CefMenuCommand)OpenImg, "Open image in new tab");
            m.AddItem((CefMenuCommand)SaveImg, "Save image as…");
            m.AddItem((CefMenuCommand)CopyImg, "Copy image address");
            m.AddSeparator();
        }
        if (edit)
        {
            m.AddItem(CefMenuCommand.Undo, "Undo"); m.AddItem(CefMenuCommand.Redo, "Redo"); m.AddSeparator();
            m.AddItem(CefMenuCommand.Cut, "Cut"); m.AddItem(CefMenuCommand.Copy, "Copy"); m.AddItem(CefMenuCommand.Paste, "Paste"); m.AddItem(CefMenuCommand.SelectAll, "Select all");
            m.AddSeparator();
        }
        else if (!string.IsNullOrEmpty(p.SelectionText))
        {
            m.AddItem(CefMenuCommand.Copy, "Copy");
            var s = p.SelectionText.Length > 24 ? p.SelectionText[..24] + "…" : p.SelectionText;
            m.AddItem((CefMenuCommand)Search, $"Search for “{s}”");
            m.AddSeparator();
        }
        if (string.IsNullOrEmpty(p.LinkUrl) && !edit)
        {
            m.AddItem(CefMenuCommand.Back, "Back"); m.SetEnabled(CefMenuCommand.Back, br.CanGoBack);
            m.AddItem(CefMenuCommand.Forward, "Forward"); m.SetEnabled(CefMenuCommand.Forward, br.CanGoForward);
            m.AddItem(CefMenuCommand.Reload, "Reload");
            m.AddSeparator();
            m.AddItem((CefMenuCommand)Print, "Print…");
            m.AddItem((CefMenuCommand)SavePdf, "Save as PDF…");
            m.AddItem(CefMenuCommand.SelectAll, "Select all");
            m.AddSeparator();
        }
        m.AddItem((CefMenuCommand)Source, "View page source");
        m.AddItem((CefMenuCommand)Inspect, "Inspect");
    }

    protected override bool OnContextMenuCommand(IWebBrowser b, IBrowser br, IFrame f, IContextMenuParams p, CefMenuCommand cmd, CefEventFlags flags)
    {
        int c = (int)cmd;
        if (c >= Spell0 && c < Spell0 + 4) { br.GetHost().ReplaceMisspelling(p.DictionarySuggestions[c - Spell0]); return true; }
        var link = p.LinkUrl; var src = p.SourceUrl; var sel = p.SelectionText; int x = p.XCoord, y = p.YCoord;
        UI.Run(() =>
        {
            switch (c)
            {
                case OpenLink: w.NewTab(link, true, t.Private, afterTab: t); break;
                case OpenPriv: w.OpenPrivateWindow(link); break;
                case CopyLink: Clipboard.SetText(link); break;
                case SaveLink: br.GetHost().StartDownload(link); break;
                case OpenImg: w.NewTab(src, true, t.Private, afterTab: t); break;
                case SaveImg: br.GetHost().StartDownload(src); break;
                case CopyImg: Clipboard.SetText(src); break;
                case Search: w.NewTab(Store.Resolve(sel), true, t.Private, afterTab: t); break;
                case Source: w.ViewSource(); break;
                case Inspect: br.GetHost().ShowDevTools(inspectElementAtX: x, inspectElementAtY: y); break;
                case Print: br.GetHost().Print(); break;
                case SavePdf: w.SavePdf(); break;
            }
        });
        return c >= OpenLink;
    }
}

class TabPermissions : PermissionHandler
{
    readonly Tab t;
    public TabPermissions(Tab t) { this.t = t; }
    protected override bool OnShowPermissionPrompt(IWebBrowser b, IBrowser br, ulong promptId, string origin, PermissionRequestType types, IPermissionPromptCallback cb)
    {
        UI.Run(() =>
        {
            var r = MessageBox.Show($"{origin} wants to use: {types}\n\nAllow?", "Site permission", MessageBoxButton.YesNo, MessageBoxImage.Question);
            cb.Continue(r == MessageBoxResult.Yes ? PermissionRequestResult.Accept : PermissionRequestResult.Deny);
            cb.Dispose();
        });
        return true;
    }
}

class TabFind : FindHandler
{
    readonly MainWindow w;
    public TabFind(MainWindow w) { this.w = w; }
    protected override void OnFindResult(IWebBrowser b, IBrowser br, int id, int count, CefSharp.Structs.Rect rect, int active, bool final)
        => UI.Run(() => w.OnFindResult(active, count));
}

class TabRequest : RequestHandler
{
    protected override bool OnCertificateError(IWebBrowser b, IBrowser br, CefErrorCode code, string url, ISslInfo ssl, IRequestCallback cb)
    {
        UI.Run(() =>
        {
            var r = MessageBox.Show("The security certificate for this site is not trusted (" + code + ").\n\n" + url + "\n\nProceed anyway? This is unsafe.", "Certificate problem", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            cb.Continue(r == MessageBoxResult.Yes); cb.Dispose();
        });
        return true;
    }

    static readonly string[] Block = { "doubleclick.net", "googlesyndication.com", "googleadservices.com", "google-analytics.com", "googletagmanager.com", "adservice.google.com", "facebook.net", "adnxs.com", "taboola.com", "outbrain.com", "scorecardresearch.com", "criteo.com", "amazon-adsystem.com", "moatads.com", "quantserve.com", "hotjar.com", "adsrvr.org", "pubmatic.com", "rubiconproject.com", "openx.net" };
    class Ads : CefSharp.Handler.ResourceRequestHandler
    {
        protected override CefReturnValue OnBeforeResourceLoad(IWebBrowser b, IBrowser br, IFrame f, IRequest r, IRequestCallback cb)
        {
            if (!Store.D.Settings.AdBlock || r.ResourceType == ResourceType.MainFrame) return CefReturnValue.Continue;
            try
            {
                var h = new Uri(r.Url).Host;
                if (Block.Any(x => h == x || h.EndsWith("." + x))) return CefReturnValue.Cancel;
            }
            catch { }
            return CefReturnValue.Continue;
        }
    }
    static readonly Ads ads = new();
    protected override IResourceRequestHandler GetResourceRequestHandler(IWebBrowser b, IBrowser br, IFrame f, IRequest r, bool nav, bool dl, string initiator, ref bool disable)
        => Store.D.Settings.AdBlock ? ads : null;

    protected override bool GetAuthCredentials(IWebBrowser b, IBrowser br, string origin, bool isProxy, string host, int port, string realm, string scheme, IAuthCallback cb)
    {
        UI.Run(() =>
        {
            var d = new AuthWindow(host + (realm != "" ? " – " + realm : ""));
            if (d.ShowDialog() == true) cb.Continue(d.User, d.Pass); else cb.Cancel();
            cb.Dispose();
        });
        return true;
    }
}

class TabJsDialog : JsDialogHandler
{
    readonly MainWindow w;
    public TabJsDialog(MainWindow w) { this.w = w; }
    protected override bool OnJSDialog(IWebBrowser b, IBrowser br, string origin, CefJsDialogType type, string msg, string def, IJsDialogCallback cb, ref bool suppress)
    {
        UI.Run(() =>
        {
            string host = ""; try { host = new Uri(origin).Host; } catch { }
            var title = host == "" ? "Page says" : host + " says";
            switch (type)
            {
                case CefJsDialogType.Alert: MessageBox.Show(w, msg, title, MessageBoxButton.OK); cb.Continue(true); break;
                case CefJsDialogType.Confirm: cb.Continue(MessageBox.Show(w, msg, title, MessageBoxButton.OKCancel) == MessageBoxResult.OK); break;
                default: var p = new PromptWindow(title, msg, def) { Owner = w }; if (p.ShowDialog() == true) cb.Continue(true, p.Value); else cb.Continue(false); break;
            }
            cb.Dispose();
        });
        return true;
    }
    protected override bool OnBeforeUnloadDialog(IWebBrowser b, IBrowser br, string msg, bool isReload, IJsDialogCallback cb)
    {
        UI.Run(() => { cb.Continue(MessageBox.Show(w, "This page asks you to confirm that you want to leave.", "Leave site?", MessageBoxButton.OKCancel) == MessageBoxResult.OK); cb.Dispose(); });
        return true;
    }
}

class TabFocus : FocusHandler
{
    readonly Tab t; readonly MainWindow w;
    public TabFocus(Tab t, MainWindow w) { this.t = t; this.w = w; }
    protected override void OnGotFocus(IWebBrowser b, IBrowser br) => UI.Run(() => w.OnPaneFocus(t));
}
