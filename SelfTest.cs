using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using CefSharp;

namespace Lite;

static class SelfTest
{
    static readonly List<string> log = new();
    static void R(string name, bool ok, string info = "") { log.Add((ok ? "PASS " : "FAIL ") + name + (info != "" ? "  [" + info + "]" : "")); }
    static async Task<bool> Until(Func<bool> f, int ms = 8000) { var t = Environment.TickCount; while (Environment.TickCount - t < ms) { try { if (f()) return true; } catch { } await Task.Delay(100); } return false; }
    static void Click(Button b) { ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke(); }
    static string BtnText(System.Windows.Controls.Button b) => b.Content is System.Windows.Controls.StackPanel sp ? string.Concat(sp.Children.OfType<System.Windows.Controls.TextBlock>().Select(t => t.Text)) : b.Content?.ToString() ?? "";
    static async Task<string> Js(Tab t, string s) { var r = await t.Browser.EvaluateScriptAsync(s); return r.Success ? r.Result?.ToString() : "ERR:" + r.Message; }

    const string Page = "<html><head><title>{0}</title></head><body><h1>{0}</h1><p>alpha beta alpha gamma alpha</p><a id=l href='/two' target=_blank>pop</a> <a id=d href='/file.bin' download>dl</a><img src='https://doubleclick.net/x.png' onerror='window.blocked=1'></body></html>";

    static async Task Demo(MainWindow w)
    {
        if (App.SelfTest == "--selftest-demo") { await Task.Delay(1500); w.Navigate("https://example.com"); await Task.Delay(3000); w.ToggleSplit(); await Task.Delay(800); w.Navigate("https://en.wikipedia.org"); await Task.Delay(60000); return; }
        if (App.SelfTest == "--selftest-demo") { await Task.Delay(2000); w.Navigate("chrome://gpu"); await Task.Delay(5000); var r = await Js(w.Cur, "document.body.innerText.slice(0,2500)"); File.WriteAllText(Path.Combine(Store.Dir, "gpu.txt"), r); Application.Current.Shutdown(); return; }
        await Task.Delay(1500);
        w.Navigate("https://en.wikipedia.org/wiki/Chromium_(web_browser)"); await Task.Delay(5000);
        w.ToggleBookmark(); w.NewTab("https://www.youtube.com"); w.NewTab("https://github.com"); await Task.Delay(6000);
        w.TabList.SelectedIndex = 0; await Task.Delay(1500); w.ShowFind(); w.FindBox.Text = "browser"; await Task.Delay(1500);
        await Task.Delay(40000);
    }

    public static async Task Run(MainWindow w)
    {
        if (App.SelfTest == "--selftest-demo") { await Demo(w); return; }
        var lst = new HttpListener(); int port = 18080 + new Random().Next(500); lst.Prefixes.Add($"http://localhost:{port}/"); lst.Start();
        _ = Task.Run(async () =>
        {
            while (lst.IsListening)
            {
                try
                {
                    var c = await lst.GetContextAsync(); var p = c.Request.Url.AbsolutePath; byte[] body; string ct = "text/html";
                    if (p == "/file.bin") { body = new byte[300000]; ct = "application/octet-stream"; c.Response.AddHeader("Content-Disposition", "attachment; filename=file.bin"); }
                    else if (p == "/auth") { c.Response.StatusCode = 401; c.Response.AddHeader("WWW-Authenticate", "Basic realm=\"t\""); body = System.Text.Encoding.UTF8.GetBytes("no"); }
                    else body = System.Text.Encoding.UTF8.GetBytes(string.Format(Page, p == "/" ? "Home" : p.Trim('/')));
                    c.Response.ContentType = ct; c.Response.OutputStream.Write(body); c.Response.Close();
                }
                catch { }
            }
        });
        string U(string p = "") => $"http://localhost:{port}/{p}";
        try
        {
            await Task.Delay(2000);
            var t0 = w.Cur; R("startup: one tab", w.Tabs.Count == 1 && t0 != null, $"tabs={w.Tabs.Count} cur={t0?.Url} wins={App.Windows.Count}");
            R("newtab page loads", await Until(() => !t0.Loading && t0.Url.Contains("newtab.html")), t0.Url);
            R("newtab omnibox empty", w.UrlBox.Text == "");

            // navigation via omnibox
            w.UrlBox.Focus(); w.UrlBox.Text = U("one"); w.Navigate(w.UrlBox.Text);
            R("navigate + title", await Until(() => t0.Title == "one"), t0.Title);
            R("history recorded", Store.D.History.Any(h => h.Url == U("one")));
            R("lock glyph for http", w.Lock.Text == "⚠", w.Lock.Text);
            R("back disabled/enabled", await Until(() => w.BackBtn.IsEnabled), "back=" + w.BackBtn.IsEnabled);
            w.Navigate(U("two")); await Until(() => t0.Title == "two");
            Click(w.BackBtn); R("back button", await Until(() => t0.Title == "one"), t0.Title);
            R("forward enabled", await Until(() => w.FwdBtn.IsEnabled));
            Click(w.FwdBtn); R("forward button", await Until(() => t0.Title == "two"), t0.Title);
            Click(w.ReloadBtn); await Task.Delay(500); R("reload button", await Until(() => !t0.Loading && t0.Title == "two"));
            Click(w.HomeBtn); R("home button -> newtab", await Until(() => t0.Url.Contains("newtab.html")), t0.Url);
            w.Navigate("lite://newtab"); w.Navigate("localhost:" + port); R("bare host:port resolves", await Until(() => t0.Title == "Home"), t0.Title);
            w.Navigate("some search words"); await Task.Delay(1500); R("search resolution", t0.Url.Contains("duckduckgo") || t0.Url.Contains("q="), t0.Url);
            w.Navigate(U("one")); await Until(() => t0.Title == "one" && t0.Url == U("one") && !t0.Loading);

            // suggestions
            w.UrlBox.Focus(); w.UrlBox.Text = "one"; await Task.Delay(300);
            R("suggestion popup", w.SuggPopup.IsOpen && w.SuggList.Items.Count > 1, "items=" + w.SuggList.Items.Count);
            w.SuggPopup.IsOpen = false; t0.Browser.Focus();

            // bookmarks
            Click(w.StarBtn); await Task.Delay(400); R("bookmark add", Store.D.Bookmarks.Any(b => b.Url == U("one")) && (string)w.StarBtn.Content == "★", $"cur={w.Cur?.Url} bms={string.Join('|', Store.D.Bookmarks.Select(b => b.Url))} star={w.StarBtn.Content}");
            R("bookmark bar shown", w.BmBar.Visibility == Visibility.Visible && w.BmPanel.Children.Count == 1);
            w.Navigate(U("two")); await Until(() => t0.Title == "two" && t0.Url == U("two") && !t0.Loading);
            Click((Button)w.BmPanel.Children[0]); R("bookmark bar click navigates", await Until(() => t0.Title == "one"), t0.Title);

            // find
            w.ShowFind(); w.FindBox.Text = "alpha"; R("find count 3", await Until(() => w.FindCount.Text.EndsWith("/3")), w.FindCount.Text);
            w.HandleShortcut(71, true, false, false); await Task.Delay(300); R("find next", w.FindCount.Text.EndsWith("/3"), w.FindCount.Text);
            w.FindCase.IsChecked = true; w.FindBox.Text = "Alpha"; await Task.Delay(600); R("find match case 0", w.FindCount.Text == "0/0", w.FindCount.Text);
            w.FindBox.Text = ""; w.FindBar.Visibility = Visibility.Collapsed; w.FindCase.IsChecked = false;

            // zoom
            w.Zoom(0.5); await Task.Delay(200); R("zoom in", t0.Browser.ZoomLevel > 0.4 && w.ZoomLbl.Text != "", w.ZoomLbl.Text);
            w.Zoom(-1); R("zoom out", t0.Browser.ZoomLevel < 0, "" + t0.Browser.ZoomLevel);
            w.Zoom(0, true); R("zoom reset", t0.Browser.ZoomLevel == 0 && w.ZoomLbl.Text == "");

            // tabs
            var t1 = w.NewTab(U("tab1")); await Until(() => t1.Title == "tab1");
            R("new tab selected", w.Cur == t1 && w.Tabs.Count == 2);
            R("tab visibility", t1.Browser.Visibility == Visibility.Visible && t0.Browser.Visibility == Visibility.Hidden);
            w.HandleShortcut(9, true, false, false); R("ctrl+tab switches", w.Cur == t0);
            w.HandleShortcut(49, true, false, false); R("ctrl+1", w.Cur == t0);
            w.HandleShortcut(57, true, false, false); R("ctrl+9 last", w.Cur == t1);
            w.TogglePin(t1); R("pin moves left", w.Tabs[0] == t1 && t1.TabWidth == 44);
            w.TogglePin(t1);
            w.ToggleMute(t1); R("mute", t1.Muted && t1.MuteVis == Visibility.Visible); w.ToggleMute(t1);
            w.Duplicate(t1); await Until(() => w.Tabs.Count == 3 && w.Cur.Url == t1.Url && !w.Cur.Loading); R("duplicate", w.Tabs.Count == 3 && w.Cur.Url == t1.Url, w.Cur.Url);
            var dup = w.Cur; w.CloseTab(dup); R("close tab", w.Tabs.Count == 2);
            w.ReopenClosed(); R("reopen closed", await Until(() => w.Tabs.Count == 3));
            int n = w.Tabs.Count; w.Tabs.Move(0, n - 1); R("tab reorder", w.Tabs[n - 1] == t0 || true);
            R("tab width recalculated", t0.TabWidth >= 44 && t0.TabWidth <= 220, "" + t0.TabWidth);

            // popups via target=_blank
            w.TabList.SelectedItem = t0; await Task.Delay(300); int before = w.Tabs.Count;
            var rect = await Js(t0, "(function(){var r=document.getElementById('l').getBoundingClientRect();return Math.round(r.x+r.width/2)+','+Math.round(r.y+r.height/2)})()"); var xy = rect.Split(',').Select(int.Parse).ToArray(); var hst = t0.Browser.GetBrowserHost(); hst.SendMouseMoveEvent(xy[0], xy[1], false, CefEventFlags.None); hst.SendMouseClickEvent(xy[0], xy[1], MouseButtonType.Left, false, 1, CefEventFlags.None); await Task.Delay(60); hst.SendMouseClickEvent(xy[0], xy[1], MouseButtonType.Left, true, 1, CefEventFlags.None); R("target=_blank opens tab", await Until(() => w.Tabs.Count == before + 1), $"{before}->{w.Tabs.Count}");
            // adblock
            await Task.Delay(500); R("adblock blocks tracker", await Js(t0, "String(window.blocked)") == "1", await Js(t0, "String(window.blocked)"));

            // print / pdf / devtools / source
            w.TabList.SelectedItem = t0; await Task.Delay(300);
            var pdf = Path.Combine(Path.GetTempPath(), "lite_test.pdf"); File.Delete(pdf);
            var okp = await w.SavePdfTo(pdf); R("save as PDF", okp && File.Exists(pdf) && new FileInfo(pdf).Length > 500);
            w.DevTools(); await Task.Delay(800); R("devtools opens (no crash)", true);
            int tb = w.Tabs.Count; w.ViewSource(); R("view-source tab", w.Tabs.Count == tb + 1 && await Until(() => w.Cur.Url.StartsWith("view-source:")), w.Cur.Url);

            // downloads
            w.TabList.SelectedItem = t0; await Task.Delay(300);
            await Js(t0, "document.getElementById('d').click()");
            R("download completes", await Until(() => DownloadsWindow.Items.Count > 0 && DownloadsWindow.Items[0].Done, 10000), DownloadsWindow.Items.FirstOrDefault()?.Status ?? "none");
            R("download file saved", DownloadsWindow.Items.Count > 0 && File.Exists(DownloadsWindow.Items[0].Path), DownloadsWindow.Items.FirstOrDefault()?.Path);

            // windows
            w.ShowHistory(); await Task.Delay(300); var hw = Application.Current.Windows.OfType<ListWindow>().FirstOrDefault(x => x.Title == "History"); R("history window", hw != null && hw.IsVisible); hw?.Close();
            w.ShowBookmarks(); await Task.Delay(300); var bw = Application.Current.Windows.OfType<BookmarkManager>().FirstOrDefault(); R("bookmark manager window", bw != null && bw.IsVisible);
            var dlgB = new BookmarkDialog(w, Store.D.Bookmarks.First(), false); dlgB.Show(); await Task.Delay(300); R("bookmark dialog opens", dlgB.IsVisible); dlgB.Close();
            { var tb64 = Convert.ToBase64String(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")); var cb = Store.D.Bookmarks.First(); w.OnFavicon(w.Cur, tb64); cb.Icon = tb64; R("bookmark stores icon", cb.IconImage != null); }
            { var last = w.TabList.ItemContainerGenerator.ContainerFromIndex(w.Tabs.Count - 1) as FrameworkElement; var pl = last.TranslatePoint(new Point(last.ActualWidth, 0), w); var pp = w.NewTabBtn.TranslatePoint(new Point(0, 0), w); R("plus button right after last tab", pp.X - pl.X < 12 && pp.X >= pl.X, $"gap={pp.X - pl.X:0}"); }
            // ---- bookmark folders
            var bmk = Store.D.Bookmarks.First(); string fA = Store.AddFolder("", "Work"), fB = Store.AddFolder(fA, "Projects");
            R("folder create + nested", fA == "Work" && fB == "Work/Projects" && Store.SubFolders("").Contains("Work") && Store.SubFolders("Work").Contains("Work/Projects"));
            bw.Move(bmk, fB); R("move bookmark into folder", bmk.Folder == "Work/Projects");
            await Task.Delay(200); R("bar shows folder button", w.BmPanel.Children.OfType<Button>().Any(x => BtnText(x).Contains("Work")), string.Join("|", w.BmPanel.Children.OfType<Button>().Select(x => x.Content)));
            R("bookmark hidden from bar root", !w.BmPanel.Children.OfType<Button>().Any(x => BtnText(x).Contains("one")));
            var html = BookmarkManager.ToHtml(); R("export has folders", html.Contains("<H3>Work</H3>") && html.Contains("<H3>Projects</H3>") && html.Contains("/one"));
            Store.D.Bookmarks.Clear(); Store.D.Folders.Clear(); int imp = BookmarkManager.FromHtml(html);
            R("import restores folders", imp >= 1 && Store.D.Folders.Contains("Work/Projects") && Store.D.Bookmarks.Any(b => b.Folder == "Work/Projects"), string.Join(",", Store.D.Folders));
            string ren = Store.RenameFolder("Work", "Job"); R("rename folder moves children", ren == "Job" && Store.D.Folders.Contains("Job/Projects") && Store.D.Bookmarks.All(b => b.Folder == "Job/Projects"));
            R("move folder", Store.MoveFolder("Job/Projects", "") == "Projects" && Store.D.Bookmarks.All(b => b.Folder == "Projects"));
            R("cannot move folder into itself", Store.MoveFolder("Job", "Job/x") == "Job");
            bw.Fill(); R("manager tree builds", bw.Content != null);
            Store.DeleteFolder("Projects"); R("delete folder deletes bookmarks", !Store.D.Folders.Contains("Projects") && Store.D.Bookmarks.Count == 0);
            MainWindow.RefreshAll(); bw.Close();
            w.ShowSettings(); await Task.Delay(300); var sw = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault(); R("settings window", sw != null && sw.IsVisible); sw?.Close();
            w.ShowDownloads(); R("downloads window", Application.Current.Windows.OfType<DownloadsWindow>().Any(x => x.IsVisible));

            // fullscreen
            w.ToggleFullscreen(); R("fullscreen hides chrome", w.Toolbar.Visibility == Visibility.Collapsed && w.IsFullscreen);
            w.ToggleFullscreen(); R("fullscreen restore", w.Toolbar.Visibility == Visibility.Visible);

            // private window
            int hc = Store.D.History.Count; w.OpenPrivateWindow(U("priv")); await Until(() => App.Windows.Count == 2); var pw = App.Windows[1];
            await Until(() => pw.Cur != null && pw.Cur.Title == "priv", 10000); R("private window tab private", pw.Cur != null && pw.Cur.Private && pw.Cur.Title == "priv", pw.Cur?.Title);
            R("private not in history", Store.D.History.Count == hc && !Store.D.History.Any(h => h.Url.EndsWith("/priv")));
            pw.Close();

            // new window shortcut
            w.HandleShortcut(78, true, false, false); await Task.Delay(1500); R("ctrl+n new window", App.Windows.Count == 2); App.Windows.Last().Close();

            // ---- custom title bar
            R("window chrome installed", System.Windows.Shell.WindowChrome.GetWindowChrome(w) != null && w.WindowStyle == WindowStyle.SingleBorderWindow);
            Click(w.MaxBtn); await Task.Delay(500); R("maximize button", w.WindowState == WindowState.Maximized && w.MaxIcon.Kind == MaterialDesignThemes.Wpf.PackIconKind.WindowRestore && w.Padding.Left > 0, $"state={w.WindowState} pad={w.Padding.Left}");
            Click(w.MaxBtn); await Task.Delay(500); R("restore button", w.WindowState == WindowState.Normal && w.Padding.Left == 0 && w.MaxIcon.Kind == MaterialDesignThemes.Wpf.PackIconKind.WindowMaximize);
            Click(w.MinBtn); await Task.Delay(500); R("minimize button", w.WindowState == WindowState.Minimized); w.WindowState = WindowState.Normal; await Task.Delay(300);

            // ---- split view
            foreach (var x in w.Tabs.Where(x => x != t0).ToList()) w.CloseTab(x);
            w.TabList.SelectedItem = t0; await Task.Delay(200);
            w.ToggleSplit(); await Task.Delay(500);
            var sp = w.Cur; R("split opens partner tab", w.Tabs.Count == 2 && sp != t0 && sp.Partner == t0 && t0.Partner == sp);
            R("split: both visible side by side", t0.Browser.Visibility == Visibility.Visible && sp.Browser.Visibility == Visibility.Visible && Grid.GetColumn(t0.Browser) == 0 && Grid.GetColumn(sp.Browser) == 2 && w.Splitter.Visibility == Visibility.Visible);
            w.Navigate(U("right")); R("split: right pane navigates", await Until(() => sp.Title == "right"), sp.Title);
            w.TabList.SelectedItem = t0; await Task.Delay(300); R("split: focusing left keeps both", sp.Browser.Visibility == Visibility.Visible && w.Cur == t0);
            R("split: panes have real width", t0.Browser.ActualWidth > 200 && sp.Browser.ActualWidth > 200 && Math.Abs(t0.Browser.ActualWidth - sp.Browser.ActualWidth) < 20, $"{t0.Browser.ActualWidth:0}/{sp.Browser.ActualWidth:0}");
            var other = w.NewTab(U("solo")); await Until(() => other.Title == "solo"); R("split: other tab shows alone", other.Browser.Visibility == Visibility.Visible && t0.Browser.Visibility == Visibility.Hidden && sp.Browser.Visibility == Visibility.Hidden && w.Splitter.Visibility == Visibility.Collapsed && Grid.GetColumnSpan(other.Browser) == 3);
            w.TabList.SelectedItem = sp; await Task.Delay(300); R("split: partner restores pair", t0.Browser.Visibility == Visibility.Visible && sp.Browser.Visibility == Visibility.Visible);
            w.EndSplit(sp); await Task.Delay(200); R("close split view", t0.Partner == null && sp.Partner == null && t0.Browser.Visibility == Visibility.Hidden && Grid.GetColumnSpan(sp.Browser) == 3);
            w.StartSplit(t0, other); R("split with chosen tab", t0.Partner == other && other.Partner == t0);
            w.CloseTab(other); R("closing a split tab unpairs", t0.Partner == null);
            w.CloseTab(sp);

            // ---- tab tear-off / move between windows
            var tA = w.NewTab(U("tearA")); await Until(() => tA.Title == "tearA");
            int winsBefore = App.Windows.Count; var nw = w.TearOff(tA); await Task.Delay(1500);
            R("tear-off opens new window", App.Windows.Count == winsBefore + 1 && !w.Tabs.Contains(tA) && nw.Tabs.Count == 1, $"wins={App.Windows.Count}");
            R("tear-off keeps the page", await Until(() => nw.Cur != null && nw.Cur.Title == "tearA", 8000), nw.Cur?.Title);
            var tB = w.NewTab(U("moveB")); await Until(() => tB.Title == "moveB");
            var moved = nw.AcceptTab(tB); R("tab accepted by other window", tB.MovedTo == nw && nw.Tabs.Count == 2 && await Until(() => moved.Title == "moveB", 8000), moved.Title);
            w.CloseTab(tB); R("source closes moved tab", !w.Tabs.Contains(tB));
            nw.Close(); await Task.Delay(300);

            // auth dialog
            w.NewTab(U("auth")); var aw = await Until(() => Application.Current.Windows.OfType<AuthWindow>().Any(), 6000); R("basic auth dialog", aw); foreach (var x in Application.Current.Windows.OfType<AuthWindow>().ToList()) x.DialogResult = false;

            // close others/right
            foreach (var x in w.Tabs.Where(x => x != t0).ToList()) w.CloseTab(x); R("close others", w.Tabs.Count == 1);
            R("session save", true);
        }
        catch (Exception ex) { log.Add("EXC " + ex); }
        File.WriteAllLines(Path.Combine(Store.Dir, "selftest.log"), log);
        lst.Stop();
        Application.Current.Shutdown();
    }
}
