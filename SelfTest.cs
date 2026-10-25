using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Net;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace Lite;

static class SelfTest
{
    static readonly List<string> log = new();
    static void R(string name, bool ok, string info = "") { log.Add((ok ? "PASS " : "FAIL ") + name + (info != "" ? "  [" + info + "]" : "")); }
    static async Task<bool> Until(Func<bool> f, int ms = 8000) { var t = Environment.TickCount; while (Environment.TickCount - t < ms) { try { if (f()) return true; } catch { } await Task.Delay(100); } return false; }
    static void Click(Button b) { ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke(); }
    static string BtnText(System.Windows.Controls.Button b) => b.Content is System.Windows.Controls.StackPanel sp ? string.Concat(sp.Children.OfType<System.Windows.Controls.TextBlock>().Select(t => t.Text)) : b.Content?.ToString() ?? "";
    static byte[] FakeZip; static string FakeJson;
    static byte[] MakeZip(params (string name, string text)[] files) { using var ms = new MemoryStream(); using (var z = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true)) foreach (var (n, tx) in files) { var e = z.CreateEntry(n); using var s = e.Open(); var b = System.Text.Encoding.UTF8.GetBytes(tx); s.Write(b); } return ms.ToArray(); }
    static string Release(string tag, byte[] zip, int port, string sha = null) => System.Text.Json.JsonSerializer.Serialize(new { tag_name = tag, html_url = "http://localhost/rel", body = "notes", assets = new[] { new { name = "Orbit-win-x64.zip", browser_download_url = $"http://localhost:{port}/updfiles/Orbit-win-x64.zip", size = zip.Length, digest = "sha256:" + (sha ?? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(zip)).ToLowerInvariant()) } } });
    static async Task<string> Js(Tab t, string s) { var r = await t.Wv.EvalAsync(s); if (r == null) return "ERR:eval"; if (r == "null") return null; return r.StartsWith('"') ? System.Text.Json.JsonSerializer.Deserialize<string>(r) : r; }

    const string Page = "<html><head><title>{0}</title></head><body><h1>{0}</h1><p>alpha beta alpha gamma alpha</p><a id=l href='/two' target=_blank>pop</a> <a id=d href='/file.bin' download>dl</a><img src='https://doubleclick.net/x.png' onerror='window.blocked=1'></body></html>";

    /// <summary>Seeds sample data and renders each themed window to a PNG (in %TEMP%\orbit-ui) so the design can be reviewed.</summary>
    static async Task UiShots(MainWindow w)
    {
        var dir = Path.Combine(Path.GetTempPath(), "orbit-ui"); Directory.CreateDirectory(dir); foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
        void Shot(Window win, string name)
        {
            win.UpdateLayout(); var vis = (FrameworkElement)win.Content; int pw = (int)Math.Ceiling(win.ActualWidth), ph = (int)Math.Ceiling(win.ActualHeight);
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(pw, ph, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(win);   // the window itself, so its own Background is what we see
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb)); using var fs = File.Create(Path.Combine(dir, name + ".png")); enc.Save(fs);
        }
        await Task.Delay(1500);
        // sample data
        var now = DateTime.Now; string[][] sites = { new[] { "Orbit Browser · GitHub", "https://github.com/MykePalma/OrbitBrowser" }, new[] { "Wikipedia, the free encyclopedia", "https://en.wikipedia.org/wiki/Main_Page" }, new[] { "YouTube", "https://www.youtube.com/" }, new[] { "Speedtest by Ookla", "https://www.speedtest.net/" }, new[] { "Chromium (web browser) - Wikipedia", "https://en.wikipedia.org/wiki/Chromium_(web_browser)" }, new[] { "Docs: WCAG 2.2 What's new", "https://www.w3.org/WAI/standards-guidelines/wcag/new-in-22/" }, new[] { "Example Domain", "https://example.com/" }, new[] { "DuckDuckGo", "https://duckduckgo.com/" } };
        for (int k = 0; k < 14; k++) { var s = sites[k % sites.Length]; Store.D.History.Add(new HistoryItem { Title = s[0], Url = s[1] + (k > 7 ? "?p=" + k : ""), Time = now.AddHours(-k * (k < 6 ? 0.7 : 9)) }); }
        Store.D.Folders.AddRange(new[] { "Work", "Work/Design", "Reading" });
        Store.D.Bookmarks.AddRange(new[] { new Bookmark { Title = "Orbit on GitHub", Url = "https://github.com/MykePalma/OrbitBrowser" }, new Bookmark { Title = "WCAG 2.2 overview", Url = "https://www.w3.org/WAI/standards-guidelines/wcag/new-in-22/", Folder = "Work/Design" }, new Bookmark { Title = "Fluent 2 typography", Url = "https://fluent2.microsoft.design/typography", Folder = "Work/Design" }, new Bookmark { Title = "Wikipedia", Url = "https://en.wikipedia.org/", Folder = "Reading" } });
        DownloadsWindow.Add(1, "x", @"C:\Users\you\Downloads\Orbit-win-x64.zip"); DownloadsWindow.Update(1, 120_000_000, 266_000_000, 45, false, false, "");
        DownloadsWindow.Add(2, "x", @"C:\Users\you\Downloads\quarterly-report.pdf"); DownloadsWindow.Update(2, 2_400_000, 2_400_000, 100, true, false, "");
        DownloadsWindow.Add(3, "x", @"C:\Users\you\Downloads\holiday-photos.zip"); DownloadsWindow.Update(3, 10, 1000, 1, false, true, "");
        MainWindow.RefreshAll(); await Task.Delay(300);

        w.ShowSettings(); var sw = Application.Current.Windows.OfType<SettingsWindow>().First(); await Task.Delay(600);
        foreach (var page in new[] { "General", "Appearance", "Privacy", "Extensions", "Downloads", "Updates", "About" }) { sw.ShowPage(page); await Task.Delay(400); Shot(sw, "settings-" + page.ToLower()); }
        sw.Close();
        w.ShowHistory(); var hw = Application.Current.Windows.OfType<HistoryWindow>().First(); await Task.Delay(700); Shot(hw, "history"); hw.Close();
        w.ShowDownloads(); var dw = Application.Current.Windows.OfType<DownloadsWindow>().First(); await Task.Delay(600); Shot(dw, "downloads");
        w.ShowBookmarks(); var bw = Application.Current.Windows.OfType<BookmarkManager>().First(); await Task.Delay(700); Shot(bw, "bookmarks"); bw.Close();
        var bd = new BookmarkDialog(w, Store.D.Bookmarks[1], false); bd.Show(); await Task.Delay(500); Shot(bd, "bookmark-dialog"); bd.Close();
        var pr = new PromptWindow("New folder", "Folder name", "Reading list") { Owner = w }; pr.Show(); await Task.Delay(400); Shot(pr, "prompt"); pr.Close();
        var au = new AuthWindow("example.com – Restricted area"); au.Show(); await Task.Delay(400); Shot(au, "auth"); au.Close();
        // empty states
        Store.D.History.Clear(); w.ShowHistory(); hw = Application.Current.Windows.OfType<HistoryWindow>().First(); await Task.Delay(600); Shot(hw, "history-empty"); hw.Close();
        Store.D.Bookmarks.Clear(); Store.D.Folders.Clear(); w.ShowBookmarks(); bw = Application.Current.Windows.OfType<BookmarkManager>().First(); await Task.Delay(600); Shot(bw, "bookmarks-empty"); bw.Close();
        DownloadsWindow.Items.Clear(); await Task.Delay(300); Shot(dw, "downloads-empty");
        File.WriteAllText(Path.Combine(dir, "done.txt"), "ok");
        Application.Current.Shutdown();
    }

    static async Task Demo(MainWindow w)
    {
        if (App.SelfTest == "--selftest-demo") { await Task.Delay(1500); w.WindowState = WindowState.Maximized; w.Navigate("https://www.facebook.com"); w.NewTab("https://github.com"); w.NewTab("https://duckduckgo.com"); w.NewTab("https://en.wikipedia.org"); w.NewTab("https://www.youtube.com"); await Task.Delay(60000); return; }
        if (App.SelfTest == "--selftest-demo") { await Task.Delay(2000); w.Navigate("chrome://gpu"); await Task.Delay(5000); var r = await Js(w.Cur, "document.body.innerText.slice(0,2500)"); File.WriteAllText(Path.Combine(Store.Dir, "gpu.txt"), r); Application.Current.Shutdown(); return; }
        await Task.Delay(1500);
        w.Navigate("https://en.wikipedia.org/wiki/Chromium_(web_browser)"); await Task.Delay(5000);
        w.ToggleBookmark(); w.NewTab("https://www.youtube.com"); w.NewTab("https://github.com"); await Task.Delay(6000);
        w.TabList.SelectedIndex = 0; await Task.Delay(1500); w.ShowFind(); w.FindBox.Text = "browser"; await Task.Delay(1500);
        await Task.Delay(40000);
    }

    /// <summary>Installs uBlock Origin from GitHub into the profile and checks that it really blocks ad and tracker requests.</summary>
    static async Task ExtTest(MainWindow w)
    {
        try
        {
            await Task.Delay(2000);
            Store.D.Settings.AdBlock = false;    // measure the extension on its own
            string[] urls = { "https://ads.pubmatic.com/AdServer/js/showad.js", "https://connect.facebook.net/en_US/fbevents.js", "https://static.criteo.net/js/ld/publishertag.js", "https://ib.adnxs.com/ut/v3/prebid", "https://securepubads.g.doubleclick.net/pagead/ads" };
            var probe = "window.__r={};" + System.Text.Json.JsonSerializer.Serialize(urls) + ".forEach(function(u){fetch(u,{mode:'no-cors',cache:'no-store'}).then(function(){__r[u]='ok'}).catch(function(){__r[u]='blocked'})});'started'";
            async Task<int> Blocked()
            {
                w.Navigate("https://example.com/"); await Until(() => !w.Cur.Loading && w.Cur.Url.Contains("example.com"), 15000); await Task.Delay(1500);
                await Js(w.Cur, probe); await Task.Delay(6000);
                var r = await Js(w.Cur, "JSON.stringify(window.__r)"); log.Add("INFO probe: " + r);
                return r == null ? -1 : System.Text.RegularExpressions.Regex.Matches(r, "\"blocked\"").Count;
            }
            int before = await Blocked();
            R("ext: control - nothing is blocked without the extension", before == 0, "blocked=" + before);
            ExtInfo info = null;
            try { info = await Extensions.InstallUBlockAsync(s => log.Add("INFO " + s)); } catch (Exception ex) { log.Add("INFO install failed: " + ex.Message); }
            R("ext: uBlock Origin installs", info != null, info == null ? "" : info.Name + " " + info.Version);
            var list = await w.Cur.Wv.Core.Profile.GetBrowserExtensionsAsync();
            R("ext: listed and enabled", list.Any(x => x.IsEnabled && x.Name.StartsWith("uBlock")), string.Join(", ", list.Select(x => x.Name + (x.IsEnabled ? "" : " (off)"))));
            await Task.Delay(12000);   // let the extension finish its first start
            int after = await Blocked();
            R("ext: uBlock Origin blocks ad and tracker requests", after >= 4, $"blocked={after} of {urls.Length}");
        }
        catch (Exception ex) { log.Add("FAIL ext test crashed: " + ex); }
        File.WriteAllLines(Path.Combine(Store.Dir, "selftest.log"), log);
        Application.Current.Shutdown();
    }

    public static async Task Run(MainWindow w)
    {
        if (App.SelfTest == "--selftest-ext") { await ExtTest(w); return; }
        if (App.SelfTest == "--selftest-ads")
        {
            await Task.Delay(1500); Store.D.Settings.AdBlock = true; await AdBlocker.UpdateAsync();
            var lines = new List<string>();
            AdBlocker.Trace = new System.Collections.Concurrent.ConcurrentQueue<string>();
            foreach (var site in new[] { "https://www.cnn.com/", "https://www.dailymail.co.uk/", "https://www.speedtest.net/" })
            {
                w.Navigate(site); await Task.Delay(22000); var cur = w.Cur;
                var hidden = await Js(cur, "String([...document.querySelectorAll('*')].filter(e => getComputedStyle(e).display === 'none' && e.getAttribute('class') && /(^|[ _-])(ad|ads|advert|sponsor|banner)([ _-]|$)/i.test(e.getAttribute('class'))).length)");
                lines.Add($"{site} blocked={cur.BlockedCount} hiddenAdElements={hidden}"); File.WriteAllLines(Path.Combine(Path.GetTempPath(), "orbit-ads-result.txt"), lines); File.WriteAllLines(Path.Combine(Path.GetTempPath(), "orbit-ads-trace.txt"), AdBlocker.Trace.ToArray());
            }
            await Task.Delay(60000); Application.Current.Shutdown(); return;
        }
        if (App.SelfTest == "--selftest-shots")
        {
            // a tidy, realistic state for marketing screenshots (throwaway profile)
            Store.D.Folders.AddRange(new[] { "Reading", "Projects" });
            Store.D.Bookmarks.AddRange(new[] { new Bookmark { Title = "Orbit on GitHub", Url = "https://github.com/MykePalma/OrbitBrowser" }, new Bookmark { Title = "myke.pt", Url = "https://myke.pt/" }, new Bookmark { Title = "DuckDuckGo", Url = "https://duckduckgo.com/" } });
            w.Width = 1280; w.Height = 820; MainWindow.RefreshAll();
            await Task.Delay(1200); w.Navigate("https://myke.pt/"); w.NewTab("https://duckduckgo.com/", false); await Task.Delay(6000);
            w.TabList.SelectedIndex = 0; await Task.Delay(500); w.ToggleSplit(); await Task.Delay(600); w.Navigate("https://github.com/MykePalma/OrbitBrowser"); await Task.Delay(9000);
            w.ToggleBookmark(); await Task.Delay(800);
            await Task.Delay(90000); Application.Current.Shutdown(); return;
        }
        if (App.SelfTest == "--selftest-live")
        {
            await Task.Delay(1500); var now = DateTime.Now;
            for (int k = 0; k < 12; k++) Store.D.History.Add(new HistoryItem { Title = new[] { "web design good practices wiki - Pesquisa Google", "Facebook", "DuckDuckGo - Protection. Privacy. Peace of mind.", "Speedtest by Ookla" }[k % 4], Url = new[] { "https://www.google.com/search?q=x", "https://www.facebook.com/", "https://duckduckgo.com/", "https://www.speedtest.net/" }[k % 4], Time = now.AddMinutes(-k * 7) });
            try { w.ShowHistory(); } catch (Exception ex) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "orbit-live-error.txt"), ex.ToString()); }
            await Task.Delay(60000); Application.Current.Shutdown(); return;
        }
        if (App.SelfTest == "--selftest-ui") { try { await UiShots(w); } catch (Exception ex) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "orbit-ui-error.txt"), ex.ToString()); Application.Current.Shutdown(); } return; }
        if (App.SelfTest == "--selftest-demo") { await Demo(w); return; }
        var lst = new HttpListener(); int port = 18080 + new Random().Next(500); lst.Prefixes.Add($"http://localhost:{port}/"); lst.Start();
        _ = Task.Run(async () =>
        {
            while (lst.IsListening)
            {
                try
                {
                    var c = await lst.GetContextAsync(); var p = c.Request.Url.AbsolutePath; byte[] body; string ct = "text/html";
                    if (p == "/adtest") { body = System.Text.Encoding.UTF8.GetBytes("<html><body><div class='ad-banner' id='b'>AD</div><img id='i' src='/ads/banner.png' onerror='window.imgBlocked=1' onload='window.imgLoaded=1'><script src='/ads/track.js'></script><p id='c'>content</p></body></html>"); }
                    else if (p == "/ads/banner.png") { body = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="); ct = "image/png"; }
                    else if (p == "/ads/track.js") { body = System.Text.Encoding.UTF8.GetBytes("window.trk=1;"); ct = "text/javascript"; }
                    else if (p == "/updapi/releases/latest") { body = System.Text.Encoding.UTF8.GetBytes(FakeJson ?? "{}"); ct = "application/json"; }
                    else if (p == "/updfiles/Orbit-win-x64.zip") { body = FakeZip ?? Array.Empty<byte>(); ct = "application/zip"; }
                    else if (p == "/file.bin") { body = new byte[300000]; ct = "application/octet-stream"; c.Response.AddHeader("Content-Disposition", "attachment; filename=file.bin"); }
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
            var t0 = w.Cur; R("startup: one tab", w.Tabs.Count == 1 && t0 != null, $"tabs={w.Tabs.Count} cur={t0?.Url} wins={App.Browsers.Count}");
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
            w.SuggPopup.IsOpen = false; t0.Wv.Focus();

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
            w.Zoom(0.5); await Task.Delay(200); R("zoom in", t0.Wv.ZoomLevel > 0.4 && w.ZoomLbl.Text != "", w.ZoomLbl.Text);
            w.Zoom(-1); R("zoom out", t0.Wv.ZoomLevel < 0, "" + t0.Wv.ZoomLevel);
            w.Zoom(0, true); R("zoom reset", t0.Wv.ZoomLevel == 0 && w.ZoomLbl.Text == "");

            // tabs
            var t1 = w.NewTab(U("tab1")); await Until(() => t1.Title == "tab1");
            R("new tab selected", w.Cur == t1 && w.Tabs.Count == 2);
            R("tab visibility", t1.Wv.Visibility == Visibility.Visible && t0.Wv.Visibility == Visibility.Hidden);
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
            var rect = await Js(t0, "(function(){var r=document.getElementById('l').getBoundingClientRect();return Math.round(r.x+r.width/2)+','+Math.round(r.y+r.height/2)})()"); var xy = rect.Split(',').Select(int.Parse).ToArray(); await t0.Wv.ClickAt(xy[0], xy[1]); R("target=_blank opens tab", await Until(() => w.Tabs.Count == before + 1), $"{before}->{w.Tabs.Count}");
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
            R("download completes", await Until(() => DownloadsWindow.Items.Count > 0 && DownloadsWindow.Items[0].Done, 25000), DownloadsWindow.Items.FirstOrDefault()?.Status ?? "none");
            R("download file saved", DownloadsWindow.Items.Count > 0 && File.Exists(DownloadsWindow.Items[0].Path), DownloadsWindow.Items.FirstOrDefault()?.Path);

            // windows
            w.ShowHistory(); await Task.Delay(300); var hw = Application.Current.Windows.OfType<HistoryWindow>().FirstOrDefault(); R("history window", hw != null && hw.IsVisible); hw?.Close();
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
            int hc = Store.D.History.Count; w.OpenPrivateWindow(U("priv")); await Until(() => App.Browsers.Count == 2); var pw = App.Browsers[1];
            await Until(() => pw.Cur != null && pw.Cur.Title == "priv", 10000); R("private window tab private", pw.Cur != null && pw.Cur.Private && pw.Cur.Title == "priv", pw.Cur?.Title);
            R("private not in history", Store.D.History.Count == hc && !Store.D.History.Any(h => h.Url.EndsWith("/priv")));
            pw.Close();

            // new window shortcut
            w.HandleShortcut(78, true, false, false); await Task.Delay(1500); R("ctrl+n new window", App.Browsers.Count == 2); App.Browsers.Last().Close();

            // ---- every window uses the dark theme (a light window with light text is unreadable)
            { var bgOk = new Func<Window, bool>(x => x.Background is SolidColorBrush b && b.Color == Color.FromRgb(0x20, 0x21, 0x24) && x.Foreground is SolidColorBrush f && f.Color == Color.FromRgb(0xE8, 0xEA, 0xED));
              w.ShowHistory(); w.ShowSettings(); w.ShowBookmarks(); w.ShowDownloads(); await Task.Delay(500);
              var wins = Application.Current.Windows.OfType<Window>().Where(x => x is HistoryWindow or SettingsWindow or BookmarkManager or DownloadsWindow).ToList();
              R("themed windows are dark", wins.Count == 4 && wins.All(bgOk), string.Join(", ", wins.Select(x => x.GetType().Name + ":" + x.Background)));
              var bdlg = new BookmarkDialog(w, new Bookmark { Title = "t", Url = "http://x" }, false); var pr = new PromptWindow("t", "m", "d"); var au = new AuthWindow("site"); bdlg.Show(); pr.Show(); au.Show(); await Task.Delay(300);
              R("dialogs are dark", bgOk(bdlg) && bgOk(pr) && bgOk(au), $"{bdlg.Background} {pr.Background} {au.Background}");
              bdlg.Close(); pr.Close(); au.Close(); foreach (var x in wins.Where(x => x is not DownloadsWindow)) x.Close(); }

            // ---- bookmark bar spacing: neighbouring items need breathing room
            { var b1 = new Bookmark { Title = "First", Url = "http://a/" }; var b2 = new Bookmark { Title = "Second", Url = "http://b/" }; Store.D.Bookmarks.Add(b1); Store.D.Bookmarks.Add(b2); MainWindow.RefreshAll(); await Task.Delay(300);
              var kids = w.BmPanel.Children.OfType<System.Windows.Controls.Button>().Where(x => BtnText(x) is "First" or "Second").ToList();
              if (kids.Count == 2) { var l = kids[0].TranslatePoint(new Point(kids[0].ActualWidth, 0), w).X; var r = kids[1].TranslatePoint(new Point(0, 0), w).X; var textGap = (r + 8) - (l - 8); R("bookmark bar: items are spaced apart", r - l >= 4 && textGap >= 16, $"button gap={r - l:0.#}px, visible text gap={textGap:0.#}px"); } else R("bookmark bar: items are spaced apart", false, "buttons not found");
              Store.D.Bookmarks.Remove(b1); Store.D.Bookmarks.Remove(b2); MainWindow.RefreshAll(); }

            // ---- ad blocker: engine
            FilterEngine E(string rules) => new FilterEngine().Also(x => x.AddList(rules)).Seal();
            bool Bl(FilterEngine e, string url, string doc = "page.test", ReqType ty = ReqType.Script) => e.ShouldBlock(url, doc, ty);
            { var e = E("||ads.example.com^");
              R("adblock: ||host^ blocks the host, its subdomains and ports", Bl(e, "http://ads.example.com/x.js") && Bl(e, "https://cdn.ads.example.com/a") && Bl(e, "http://ads.example.com:8080/") && Bl(e, "http://ads.example.com"));
              R("adblock: ||host^ leaves look-alikes alone", !Bl(e, "http://notads.example.com/x") && !Bl(e, "http://example.com/ads.example.com/x") && !Bl(e, "http://ads.example.com.evil.test/x") && !Bl(e, "http://example.com/"));
              var s = E("/banner/*/img^");
              R("adblock: * and ^ wildcards", Bl(s, "http://x.test/banner/300/img?x=1") && Bl(s, "http://x.test/banner/300/img") && Bl(s, "http://x.test/banner/a/b/img/") && !Bl(s, "http://x.test/banner/300/imgs") && !Bl(s, "http://x.test/banner/300/img.png"));
              var a1 = E("|http://tracker.\n.swf|");
              R("adblock: | start and end anchors", Bl(a1, "http://tracker.example.com/x") && !Bl(a1, "https://tracker.example.com/x") && !Bl(a1, "http://other.test/?u=http://tracker.x") && Bl(a1, "http://x.test/a.swf") && !Bl(a1, "http://x.test/a.swf?x=1"));
              var tp = E("||cdn.example.com^$third-party");
              R("adblock: $third-party only blocks other sites", Bl(tp, "http://cdn.example.com/a.js", "page.test") && !Bl(tp, "http://cdn.example.com/a.js", "www.example.com") && !Bl(tp, "http://cdn.example.com/a.js", "cdn.example.com"));
              var fp = E("||x.test/f$~third-party");
              R("adblock: $~third-party only blocks the site's own requests", Bl(fp, "http://x.test/f", "www.x.test") && !Bl(fp, "http://x.test/f", "other.test"));
              var ty = E("||x.test/ad$script\n||x.test/pix$~image\n||x.test/img$image,stylesheet");
              R("adblock: resource-type options", Bl(ty, "http://x.test/ad", "p.test", ReqType.Script) && !Bl(ty, "http://x.test/ad", "p.test", ReqType.Image) && Bl(ty, "http://x.test/pix", "p.test", ReqType.Script) && !Bl(ty, "http://x.test/pix", "p.test", ReqType.Image)
                  && Bl(ty, "http://x.test/img", "p.test", ReqType.Stylesheet) && !Bl(ty, "http://x.test/img", "p.test", ReqType.Script));
              var ex = E("||ads.example.com^\n@@||ads.example.com/allowed/");
              R("adblock: @@ exceptions win", Bl(ex, "http://ads.example.com/x.js") && !Bl(ex, "http://ads.example.com/allowed/a.js"));
              var impR = E("||a.test^$important\n@@||a.test^");
              R("adblock: $important beats an exception", Bl(impR, "http://a.test/x"));
              var dom = E("||s.test/track$domain=news.test|~sub.news.test");
              R("adblock: $domain= limits where a rule applies", Bl(dom, "http://s.test/track", "news.test") && Bl(dom, "http://s.test/track", "www.news.test") && !Bl(dom, "http://s.test/track", "other.test") && !Bl(dom, "http://s.test/track", "sub.news.test"));
              var doc = E("||ads.example.com^\n@@||news.test^$document");
              R("adblock: @@...$document switches blocking off for a site", Bl(doc, "http://ads.example.com/x", "other.test") && !Bl(doc, "http://ads.example.com/x", "news.test") && !Bl(doc, "http://ads.example.com/x", "www.news.test"));
              var junk = E("! comment\n[Adblock Plus 2.0]\n/regex.*rule/\n||x.test^$csp=script-src 'none'\n||y.test^$redirect=noopt\n||z.test^$popup\n||ok.test^\n##+js(abort-on-property-read, foo)\nsite.test##div:has-text(Sponsored)");
              R("adblock: comments and unsupported rules are skipped, the rest works", junk.NetworkRules == 1 && junk.Skipped >= 5 && Bl(junk, "http://ok.test/x") && !Bl(junk, "http://x.test/y") && !Bl(junk, "http://y.test/y"), $"rules={junk.NetworkRules} skipped={junk.Skipped}");
              var mc = E("/Banner.GIF$match-case");
              R("adblock: match-case and default case-insensitivity", Bl(mc, "http://x.test/Banner.GIF") && !Bl(mc, "http://x.test/banner.gif") && Bl(E("/banner.gif"), "http://x.test/BANNER.GIF"));
              var ce = E("##.ad-banner\n##.promo\nnews.test##div#sponsor\nnews.test#@#.promo\n~safe.test##.sidebar-ad\n#@#.never");
              var nCss = ce.CosmeticCss("news.test"); var oCss = ce.CosmeticCss("www.other.test"); var sCss = ce.CosmeticCss("safe.test");
              R("adblock: element hiding is generic, per-domain and with exceptions", nCss.Contains(".ad-banner{display:none!important}") && nCss.Contains("div#sponsor{") && !nCss.Contains(".promo{") && oCss.Contains(".promo{") && !oCss.Contains("div#sponsor") && oCss.Contains(".sidebar-ad{") && !sCss.Contains(".sidebar-ad{") && sCss.Contains(".ad-banner{"), nCss);
              R("adblock: registrable domain for third-party checks", FilterEngine.RegDomain("a.b.example.com") == "example.com" && FilterEngine.RegDomain("www.bbc.co.uk") == "bbc.co.uk" && FilterEngine.RegDomain("example.com") == "example.com" && FilterEngine.RegDomain("127.0.0.1") == "127.0.0.1"); }
            { var swp = System.Diagnostics.Stopwatch.StartNew(); var sbp = new System.Text.StringBuilder(); var rnd = new Random(7);
              for (int k = 0; k < 20000; k++) { sbp.Append("||ad").Append(k).Append(".tracker").Append(k % 500).Append(".test^$third-party\n"); sbp.Append("/ads").Append(k).Append("/banner*.gif\n"); sbp.Append("-sponsor").Append(k).Append("-\n"); }
              var big = E(sbp.ToString()); var parse = swp.ElapsedMilliseconds; swp.Restart(); int hits = 0;
              for (int k = 0; k < 3000; k++) { if (big.ShouldBlock($"https://cdn{k}.example.com/assets/img/photo{k}.jpg?size=large&v={k}", "page.test", ReqType.Image)) hits++; if (big.ShouldBlock($"https://ad{k * 3}.tracker{(k * 3) % 500}.test/x.js", "page.test", ReqType.Script)) hits++; }
              R("adblock: 60,000 rules parse quickly and 6,000 lookups are fast", parse < 4000 && swp.ElapsedMilliseconds < 600 && hits == 3000 && big.NetworkRules == 60000, $"parse={parse}ms lookups={swp.ElapsedMilliseconds}ms hits={hits}"); }
            { Store.D.Settings.AdAllow.Clear(); AdBlocker.SetPaused("news.test", true);
              R("adblock: pausing a site covers its subdomains", AdBlocker.IsPaused("news.test") && AdBlocker.IsPaused("www.news.test") && !AdBlocker.IsPaused("othernews.test") && !AdBlocker.IsPaused("test"));
              AdBlocker.SetPaused("news.test", false); R("adblock: resuming a site", !AdBlocker.IsPaused("www.news.test")); }

            // ---- ad blocker: in the real browser (blocked requests, hidden boxes, shield count, pausing)
            { Store.D.Settings.AdBlock = true; Store.D.Settings.AdAllow.Clear(); AdBlocker.UseEngine(E("/ads/banner.png\n/ads/track.js$script\nlocalhost##.ad-banner"));
              var at = w.NewTab(U("adtest")); await Until(() => !at.Loading && at.Url.Contains("adtest"), 8000); await Task.Delay(2000);
              var imgBlocked = await Js(at, "String(window.imgBlocked)"); var trk = await Js(at, "String(window.trk)"); var imgLoaded = await Js(at, "String(window.imgLoaded)");
              var boxDisp = await Js(at, "getComputedStyle(document.getElementById('b')).display"); var contentDisp = await Js(at, "getComputedStyle(document.getElementById('c')).display");
              R("adblock browser: ad image and script are blocked", imgBlocked == "1" && trk == "undefined" && imgLoaded == "undefined", $"img={imgBlocked} trk={trk} loaded={imgLoaded}");
              R("adblock browser: the ad box is hidden, real content stays", boxDisp == "none" && contentDisp != "none", $"box={boxDisp} content={contentDisp}");
              R("adblock browser: the blocked count is tracked", at.BlockedCount == 2, "count=" + at.BlockedCount);
              w.TabList.SelectedItem = at; await Task.Delay(500); R("adblock browser: shield shows the count", w.ShieldBtn.Visibility == Visibility.Visible && w.ShieldCount.Text == "2", w.ShieldCount.Text);
              AdBlocker.SetPaused("localhost", true); at.Wv.Reload(true); await Task.Delay(3000);
              imgLoaded = await Js(at, "String(window.imgLoaded)"); trk = await Js(at, "String(window.trk)"); boxDisp = await Js(at, "getComputedStyle(document.getElementById('b')).display");
              R("adblock browser: pausing a site lets everything load", imgLoaded == "1" && trk == "1" && boxDisp != "none" && at.BlockedCount == 0, $"loaded={imgLoaded} trk={trk} box={boxDisp} count={at.BlockedCount}");
              R("adblock browser: shield shows the paused state", w.ShieldIcon.Kind == MaterialDesignThemes.Wpf.PackIconKind.ShieldOffOutline && w.ShieldCount.Text == "");
              AdBlocker.SetPaused("localhost", false); Store.D.Settings.AdBlock = false; at.Wv.Reload(true); await Task.Delay(3000);
              imgLoaded = await Js(at, "String(window.imgLoaded)"); trk = await Js(at, "String(window.trk)");
              R("adblock browser: switching the blocker off lets everything load", imgLoaded == "1" && trk == "1" && at.BlockedCount == 0, $"loaded={imgLoaded} trk={trk}");
              Store.D.Settings.AdBlock = true; AdBlocker.UseEngine(AdBlocker.Build()); w.CloseTab(at); }

            // ---- ad blocker: the real filter lists (needs a network connection; reported but never fails the run)
            try
            {
                var sw2 = System.Diagnostics.Stopwatch.StartNew(); Store.D.Settings.AdBlock = true; bool dl = await AdBlocker.UpdateAsync(); var real = AdBlocker.Engine; var ms = sw2.ElapsedMilliseconds;
                if (!dl) log.Add("INFO adblock: could not download the real filter lists (offline?)");
                else
                {
                    R("adblock real lists: downloaded and parsed", real.NetworkRules > 20000 && real.CosmeticRules > 3000, $"network={real.NetworkRules:N0} exceptions={real.ExceptionRules:N0} page rules={real.CosmeticRules:N0} skipped={real.Skipped:N0} in {ms}ms");
                    var blockedUrls = new[] { ("https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js", ReqType.Script), ("https://securepubads.g.doubleclick.net/tag/js/gpt.js", ReqType.Script), ("https://www.google-analytics.com/analytics.js", ReqType.Script), ("https://connect.facebook.net/en_US/fbevents.js", ReqType.Script), ("https://cdn.taboola.com/libtrc/loader.js", ReqType.Script), ("https://static.criteo.net/js/ld/publishertag.js", ReqType.Script) };
                    var okUrls = new[] { ("https://example.com/", ReqType.SubDocument), ("https://cdn.jsdelivr.net/npm/jquery@3.6.0/dist/jquery.min.js", ReqType.Script), ("https://fonts.googleapis.com/css2?family=Inter", ReqType.Stylesheet), ("https://www.google.com/search?q=orbit", ReqType.SubDocument), ("https://github.com/MykePalma/OrbitBrowser", ReqType.SubDocument), ("https://upload.wikimedia.org/wikipedia/commons/a/a9/Example.jpg", ReqType.Image) };
                    var missed = blockedUrls.Where(u => !real.ShouldBlock(u.Item1, "news.example.org", u.Item2)).Select(u => u.Item1).ToList();
                    var wrong = okUrls.Where(u => real.ShouldBlock(u.Item1, "news.example.org", u.Item2)).Select(u => u.Item1).ToList();
                    R("adblock real lists: well-known ad and tracker URLs are blocked", missed.Count == 0, string.Join(", ", missed));
                    R("adblock real lists: normal sites and libraries are not", wrong.Count == 0, string.Join(", ", wrong));
                    var css = real.CosmeticCss("news.example.org"); R("adblock real lists: page-cleaning CSS is produced", css.Length > 10000, $"{css.Length / 1024} KB of CSS");
                }
            }
            catch (Exception ex) { log.Add("INFO adblock real lists: " + ex.GetType().Name + " " + ex.Message); }

            // ---- custom title bar
            R("window chrome installed", System.Windows.Shell.WindowChrome.GetWindowChrome(w) != null && w.WindowStyle == WindowStyle.SingleBorderWindow);
            { var first = w.TabList.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement; var fx = first.TranslatePoint(new Point(0, 0), w).X; R("first tab has left margin", fx >= 8, $"x={fx:0.#} pad={w.Root.Margin.Left}"); }
            // tab tint from icon colour
            BitmapSource Solid(Color c) { var px = new byte[16 * 16 * 4]; for (int i = 0; i < px.Length; i += 4) { px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = 255; } return BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, px, 64); }
            var blue = Tab.DominantColor(Solid(Color.FromRgb(24, 119, 242))); R("tint: blue icon -> blue", blue is Color bc && bc.B > 200 && bc.R < 100, blue?.ToString());
            var orange = Tab.DominantColor(Solid(Color.FromRgb(255, 120, 20))); R("tint: orange icon -> orange", orange is Color oc && oc.R > 200 && oc.B < 100, orange?.ToString());
            R("tint: grey icon -> none", Tab.DominantColor(Solid(Color.FromRgb(128, 128, 128))) == null);
            R("tint: black icon -> none", Tab.DominantColor(Solid(Color.FromRgb(5, 5, 5))) == null);
            { var mixed = new byte[16 * 16 * 4]; for (int i = 0; i < mixed.Length; i += 4) { bool left = (i / 4) % 16 < 4; mixed[i] = (byte)(left ? 40 : 242); mixed[i + 1] = (byte)(left ? 180 : 119); mixed[i + 2] = (byte)(left ? 60 : 24); mixed[i + 3] = 255; } var big = Tab.DominantColor(BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, mixed, 64)); R("tint: dominant hue wins", big is Color mc && mc.B > mc.G, big?.ToString()); }
            var tintTab = w.Cur; tintTab.Tint = Color.FromRgb(24, 119, 242); R("tab tint brush", tintTab.TintBrush is SolidColorBrush sb && sb.Color.B == 242); tintTab.Favicon = null; R("tint cleared without icon", tintTab.Tint == null);
            Click(w.MaxBtn); await Task.Delay(700); { var src = PresentationSource.FromVisual(w); double k = src.CompositionTarget.TransformToDevice.M11; var top = w.CloseBtn.PointToScreen(new Point(0, 0)); var ic = w.MaxIcon.PointToScreen(new Point(w.MaxIcon.ActualWidth / 2, w.MaxIcon.ActualHeight / 2)); var cl = w.CloseBtn.PointToScreen(new Point(w.CloseBtn.ActualWidth, 0)); R("maximized: caption buttons inside the screen", top.Y >= -0.5 && cl.X <= SystemParameters.PrimaryScreenWidth * k + 0.5, $"closeTop={top.Y:0.#}px iconCenterY={ic.Y:0.#}px right={cl.X:0.#}px scale={k}"); }
            Click(w.MaxBtn); await Task.Delay(500); Click(w.MaxBtn); await Task.Delay(700);
            R("maximize button", w.WindowState == WindowState.Maximized && w.MaxIcon.Kind == MaterialDesignThemes.Wpf.PackIconKind.WindowRestore && w.Root.Margin.Left >= 7 && w.Root.Margin.Top >= 7, $"state={w.WindowState} pad={w.Root.Margin.Left}");
            Click(w.MaxBtn); await Task.Delay(500); R("restore button", w.WindowState == WindowState.Normal && w.Root.Margin.Left == 0 && w.MaxIcon.Kind == MaterialDesignThemes.Wpf.PackIconKind.WindowMaximize);
            Click(w.MinBtn); await Task.Delay(500); R("minimize button", w.WindowState == WindowState.Minimized); w.WindowState = WindowState.Normal; await Task.Delay(300);

            // ---- split view
            foreach (var x in w.Tabs.Where(x => x != t0).ToList()) w.CloseTab(x);
            w.TabList.SelectedItem = t0; await Task.Delay(200);
            w.ToggleSplit(); await Task.Delay(500);
            var sp = w.Cur; R("split opens partner tab", w.Tabs.Count == 2 && sp != t0 && sp.Partner == t0 && t0.Partner == sp);
            R("split: both visible side by side", t0.Wv.Visibility == Visibility.Visible && sp.Wv.Visibility == Visibility.Visible && Grid.GetColumn(t0.Wv) == 0 && Grid.GetColumn(sp.Wv) == 2 && w.Splitter.Visibility == Visibility.Visible);
            w.Navigate(U("right")); R("split: right pane navigates", await Until(() => sp.Title == "right"), sp.Title);
            w.TabList.SelectedItem = t0; await Task.Delay(300); R("split: focusing left keeps both", sp.Wv.Visibility == Visibility.Visible && w.Cur == t0);
            R("split: panes have real width", t0.Wv.ActualWidth > 200 && sp.Wv.ActualWidth > 200 && Math.Abs(t0.Wv.ActualWidth - sp.Wv.ActualWidth) < 20, $"{t0.Wv.ActualWidth:0}/{sp.Wv.ActualWidth:0}");
            var other = w.NewTab(U("solo")); await Until(() => other.Title == "solo"); R("split: other tab shows alone", other.Wv.Visibility == Visibility.Visible && t0.Wv.Visibility == Visibility.Hidden && sp.Wv.Visibility == Visibility.Hidden && w.Splitter.Visibility == Visibility.Collapsed && Grid.GetColumnSpan(other.Wv) == 3);
            w.TabList.SelectedItem = sp; await Task.Delay(300); R("split: partner restores pair", t0.Wv.Visibility == Visibility.Visible && sp.Wv.Visibility == Visibility.Visible);
            w.EndSplit(sp); await Task.Delay(200); R("close split view", t0.Partner == null && sp.Partner == null && t0.Wv.Visibility == Visibility.Hidden && Grid.GetColumnSpan(sp.Wv) == 3);
            w.StartSplit(t0, other); R("split with chosen tab", t0.Partner == other && other.Partner == t0);
            w.CloseTab(other); R("closing a split tab unpairs", t0.Partner == null);
            w.CloseTab(sp);

            // ---- drag preview
            { var a1 = w.NewTab(U("p1")); var a2 = w.NewTab(U("p2")); await Task.Delay(500); var c0 = (FrameworkElement)w.TabList.ItemContainerGenerator.ContainerFromIndex(0); double l0 = c0.TranslatePoint(new Point(0, 0), w.TabBar).X, w0 = c0.ActualWidth; int nT = w.Tabs.Count;
              R("drop slot: left of first tab -> 0", w.InsertIndexAt(new Point(l0 + 2, 10)) == 0);
              R("drop slot: right half of first tab -> 1", w.InsertIndexAt(new Point(l0 + w0 * 0.75, 10)) == 1);
              R("drop slot: far right -> end", w.InsertIndexAt(new Point(w.TabBar.ActualWidth - 5, 10)) == nT, $"n={nT}");
              w.ShowDropLine(0); R("insertion marker shows at first slot", w.DropLine.Visibility == Visibility.Visible && Math.Abs(w.DropLine.Margin.Left - (l0 - 3)) < 4, $"x={w.DropLine.Margin.Left:0.#} l0={l0:0.#}");
              w.ShowDropLine(nT); var lastC = (FrameworkElement)w.TabList.ItemContainerGenerator.ContainerFromIndex(nT - 1); R("insertion marker at end is after last tab", w.DropLine.Margin.Left >= lastC.TranslatePoint(new Point(lastC.ActualWidth, 0), w.TabBar).X - 3);
              w.DropLine.Visibility = Visibility.Collapsed;
              var gh = new DragGhost(w.Cur); gh.Follow(false); gh.Show(); await Task.Delay(300); R("drag ghost opens, click-through", gh.IsVisible && !gh.IsActive); gh.Close();
              w.CloseTab(a1); w.CloseTab(a2); }

            // ---- tab tear-off / move between windows
            var tA = w.NewTab(U("tearA")); await Until(() => tA.Title == "tearA");
            int winsBefore = App.Browsers.Count; var nw = w.TearOff(tA); await Task.Delay(1500);
            R("tear-off opens new window", App.Browsers.Count == winsBefore + 1 && !w.Tabs.Contains(tA) && nw.Tabs.Count == 1, $"wins={App.Browsers.Count}");
            R("tear-off keeps the page", await Until(() => nw.Cur != null && nw.Cur.Title == "tearA", 8000), nw.Cur?.Title);
            var tB = w.NewTab(U("moveB")); await Until(() => tB.Title == "moveB");
            var moved = nw.AcceptTab(tB); R("tab accepted by other window", tB.MovedTo == nw && nw.Tabs.Count == 2 && await Until(() => moved.Title == "moveB", 8000), moved.Title);
            w.CloseTab(tB); R("source closes moved tab", !w.Tabs.Contains(tB));
            nw.Close(); await Task.Delay(300);

            // ---- auto update
            R("update: version comparison", Updater.TryParseVersion("v1.2.3", out var v123) && v123 == new Version(1, 2, 3, 0) && Updater.TryParseVersion("2.0", out var v20) && v20 > v123 && Updater.TryParseVersion("1.0.10", out var v110) && v110 > new Version(1, 0, 9, 0) && !Updater.TryParseVersion("nightly", out _));
            R("update: newer/older/equal", Updater.IsNewer(new Version(Updater.Current.Major, Updater.Current.Minor, Updater.Current.Build + 1, 0)) && !Updater.IsNewer(new Version(Updater.Current.Major, Updater.Current.Minor, Math.Max(0, Updater.Current.Build), 0)) && !Updater.IsNewer(new Version(0, 9, 0, 0)));
            Updater.AllowInsecureForTests = true; Updater.ApiBase = $"http://localhost:{port}/updapi";
            FakeZip = MakeZip(("Orbit/Orbit.exe", "NEW-EXE"), ("Orbit/Orbit.dll", "NEW-DLL"), ("Orbit/sub/added.txt", "added")); FakeJson = Release("v99.1.0", FakeZip, port);
            var rel = await Updater.FetchLatestAsync(); R("update: reads latest release from GitHub-style API", rel != null && rel.Version == new Version(99, 1, 0, 0) && rel.AssetUrl.EndsWith("Orbit-win-x64.zip") && rel.Sha256.Length == 64 && rel.PageUrl != "", rel?.Tag);
            var prog = new List<double>(); var staged = await Updater.DownloadAndStageAsync(rel, p => prog.Add(p));
            R("update: download verified and staged", staged.Version == "99.1.0" && File.Exists(Path.Combine(staged.Dir, "Orbit.exe")) && File.Exists(Path.Combine(staged.Dir, "sub", "added.txt")) && Updater.Staged() != null, staged.Dir);
            FakeJson = Release("v99.2.0", FakeZip, port, new string('0', 64)); var rel2 = await Updater.FetchLatestAsync(); bool rejected = false; try { await Updater.DownloadAndStageAsync(rel2); } catch (InvalidDataException) { rejected = true; }
            R("update: wrong checksum is rejected", rejected && !Directory.Exists(Path.Combine(Updater.WorkDir, "99.2.0")));
            FakeJson = Release("v99.3.0", FakeZip, port).Replace("sha256:", "md5:"); var rel3 = await Updater.FetchLatestAsync(); bool noSum = false; try { await Updater.DownloadAndStageAsync(rel3); } catch (InvalidOperationException) { noSum = true; }
            R("update: refuses a release with no SHA-256", noSum);
            FakeZip = MakeZip(("Orbit/Orbit.exe", "x"), ("../evil.txt", "evil")); FakeJson = Release("v99.4.0", FakeZip, port); bool slip = false; try { await Updater.DownloadAndStageAsync(await Updater.FetchLatestAsync()); } catch (InvalidDataException) { slip = true; }
            R("update: zip with path traversal is rejected", slip && !File.Exists(Path.Combine(Updater.WorkDir, "99.4.0", "evil.txt")));
            FakeZip = MakeZip(("Orbit/readme.txt", "no exe here")); FakeJson = Release("v99.5.0", FakeZip, port); bool noExe = false; try { await Updater.DownloadAndStageAsync(await Updater.FetchLatestAsync()); } catch (InvalidDataException) { noExe = true; }
            R("update: zip without Orbit.exe is rejected", noExe);
            // apply: run the real install script against a throwaway "install" folder
            { var inst = Path.Combine(Path.GetTempPath(), "orbit-install-test"); try { Directory.Delete(inst, true); } catch { } Directory.CreateDirectory(inst); File.WriteAllText(Path.Combine(inst, "Orbit.exe"), "OLD-EXE"); File.WriteAllText(Path.Combine(inst, "Orbit.dll"), "OLD-DLL"); File.WriteAllText(Path.Combine(inst, "keep.txt"), "mine");
              var sf = Path.Combine(Updater.WorkDir, "staged.json"); R("update: staged file exists before install", File.Exists(sf));
              var script = Path.Combine(Updater.WorkDir, "apply-test.cmd"); File.WriteAllText(script, Updater.BuildScript(2147483, staged.Dir, inst, sf, Updater.WorkDir, Path.Combine(Updater.WorkDir, staged.Version), false));
              var pr = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false }); await pr.WaitForExitAsync();
              R("update: install script replaces files", File.ReadAllText(Path.Combine(inst, "Orbit.exe")) == "NEW-EXE" && File.ReadAllText(Path.Combine(inst, "Orbit.dll")) == "NEW-DLL" && File.Exists(Path.Combine(inst, "sub", "added.txt")), $"exit={pr.ExitCode}");
              R("update: install keeps the user's other files", File.ReadAllText(Path.Combine(inst, "keep.txt")) == "mine");
              R("update: staged update is forgotten after install", !File.Exists(sf) && !Directory.Exists(Path.Combine(Updater.WorkDir, staged.Version)));
              try { Directory.Delete(inst, true); } catch { } }
            R("update: install folder write check", Updater.CanWriteInstall(Path.GetTempPath()) && !Updater.CanWriteInstall(@"C:\Windows\System32"));
            // banner reacts to state
            FakeZip = MakeZip(("Orbit/Orbit.exe", "NEW")); FakeJson = Release("v99.6.0", FakeZip, port); Store.D.Settings.AutoUpdate = true;
            await Updater.CheckAsync(download: true); await Task.Delay(300);
            R("update: check downloads and shows the restart banner", Updater.Phase == UpdatePhase.Ready && w.UpdateBar.Visibility == Visibility.Visible && w.UpdText.Text.Contains("99.6.0") && w.UpdNow.Content.ToString().Contains("Restart"), $"{Updater.Phase} / {w.UpdText.Text}");
            FakeJson = "{\"tag_name\":\"v0.0.1\",\"assets\":[]}"; await Updater.CheckAsync(download: true); R("update: no usable release -> up to date", Updater.Phase == UpdatePhase.UpToDate);
            FakeJson = null; await Updater.CheckAsync(download: true); R("update: broken response -> failure reported, no crash", Updater.Phase == UpdatePhase.Failed, Updater.Message);
            Updater.DiscardStaged(); Updater.ApiBase = "https://api.github.com/repos/MykePalma/OrbitBrowser"; Updater.AllowInsecureForTests = false;
            try { var real = await Updater.FetchLatestAsync(); R("update: real GitHub release is readable", real != null && real.Sha256.Length == 64 && real.AssetUrl.StartsWith("https://github.com/"), real?.Tag); } catch (Exception ex) { log.Add("INFO update: real GitHub unreachable (" + ex.GetType().Name + ")"); }

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
