using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CefSharp;

namespace Lite;

static class K
{
    public static Brush B(string k) => (Brush)Application.Current.FindResource(k);
    public static Window Base(string title, double w, double h, Window owner = null) => new Window
    {
        Title = title, Width = w, Height = h, Background = B("Bg"), Foreground = B("Fg"), Owner = owner,
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen, FontSize = 14
    };
    public static Button Btn(string text, RoutedEventHandler click, double w = double.NaN)
    {
        var b = new Button { Content = text, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4), Background = B("Bar"), Foreground = B("Fg"), BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Width = w };
        b.Click += click; return b;
    }
    public static TextBox Box(string text = "") => new TextBox { Text = text, Padding = new Thickness(8, 6, 8, 6), Background = B("Field"), Foreground = B("Fg"), BorderBrush = B("Bar"), CaretBrush = B("Fg"), Margin = new Thickness(4) };
    public static CheckBox Check(string t, bool v) => new CheckBox { Content = t, IsChecked = v, Foreground = B("Fg"), Margin = new Thickness(4, 8, 4, 8) };
}

public class AuthWindow : Window
{
    public string User => u.Text; public string Pass => p.Password;
    readonly TextBox u = K.Box(); readonly PasswordBox p = new() { Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(4), Background = K.B("Field"), Foreground = K.B("Fg") };
    public AuthWindow(string site)
    {
        Title = "Sign in"; Width = 360; Height = 230; Background = K.B("Bg"); Foreground = K.B("Fg"); WindowStartupLocation = WindowStartupLocation.CenterScreen; ResizeMode = ResizeMode.NoResize;
        var s = new StackPanel { Margin = new Thickness(14) };
        s.Children.Add(new TextBlock { Text = site, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 8) });
        s.Children.Add(new TextBlock { Text = "Username" }); s.Children.Add(u);
        s.Children.Add(new TextBlock { Text = "Password" }); s.Children.Add(p);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = K.Btn("Sign in", (_, _) => DialogResult = true); ok.IsDefault = true;
        row.Children.Add(ok); row.Children.Add(K.Btn("Cancel", (_, _) => DialogResult = false) is var c ? c : null);
        s.Children.Add(row); Content = s; Loaded += (_, _) => u.Focus();
    }
}

public class DownloadsWindow : Window
{
    public static readonly ObservableCollection<DownloadEntry> Items = new();
    static readonly Dictionary<int, DownloadEntry> map = new();
    public static void Add(int id, string url, string path)
    {
        var e = new DownloadEntry { Id = id, Url = url, Path = path, Name = Path.GetFileName(path) };
        map[id] = e; Items.Insert(0, e);
    }
    public static void Update(int id, long rec, long tot, int pct, bool done, bool cancel, string path, IDownloadItemCallback cb)
    {
        if (!map.TryGetValue(id, out var e)) return;
        e.Callback = cb; e.Received = rec; e.Total = tot; e.Percent = pct; e.Done = done; e.Canceled = cancel;
        if (!string.IsNullOrEmpty(path)) { e.Path = path; e.Name = Path.GetFileName(path); }
    }

    readonly ListView lv = new() { ItemsSource = Items, Background = K.B("Bg"), Foreground = K.B("Fg"), BorderThickness = new Thickness(0) };
    public DownloadsWindow(Window owner)
    {
        Title = "Downloads"; Width = 620; Height = 440; Background = K.B("Bg"); Foreground = K.B("Fg"); Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var dt = new DataTemplate();
        var f = new FrameworkElementFactory(typeof(StackPanel)); f.SetValue(MarginProperty, new Thickness(2, 4, 2, 4));
        var n = new FrameworkElementFactory(typeof(TextBlock)); n.SetBinding(TextBlock.TextProperty, new Binding("Name")); n.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        var pb = new FrameworkElementFactory(typeof(ProgressBar)); pb.SetBinding(ProgressBar.ValueProperty, new Binding("Percent")); pb.SetValue(HeightProperty, 4.0); pb.SetValue(MarginProperty, new Thickness(0, 4, 0, 4));
        var st = new FrameworkElementFactory(typeof(TextBlock)); st.SetBinding(TextBlock.TextProperty, new Binding("Status")); st.SetValue(TextBlock.ForegroundProperty, K.B("Mut"));
        f.AppendChild(n); f.AppendChild(pb); f.AppendChild(st); dt.VisualTree = f; lv.ItemTemplate = dt;
        lv.MouseDoubleClick += (_, _) => Open();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
        bar.Children.Add(K.Btn("Open", (_, _) => Open()));
        bar.Children.Add(K.Btn("Show in folder", (_, _) => { if (lv.SelectedItem is DownloadEntry e && File.Exists(e.Path)) Process.Start("explorer.exe", "/select,\"" + e.Path + "\""); }));
        bar.Children.Add(K.Btn("Pause/Resume", (_, _) => { if (lv.SelectedItem is DownloadEntry e && !e.Done && e.Callback != null) { try { if (e.Paused) e.Callback.Resume(); else e.Callback.Pause(); e.Paused = !e.Paused; } catch { } } }));
        bar.Children.Add(K.Btn("Cancel", (_, _) => { if (lv.SelectedItem is DownloadEntry e && !e.Done) try { e.Callback?.Cancel(); } catch { } }));
        bar.Children.Add(K.Btn("Remove", (_, _) => { if (lv.SelectedItem is DownloadEntry e) { Items.Remove(e); map.Remove(e.Id); } }));
        bar.Children.Add(K.Btn("Clear all", (_, _) => { Items.Clear(); map.Clear(); }));
        var d = new DockPanel(); DockPanel.SetDock(bar, Dock.Top); d.Children.Add(bar); d.Children.Add(lv); Content = d;
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }
    void Open() { if (lv.SelectedItem is DownloadEntry e && e.Done && File.Exists(e.Path)) Process.Start(new ProcessStartInfo(e.Path) { UseShellExecute = true }); }
}

public class ListWindow : Window
{
    readonly bool bookmarks; readonly MainWindow main;
    readonly TextBox search = K.Box(); readonly ListView lv = new() { Background = K.B("Bg"), Foreground = K.B("Fg"), BorderThickness = new Thickness(0) };
    public ListWindow(MainWindow owner, bool bookmarks)
    {
        this.bookmarks = bookmarks; main = owner;
        Title = bookmarks ? "Bookmarks" : "History"; Width = 760; Height = 520; Background = K.B("Bg"); Foreground = K.B("Fg"); Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = "Title", Width = 320, DisplayMemberBinding = new Binding("Title") });
        gv.Columns.Add(new GridViewColumn { Header = "Address", Width = 290, DisplayMemberBinding = new Binding("Url") });
        if (!bookmarks) gv.Columns.Add(new GridViewColumn { Header = "Visited", Width = 120, DisplayMemberBinding = new Binding("When") });
        lv.View = gv;
        search.TextChanged += (_, _) => Fill();
        lv.MouseDoubleClick += (_, _) => OpenSel(true);
        lv.KeyDown += (_, e) => { if (e.Key == Key.Delete) DeleteSel(); if (e.Key == Key.Enter) OpenSel(true); };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
        bar.Children.Add(K.Btn("Open", (_, _) => OpenSel(true)));
        bar.Children.Add(K.Btn("Open in new tab", (_, _) => OpenSel(false)));
        bar.Children.Add(K.Btn("Delete", (_, _) => DeleteSel()));
        if (bookmarks)
        {
            bar.Children.Add(K.Btn("Export HTML", (_, _) => Export()));
            bar.Children.Add(K.Btn("Import HTML", (_, _) => Import()));
        }
        else bar.Children.Add(K.Btn("Clear all history", (_, _) => { if (MessageBox.Show("Clear all browsing history?", Title, MessageBoxButton.YesNo) == MessageBoxResult.Yes) { Store.D.History.Clear(); Store.Save(); Fill(); } }));
        var top = new StackPanel(); top.Children.Add(search); top.Children.Add(bar);
        var d = new DockPanel(); DockPanel.SetDock(top, Dock.Top); d.Children.Add(top); d.Children.Add(lv); Content = d;
        Loaded += (_, _) => { Fill(); search.Focus(); };
    }
    void Fill()
    {
        var f = search.Text.ToLowerInvariant();
        IEnumerable<object> src = bookmarks ? Store.D.Bookmarks : Store.D.History.OrderByDescending(h => h.Time);
        lv.ItemsSource = src.Where(o => o is Bookmark b ? (b.Title + b.Url).ToLowerInvariant().Contains(f) : o is HistoryItem h && (h.Title + h.Url).ToLowerInvariant().Contains(f)).Take(1000).ToList();
    }
    string Sel => lv.SelectedItem is Bookmark b ? b.Url : lv.SelectedItem is HistoryItem h ? h.Url : null;
    void OpenSel(bool current) { var u = Sel; if (u == null) return; if (current) main.Navigate(u); else main.NewTab(u, true); }
    void DeleteSel()
    {
        var items = lv.SelectedItems.Cast<object>().ToList();
        foreach (var o in items) { if (o is Bookmark b) Store.D.Bookmarks.Remove(b); else if (o is HistoryItem h) Store.D.History.Remove(h); }
        Store.Save(); Fill(); main.RefreshBookmarks();
    }
    void Export()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "HTML|*.html", FileName = "bookmarks.html" };
        if (dlg.ShowDialog() != true) return;
        var s = "<!DOCTYPE NETSCAPE-Bookmark-file-1>\n<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">\n<TITLE>Bookmarks</TITLE><H1>Bookmarks</H1>\n<DL><p>\n" +
            string.Concat(Store.D.Bookmarks.Select(b => $"<DT><A HREF=\"{System.Net.WebUtility.HtmlEncode(b.Url)}\">{System.Net.WebUtility.HtmlEncode(b.Title)}</A>\n")) + "</DL><p>";
        File.WriteAllText(dlg.FileName, s);
    }
    void Import()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "HTML|*.html;*.htm" };
        if (dlg.ShowDialog() != true) return;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(dlg.FileName), "<A[^>]*HREF=\"([^\"]+)\"[^>]*>(.*?)</A>", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var u = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
            if (u.StartsWith("http") && !Store.D.Bookmarks.Any(b => b.Url == u)) Store.D.Bookmarks.Add(new Bookmark { Url = u, Title = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value) });
        }
        Store.Save(); Fill(); main.RefreshBookmarks();
    }
}

public class SettingsWindow : Window
{
    public SettingsWindow(MainWindow owner)
    {
        var s = Store.D.Settings;
        Title = "Settings"; Width = 520; Height = 560; Background = K.B("Bg"); Foreground = K.B("Fg"); Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; FontSize = 14;
        var p = new StackPanel { Margin = new Thickness(16) };
        TextBlock H(string t) => new() { Text = t, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 14, 4, 2), Foreground = K.B("Acc") };
        p.Children.Add(H("Search engine"));
        var engines = new (string, string)[] { ("DuckDuckGo", "https://duckduckgo.com/?q=%s"), ("Google", "https://www.google.com/search?q=%s"), ("Bing", "https://www.bing.com/search?q=%s"), ("Brave", "https://search.brave.com/search?q=%s"), ("Startpage", "https://www.startpage.com/do/search?q=%s"), ("Ecosia", "https://www.ecosia.org/search?q=%s") };
        var cb = new ComboBox { Margin = new Thickness(4), IsEditable = true };
        foreach (var e in engines) cb.Items.Add(new ComboBoxItem { Content = e.Item1, Tag = e.Item2 });
        var cur = engines.FirstOrDefault(e => e.Item2 == s.Engine); cb.Text = cur.Item1 ?? s.Engine;
        cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is ComboBoxItem it) { s.Engine = (string)it.Tag; Store.Save(); } };
        cb.LostFocus += (_, _) => { if (cb.Text.Contains("%s")) { s.Engine = cb.Text; Store.Save(); } };
        p.Children.Add(cb);
        p.Children.Add(new TextBlock { Text = "Custom engines: type a URL containing %s", Foreground = K.B("Mut"), FontSize = 12, Margin = new Thickness(4, 0, 4, 0) });
        p.Children.Add(H("Home page"));
        var home = K.Box(s.Home); home.LostFocus += (_, _) => { s.Home = string.IsNullOrWhiteSpace(home.Text) ? Store.NewTab : home.Text.Trim(); Store.Save(); }; p.Children.Add(home);
        p.Children.Add(H("On startup"));
        var rs = K.Check("Restore tabs from last session", s.Restore); rs.Click += (_, _) => { s.Restore = rs.IsChecked == true; Store.Save(); }; p.Children.Add(rs);
        p.Children.Add(H("Appearance & privacy"));
        var bb = K.Check("Show bookmarks bar", s.BookmarksBar); bb.Click += (_, _) => { s.BookmarksBar = bb.IsChecked == true; Store.Save(); owner.RefreshBookmarks(); }; p.Children.Add(bb);
        var ab = K.Check("Block ads and trackers", s.AdBlock); ab.Click += (_, _) => { s.AdBlock = ab.IsChecked == true; Store.Save(); }; p.Children.Add(ab);
        var dk = K.Check("Force dark mode on web pages (restart required)", s.DarkPages); dk.Click += (_, _) => { s.DarkPages = dk.IsChecked == true; Store.Save(); }; p.Children.Add(dk);
        p.Children.Add(H("Downloads"));
        var dir = K.Box(s.DownloadDir); dir.LostFocus += (_, _) => { s.DownloadDir = dir.Text; Store.Save(); };
        var row = new DockPanel(); var br = K.Btn("Browse…", (_, _) => { var d = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = s.DownloadDir }; if (d.ShowDialog() == true) { dir.Text = d.FolderName; s.DownloadDir = d.FolderName; Store.Save(); } });
        DockPanel.SetDock(br, Dock.Right); row.Children.Add(br); row.Children.Add(dir); p.Children.Add(row);
        var ask = K.Check("Ask where to save each file", s.AskDownload); ask.Click += (_, _) => { s.AskDownload = ask.IsChecked == true; Store.Save(); }; p.Children.Add(ask);
        p.Children.Add(H("Browsing data"));
        var r2 = new WrapPanel();
        r2.Children.Add(K.Btn("Clear history", (_, _) => { Store.D.History.Clear(); Store.Save(); }));
        r2.Children.Add(K.Btn("Clear cookies", async (_, _) => { await Cef.GetGlobalCookieManager().DeleteCookiesAsync("", ""); }));
        r2.Children.Add(K.Btn("Clear cache", async (_, _) => { foreach (var w in App.Windows) await w.ClearCache(); }));
        p.Children.Add(r2);
        Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}

public class PromptWindow : Window
{
    readonly TextBox box;
    public string Value => box.Text;
    public PromptWindow(string title, string msg, string def)
    {
        Title = title; Width = 400; Height = 200; Background = K.B("Bg"); Foreground = K.B("Fg"); WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var s = new StackPanel { Margin = new Thickness(14) };
        s.Children.Add(new TextBlock { Text = msg, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 8) });
        box = K.Box(def); s.Children.Add(box);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = K.Btn("OK", (_, _) => DialogResult = true); ok.IsDefault = true; row.Children.Add(ok);
        var c = K.Btn("Cancel", (_, _) => DialogResult = false); c.IsCancel = true; row.Children.Add(c);
        s.Children.Add(row); Content = s; Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
    }
}
