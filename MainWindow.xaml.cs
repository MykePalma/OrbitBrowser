using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CefSharp;
using CefSharp.Wpf.HwndHost;

namespace Lite;

public class Sugg { public string Display { get; set; } public string Url { get; set; } public override string ToString() => Display; }

public partial class MainWindow : Window
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    public readonly bool PrivateWin;
    public readonly ObservableCollection<Tab> Tabs = new();
    public Tab Cur => TabList.SelectedItem as Tab;
    RequestContext privCtx;
    readonly List<string> closed = new();
    DownloadsWindow dlWin;
    static bool restored;
    Point dragStart; bool suppressUrl;

    public MainWindow(bool priv, string startUrl = null)
    {
        InitializeComponent();
        PrivateWin = priv;
        App.Browsers.Add(this);
        if (priv) { Title = "Orbit (Private)"; TabBar.Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x1B, 0x3F)); }
        TabList.ItemsSource = Tabs;
        Tabs.CollectionChanged += (_, _) => RecalcTabs();
        TabBar.AllowDrop = true; TabBar.Drop += TabList_Drop;
        TabBar.DragOver += TabDragOver; TabBar.DragLeave += (_, _) => DropLine.Visibility = Visibility.Collapsed;
        TabList.GiveFeedback += (_, e) =>
        {
            e.UseDefaultCursors = false; Mouse.SetCursor(Cursors.Arrow);
            ghost?.Follow(e.Effects != DragDropEffects.None);        // no valid target under the mouse -> dropping tears the tab off
            e.Handled = true;
        };
        StateChanged += (_, _) => UpdateWindowState();
        Updater.Changed += OnUpdateChanged; Closed += (_, _) => Updater.Changed -= OnUpdateChanged;
        OnUpdateChanged();
        TabBar.SizeChanged += (_, _) => RecalcTabs();
        RefreshBookmarks();
        PreviewKeyDown += Win_KeyDown;
        AllowDrop = true;
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] fs) foreach (var f in fs) NewTab(new Uri(f).AbsoluteUri);
            else if (e.Data.GetData(DataFormats.Text) is string s && s.Trim() != "") NewTab(s.Trim());
        };
        PreviewMouseWheel += (_, e) => { if (Keyboard.Modifiers == ModifierKeys.Control) { Zoom(e.Delta > 0 ? 0.5 : -0.5); e.Handled = true; } };
        PreviewMouseDown += (_, e) => { if (e.ChangedButton == MouseButton.XButton1) { Back_Click(null, null); e.Handled = true; } else if (e.ChangedButton == MouseButton.XButton2) { Fwd_Click(null, null); e.Handled = true; } };
        Closing += (_, _) => SaveSession();
        Closed += (_, _) => { App.Browsers.Remove(this); foreach (var t in Tabs.ToList()) { try { t.Browser?.GetBrowserHost()?.CloseDevTools(); } catch { } t.Browser?.Dispose(); } };
        SourceInitialized += (_, _) => { try { var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle; int on = 1; DwmSetWindowAttribute(hwnd, 20, ref on, 4); } catch { } };
        Loaded += (_, _) =>
        {
            bool first = !restored; restored = true;
            bool restoring = !priv && first && Store.D.Settings.Restore && Store.D.Session.Count > 0;
            if (restoring) { foreach (var u in Store.D.Session.ToList()) NewTab(u, false); TabList.SelectedIndex = Tabs.Count - 1; }
            if (startUrl != null || !restoring) NewTab(startUrl);   // a URL given on the command line opens in addition to the restored tabs
            if (App.SelfTest != null && !priv && first) _ = SelfTest.Run(this);
            if (first && !priv && Updater.ShouldAutoCheck()) _ = Task.Delay(4000).ContinueWith(_ => Updater.CheckAsync(download: true));
        };
    }

    // ---------------------------------------------------------------- tabs
    public Tab NewTab(string url, bool select = true, bool? priv = null, Tab afterTab = null, int index = -1)
    {
        var t = new Tab { Private = priv ?? PrivateWin };
        var b = new ChromiumWebBrowser { Visibility = Visibility.Hidden };
        if (t.Private) { privCtx ??= new RequestContext(new RequestContextSettings { CachePath = "", PersistSessionCookies = false }); b.RequestContext = privCtx; }
        t.Browser = b;
        b.DisplayHandler = new TabDisplay(t, this); b.LifeSpanHandler = new TabLifeSpan(t, this); b.DownloadHandler = new TabDownload(this);
        b.KeyboardHandler = new TabKeyboard(this); b.MenuHandler = new TabContextMenu(t, this); b.PermissionHandler = new TabPermissions(t);
        b.FindHandler = new TabFind(this); b.FocusHandler = new TabFocus(t, this); b.JsDialogHandler = new TabJsDialog(this); b.RequestHandler = new TabRequest();
        b.LoadingStateChanged += (_, e) => Dispatcher.BeginInvoke(() =>
        {
            t.Loading = e.IsLoading; t.CanBack = e.CanGoBack; t.CanFwd = e.CanGoForward;
            if (!e.IsLoading) { AddHistory(t); if (t == Cur) Prog.Visibility = Visibility.Hidden; }
            if (t == Cur) UpdateToolbar();
        });
        b.Address = Store.Resolve(url ?? Store.D.Settings.Home); t.Url = b.Address;
        Content_.Children.Add(b);
        int i = index >= 0 ? index : afterTab != null && Tabs.Contains(afterTab) ? Tabs.IndexOf(afterTab) + 1 : Tabs.Count;
        Tabs.Insert(i, t);
        if (select) { TabList.SelectedItem = t; if (string.IsNullOrEmpty(url) || url == Store.NewTab) { } }
        LayoutPanes();
        return t;
    }

    void ShowTab(Tab t)
    {
        LayoutPanes();
        Prog.Visibility = Visibility.Hidden; Status.Visibility = Visibility.Collapsed;
        UpdateToolbar();
        Title = t.Title + " - Orbit" + (PrivateWin ? " (Private)" : "");
        if (string.IsNullOrEmpty(Store.Display(t.Url))) { UrlBox.Focus(); } else t.Browser.Focus();
    }

    public void CloseTab(Tab t)
    {
        if (!Tabs.Contains(t)) return;
        if (!t.Private && t.Url.StartsWith("http")) closed.Add(t.Url);
        int i = Tabs.IndexOf(t);
        if (Tabs.Count == 1) { if (App.Browsers.Count > 1 || true) { Close(); return; } }
        bool wasCur = t == Cur;
        EndSplitOf(t);
        Tabs.Remove(t); Content_.Children.Remove(t.Browser); t.Browser.Dispose();
        if (wasCur) TabList.SelectedIndex = Math.Min(i, Tabs.Count - 1);
        LayoutPanes();
    }

    void RecalcTabs()
    {
        if (Tabs.Count == 0) return;
        double avail = TabBar.ActualWidth - CaptionBtns.ActualWidth - TabList.Margin.Left - NewTabBtn.ActualWidth - NewTabBtn.Margin.Left - NewTabBtn.Margin.Right - 12;
        int pinned = Tabs.Count(t => t.Pinned), n = Tabs.Count - pinned;
        if (n == 0) return;
        double w = Math.Clamp((avail - pinned * 46) / n - 2, 44, 220);
        foreach (var t in Tabs.Where(x => !x.Pinned)) t.TabWidth = w;
    }

    void TabList_SelectionChanged(object s, SelectionChangedEventArgs e) { if (Cur != null) ShowTab(Cur); }
    void TabItem_MouseDown(object s, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Middle && (s as FrameworkElement)?.DataContext is Tab t) { CloseTab(t); e.Handled = true; } }
    void TabClose_Click(object s, RoutedEventArgs e) { if ((s as FrameworkElement)?.DataContext is Tab t) CloseTab(t); e.Handled = true; }
    void TabMute_Click(object s, RoutedEventArgs e) { if ((s as FrameworkElement)?.DataContext is Tab t) ToggleMute(t); e.Handled = true; }
    void NewTab_Click(object s, RoutedEventArgs e) => NewTab(null);

    public void ToggleMute(Tab t) { t.Muted = !t.Muted; try { t.Browser.GetBrowserHost()?.SetAudioMuted(t.Muted); } catch { } }
    public void TogglePin(Tab t)
    {
        t.Pinned = !t.Pinned;
        int pinnedCount = Tabs.Count(x => x.Pinned && x != t);
        int from = Tabs.IndexOf(t), to = t.Pinned ? pinnedCount : pinnedCount;
        if (from != to) Tabs.Move(from, Math.Min(to, Tabs.Count - 1));
        TabList.SelectedItem = t; RecalcTabs();
    }
    public void Duplicate(Tab t) => NewTab(t.Url, true, t.Private, t);
    public void ReopenClosed() { if (closed.Count > 0) { var u = closed[^1]; closed.RemoveAt(closed.Count - 1); NewTab(u); } }

    void TabItem_RightClick(object s, MouseButtonEventArgs e)
    {
        if ((s as FrameworkElement)?.DataContext is not Tab t) return;
        var m = new ContextMenu();
        MenuItem I(string h, Action a) => Ic.Item(h, a);
        m.Items.Add(I("New tab", () => NewTab(null, true, null, t)));
        m.Items.Add(I("Reload", () => t.Browser.Reload()));
        m.Items.Add(I("Duplicate", () => Duplicate(t)));
        if (t.Partner != null) m.Items.Add(I("Close split view", () => EndSplit(t)));
        else
        {
            m.Items.Add(I("Open split view", () => OpenSplitFor(t)));
            if (Cur != null && Cur != t && Cur.Partner == null) m.Items.Add(I("Split with current tab", () => { var c = Cur; StartSplit(c, t); }));
        }
        if (Tabs.Count > 1) m.Items.Add(I("Open in new window", () => TearOff(t)));
        m.Items.Add(I(t.Pinned ? "Unpin tab" : "Pin tab", () => TogglePin(t)));
        m.Items.Add(I(t.Muted ? "Unmute site" : "Mute site", () => ToggleMute(t)));
        m.Items.Add(new Separator());
        m.Items.Add(I("Close tab", () => CloseTab(t)));
        m.Items.Add(I("Close other tabs", () => { foreach (var x in Tabs.Where(x => x != t).ToList()) CloseTab(x); }));
        m.Items.Add(I("Close tabs to the right", () => { foreach (var x in Tabs.Skip(Tabs.IndexOf(t) + 1).ToList()) CloseTab(x); }));
        m.Items.Add(I("Reopen closed tab", ReopenClosed));
        m.IsOpen = true; e.Handled = true;
    }

    void TabList_Down(object s, MouseButtonEventArgs e) => dragStart = e.GetPosition(null);
    void TabList_MouseMove(object s, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || Cur == null) return;
        var d = e.GetPosition(null) - dragStart;
        if (Math.Abs(d.X) < 8 && Math.Abs(d.Y) < 8) return;
        if (e.OriginalSource is DependencyObject o && FindTab(o) is Tab t)
        {
            var srcItem = TabList.ItemContainerGenerator.ContainerFromItem(t) as UIElement;
            if (srcItem != null) srcItem.Opacity = 0.4;
            ghost = new DragGhost(t); ghost.Follow(true); ghost.Show();
            DragDropEffects eff;
            try { eff = DragDrop.DoDragDrop(TabList, new DataObject(typeof(Tab), t), DragDropEffects.Move); }
            finally { ghost.Close(); ghost = null; HideDropLines(); if (srcItem != null) srcItem.Opacity = 1; }
            if (t.MovedTo != null) { t.MovedTo = null; CloseTab(t); }                  // another window took the tab
            else if (eff == DragDropEffects.None && Tabs.Count > 1 && Tabs.Contains(t)) TearOff(t); // dropped outside any tab strip
        }
    }

    DragGhost ghost;

    /// <summary>Slot (0..count) a tab dropped at this tab-bar position would take: before the first tab whose middle is to the right of the mouse.</summary>
    public int InsertIndexAt(Point p)
    {
        for (int i = 0; i < Tabs.Count; i++)
            if (TabList.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement c && p.X < c.TranslatePoint(new Point(0, 0), TabBar).X + c.ActualWidth / 2) return i;
        return Tabs.Count;
    }
    /// <summary>Shows the accent insertion marker at the edge of slot <paramref name="index"/>.</summary>
    public void ShowDropLine(int index)
    {
        double x;
        if (Tabs.Count == 0) x = TabList.Margin.Left;
        else if (index < Tabs.Count && TabList.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement c) x = c.TranslatePoint(new Point(0, 0), TabBar).X - 2;
        else { var last = (FrameworkElement)TabList.ItemContainerGenerator.ContainerFromIndex(Tabs.Count - 1); x = last.TranslatePoint(new Point(last.ActualWidth, 0), TabBar).X; }
        DropLine.Margin = new Thickness(Math.Max(0, x - 1), 7, 0, 0); DropLine.Visibility = Visibility.Visible;
    }
    void HideDropLines() { foreach (var w in App.Browsers) w.DropLine.Visibility = Visibility.Collapsed; }
    void TabDragOver(object s, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(Tab))) return;
        e.Effects = DragDropEffects.Move; e.Handled = true;
        ShowDropLine(InsertIndexAt(e.GetPosition(TabBar)));
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);

    /// <summary>Moves a tab into a brand-new window at the mouse position.</summary>
    public MainWindow TearOff(Tab t)
    {
        GetCursorPos(out var p); var dpi = VisualTreeHelper.GetDpi(this);
        var size = WindowState == WindowState.Normal ? new Size(Width, Height) : RestoreBounds.Size;
        var w = new MainWindow(t.Private, t.Url) { WindowStartupLocation = WindowStartupLocation.Manual, Left = p.X / dpi.DpiScaleX - 120, Top = p.Y / dpi.DpiScaleY - 18, Width = size.Width, Height = size.Height };
        w.Show(); CloseTab(t);
        return w;
    }
    /// <summary>Takes over a tab dragged in from another window (the source window closes its copy).</summary>
    public Tab AcceptTab(Tab src, int index = -1) { src.MovedTo = this; return NewTab(src.Url, true, src.Private, null, index); }
    void TabList_Drop(object s, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Tab)) is not Tab src) return;
        e.Handled = true;
        DropLine.Visibility = Visibility.Collapsed;
        int at = InsertIndexAt(e.GetPosition(TabBar));
        if (!Tabs.Contains(src)) { AcceptTab(src, at); e.Effects = DragDropEffects.Move; return; }
        int from = Tabs.IndexOf(src), to = at > from ? at - 1 : at;      // removing the tab first shifts later slots left by one
        if (from >= 0 && to >= 0 && from != to) { Tabs.Move(from, to); TabList.SelectedItem = src; }
    }
    static Tab FindTab(DependencyObject o) { while (o != null) { if (o is FrameworkElement f && f.DataContext is Tab t && f is ListBoxItem) return t; o = VisualTreeHelper.GetParent(o); } return null; }

    // ---------------------------------------------------------------- split view
    /// <summary>Shows the selected tab and its split partner side by side; every other tab is hidden.</summary>
    void LayoutPanes()
    {
        var cur = Cur; var p = cur?.Partner;
        bool split = cur != null && p != null && Tabs.Contains(p);
        Splitter.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        foreach (var x in Tabs)
        {
            bool show = x == cur || (split && x == p);
            x.Browser.Visibility = show ? Visibility.Visible : Visibility.Hidden;
            if (split && show) { Grid.SetColumn(x.Browser, x.SplitLeft ? 0 : 2); Grid.SetColumnSpan(x.Browser, 1); }
            else { Grid.SetColumn(x.Browser, 0); Grid.SetColumnSpan(x.Browser, 3); }
        }
    }
    public void StartSplit(Tab a, Tab b)
    {
        if (a == b || !Tabs.Contains(a) || !Tabs.Contains(b)) return;
        EndSplitOf(a); EndSplitOf(b);
        a.Partner = b; b.Partner = a;
        a.SplitLeft = Tabs.IndexOf(a) < Tabs.IndexOf(b); b.SplitLeft = !a.SplitLeft;
        Content_.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star); Content_.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
        TabList.SelectedItem = a; LayoutPanes();
    }
    void EndSplitOf(Tab a) { var p = a.Partner; a.Partner = null; if (p != null) p.Partner = null; }
    public void EndSplit(Tab a) { EndSplitOf(a); LayoutPanes(); }
    /// <summary>Opens a fresh tab next to the given tab in split view and moves focus to it.</summary>
    public void OpenSplitFor(Tab c) { var n = NewTab(null, false, null, c); StartSplit(c, n); TabList.SelectedItem = n; }
    public void ToggleSplit() { var c = Cur; if (c == null) return; if (c.Partner != null) EndSplit(c); else OpenSplitFor(c); }
    public void OnPaneFocus(Tab t) { if (Cur != null && Cur.Partner == t && Tabs.Contains(t)) TabList.SelectedItem = t; }

    // ---------------------------------------------------------------- updates
    bool updateDismissed;
    void OnUpdateChanged()
    {
        var p = Updater.Phase; var v = Updater.Latest?.Version.ToString(3) ?? "";
        bool show = !updateDismissed && !full && (p == UpdatePhase.Ready || p == UpdatePhase.Available || p == UpdatePhase.Downloading);
        UpdateBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        UpdText.Text = p switch
        {
            UpdatePhase.Ready => $"Orbit {v} has been downloaded. Restart to finish updating.",
            UpdatePhase.Downloading => Updater.Message,
            UpdatePhase.Available => Updater.CanWriteInstall() ? $"Orbit {v} is available." : $"Orbit {v} is available. This copy can't update itself in its current folder.",
            _ => ""
        };
        UpdNow.Visibility = p == UpdatePhase.Downloading ? Visibility.Collapsed : Visibility.Visible;
        UpdNow.Content = p == UpdatePhase.Ready ? "Restart to update" : Updater.CanWriteInstall() ? "Update now" : "Download";
        UpdNotes.Visibility = Updater.Latest?.PageUrl is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    }
    async void UpdNow_Click(object s, RoutedEventArgs e)
    {
        if (Updater.Phase == UpdatePhase.Ready) { Updater.InstallAndRestart(); return; }
        if (Updater.Phase == UpdatePhase.Available && Updater.Latest != null)
        {
            if (!Updater.CanWriteInstall()) { NewTab(Updater.Latest.PageUrl); return; }
            await Updater.CheckAsync(download: true);                       // downloads and verifies it
            if (Updater.Phase == UpdatePhase.Ready) Updater.InstallAndRestart();
        }
    }
    void UpdNotes_Click(object s, RoutedEventArgs e) { if (Updater.Latest?.PageUrl is { Length: > 0 } u) NewTab(u); }
    void UpdLater_Click(object s, RoutedEventArgs e) { updateDismissed = true; OnUpdateChanged(); }

    // ---------------------------------------------------------------- custom title bar
    void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void CloseWin_Click(object s, RoutedEventArgs e) => Close();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    /// <summary>How far a maximized window with a custom frame extends past the screen edge on each side (SM_CXSIZEFRAME + SM_CXPADDEDBORDER), in device-independent units.</summary>
    Thickness MaximizedOverhang()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double x = (GetSystemMetrics(32) + GetSystemMetrics(92)) / dpi.DpiScaleX, y = (GetSystemMetrics(33) + GetSystemMetrics(92)) / dpi.DpiScaleY;
        return new Thickness(x, y, x, y);
    }
    void UpdateWindowState()
    {
        bool max = WindowState == WindowState.Maximized;
        MaxIcon.Kind = max ? MaterialDesignThemes.Wpf.PackIconKind.WindowRestore : MaterialDesignThemes.Wpf.PackIconKind.WindowMaximize;
        MaxBtn.ToolTip = max ? "Restore" : "Maximize";
        // a maximized chrome-less window overhangs the screen by the resize border
        Root.Margin = max && !full ? MaximizedOverhang() : new Thickness(0);   // (Window.Padding is not applied by the default template)
    }

    // ---------------------------------------------------------------- toolbar
    public void UpdateToolbar()
    {
        var t = Cur; if (t == null) return;
        BackBtn.IsEnabled = t.CanBack; FwdBtn.IsEnabled = t.CanFwd;
        ReloadBtn.Content = t.Loading ? "✕" : "⟳"; ReloadBtn.ToolTip = t.Loading ? "Stop" : "Reload (F5)";
        var disp = Store.Display(t.Url);
        if (!UrlBox.IsKeyboardFocused) { suppressUrl = true; UrlBox.Text = disp; suppressUrl = false; }
        Lock.Text = t.Url.StartsWith("https:") ? "🔒" : t.Url.StartsWith("http:") ? "⚠" : "";
        bool bm = Store.D.Bookmarks.Any(b => b.Url == t.Url);
        StarBtn.Content = bm ? "★" : "☆"; StarBtn.Foreground = bm ? new SolidColorBrush(Color.FromRgb(0xF9, 0xAB, 0x00)) : (Brush)FindResource("Fg");
        UpdateZoomLabel();
    }
    void UpdateZoomLabel()
    {
        double z = 0; try { if (Cur?.Browser.IsBrowserInitialized == true) z = Cur.Browser.ZoomLevel; } catch { }
        int pct = (int)Math.Round(Math.Pow(1.2, z) * 100);
        ZoomLbl.Text = pct != 100 ? pct + "%" : "";
    }
    public void OnTitle(Tab t, string s) { t.Title = s; if (t == Cur) Title = t.Title + " - Orbit"; TouchHistoryTitle(t); }
    public void OnStatus(Tab t, string s) { if (t == Cur) { StatusText.Text = s; Status.Visibility = string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible; } }
    public void OnProgress(Tab t, double p) { if (t != Cur) return; Prog.Value = p; Prog.Visibility = p < 1 ? Visibility.Visible : Visibility.Hidden; }

    public void Navigate(string input)
    {
        if (Cur == null) { NewTab(input); return; }
        var u = Store.Resolve(input);
        if (Cur.Browser.IsBrowserInitialized) Cur.Browser.Load(u); else Cur.Browser.Address = u;
        Cur.Browser.Focus();
    }
    void Back_Click(object s, RoutedEventArgs e) { if (Cur?.CanBack == true) Cur.Browser.Back(); }
    void Fwd_Click(object s, RoutedEventArgs e) { if (Cur?.CanFwd == true) Cur.Browser.Forward(); }
    void Reload_Click(object s, RoutedEventArgs e) { if (Cur == null) return; if (Cur.Loading) Cur.Browser.Stop(); else Cur.Browser.Reload(); }
    void Home_Click(object s, RoutedEventArgs e) => Navigate(Store.D.Settings.Home);
    void Star_Click(object s, RoutedEventArgs e) => ToggleBookmark();
    void Downloads_Click(object s, RoutedEventArgs e) => ShowDownloads();
    void Zoom_Click(object s, MouseButtonEventArgs e) => Zoom(0, true);

    public void ShowDownloads() { dlWin ??= new DownloadsWindow(this); dlWin.Show(); dlWin.Activate(); }
    public void ShowHistory() { var w = Application.Current.Windows.OfType<HistoryWindow>().FirstOrDefault(); if (w != null) { w.Activate(); return; } new HistoryWindow(this).Show(); }
    public void ShowBookmarks() { var w = Application.Current.Windows.OfType<BookmarkManager>().FirstOrDefault(); if (w != null) { w.Activate(); return; } new BookmarkManager(this).Show(); }
    public void ShowSettings(string page = null) { var w = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault() ?? new SettingsWindow(this); w.Show(); w.Activate(); if (page != null) w.ShowPage(page); }

    void Menu_Click(object s, RoutedEventArgs e)
    {
        var m = new ContextMenu { PlacementTarget = MenuBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        MenuItem I(string h, Action a, string g = null) => Ic.Item(h, a, g);
        m.Items.Add(I("New tab", () => NewTab(null), "Ctrl+T"));
        m.Items.Add(I("New window", () => new MainWindow(false).Show(), "Ctrl+N"));
        m.Items.Add(I("New private window", () => OpenPrivateWindow(null), "Ctrl+Shift+N"));
        m.Items.Add(new Separator());
        m.Items.Add(I("Bookmarks", ShowBookmarks, "Ctrl+B"));
        m.Items.Add(I("History", ShowHistory, "Ctrl+H"));
        m.Items.Add(I("Downloads", ShowDownloads, "Ctrl+J"));
        m.Items.Add(new Separator());
        m.Items.Add(I("Zoom in", () => Zoom(0.5), "Ctrl++"));
        m.Items.Add(I("Zoom out", () => Zoom(-0.5), "Ctrl+-"));
        m.Items.Add(I("Reset zoom", () => Zoom(0, true), "Ctrl+0"));
        m.Items.Add(I("Full screen", ToggleFullscreen, "F11"));
        m.Items.Add(I(Cur?.Partner != null ? "Close split view" : "Open split view", ToggleSplit));
        m.Items.Add(new Separator());
        m.Items.Add(I("Find in page…", () => ShowFind(), "Ctrl+F"));
        m.Items.Add(I("Print…", Print, "Ctrl+P"));
        m.Items.Add(I("Save page as PDF…", SavePdf, "Ctrl+S"));
        m.Items.Add(I("View page source", ViewSource, "Ctrl+U"));
        m.Items.Add(I("Developer tools", DevTools, "F12"));
        m.Items.Add(new Separator());
        m.Items.Add(I("Settings", () => ShowSettings(), "Ctrl+,"));
        m.Items.Add(I("About Orbit", () => MessageBox.Show(this, "Orbit " + Updater.CurrentText + "\nChromium " + Cef.ChromiumVersion + " · CefSharp " + Cef.CefSharpVersion, "About")));
        m.Items.Add(I("Exit", () => Application.Current.Shutdown()));
        m.IsOpen = true;
    }

    // ---------------------------------------------------------------- address bar
    void Url_Click(object s, MouseButtonEventArgs e) { if (!UrlBox.IsKeyboardFocused) { UrlBox.Focus(); UrlBox.SelectAll(); e.Handled = true; } }
    void Url_Focus(object s, KeyboardFocusChangedEventArgs e) { Omni.BorderBrush = (Brush)FindResource("Acc"); UrlBox.SelectAll(); }
    void Url_Lost(object s, KeyboardFocusChangedEventArgs e) { Omni.BorderBrush = Brushes.Transparent; Dispatcher.BeginInvoke(() => { if (!SuggList.IsKeyboardFocusWithin) { SuggPopup.IsOpen = false; UpdateToolbar(); } }); }
    public void FocusUrl() { UrlBox.Focus(); UrlBox.SelectAll(); }

    void Url_Changed(object s, TextChangedEventArgs e)
    {
        if (suppressUrl || !UrlBox.IsKeyboardFocused) return;
        var q = UrlBox.Text.Trim(); if (q == "") { SuggPopup.IsOpen = false; return; }
        var lq = q.ToLowerInvariant();
        var list = new List<Sugg> { new() { Display = "🔍  " + q + " — search or go", Url = q } };
        var seen = new HashSet<string>();
        foreach (var b in Store.D.Bookmarks) if ((b.Title + b.Url).ToLowerInvariant().Contains(lq) && seen.Add(b.Url)) list.Add(new Sugg { Display = "★  " + b.Title + "  —  " + b.Url, Url = b.Url });
        foreach (var h in Store.D.History.AsEnumerable().Reverse()) { if (list.Count >= 8) break; if ((h.Title + h.Url).ToLowerInvariant().Contains(lq) && seen.Add(h.Url)) list.Add(new Sugg { Display = "◷  " + (h.Title == "" ? h.Url : h.Title) + "  —  " + h.Url, Url = h.Url }); }
        SuggList.ItemsSource = list; SuggList.SelectedIndex = -1; SuggPopup.IsOpen = true;
    }
    void Sugg_Click(object s, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject o) { while (o != null && o is not ListBoxItem) o = VisualTreeHelper.GetParent(o); if (o is ListBoxItem li && li.DataContext is Sugg sg) { SuggPopup.IsOpen = false; Navigate(sg.Url); } }
    }
    void Url_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Down || e.Key == Key.Up)
        {
            if (!SuggPopup.IsOpen || SuggList.Items.Count == 0) return;
            int n = SuggList.Items.Count, i = SuggList.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
            if (i >= n) i = -1; if (i < -1) i = n - 1;
            SuggList.SelectedIndex = i;
            if (i >= 0) { suppressUrl = true; UrlBox.Text = ((Sugg)SuggList.SelectedItem).Url; UrlBox.CaretIndex = UrlBox.Text.Length; suppressUrl = false; }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            var txt = UrlBox.Text;
            if (Keyboard.Modifiers == ModifierKeys.Control && !txt.Contains('.') && !txt.Contains(' ')) txt = $"www.{txt}.com";
            SuggPopup.IsOpen = false;
            if (Keyboard.Modifiers == ModifierKeys.Alt) NewTab(txt); else Navigate(txt);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SuggPopup.IsOpen = false; suppressUrl = true; UrlBox.Text = Store.Display(Cur?.Url); suppressUrl = false; Cur?.Browser.Focus(); e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- bookmarks
    public static void RefreshAll() { foreach (var w in App.Browsers.ToList()) { w.RefreshBookmarks(); w.UpdateToolbar(); } }

    public void ToggleBookmark()
    {
        var t = Cur; if (t == null || !t.Url.StartsWith("http")) return;
        var ex = Store.D.Bookmarks.FirstOrDefault(b => b.Url == t.Url);
        bool isNew = ex == null;
        if (isNew) { ex = new Bookmark { Url = t.Url, Title = t.Title, Folder = lastFolder, Icon = t.FaviconB64 }; Store.D.Bookmarks.Insert(0, ex); Store.Save(); RefreshAll(); }
        if (App.SelfTest != null) return;               // no modal dialogs during automated tests
        new BookmarkDialog(this, ex, isNew).ShowDialog();
        RefreshAll();
    }
    string lastFolder = "";
    /// <summary>The site icon for a bookmark, or a generic bookmark glyph when none was captured yet.</summary>
    public static UIElement BmIcon(Bookmark b) => b.IconImage != null
        ? new Image { Source = b.IconImage, Width = 16, Height = 16 }
        : Ic.Icon(MaterialDesignThemes.Wpf.PackIconKind.BookmarkOutline, 16);
    public void OnFavicon(Tab t, string b64)
    {
        bool changed = false;
        foreach (var b in Store.D.Bookmarks.Where(b => b.Url == t.Url && b.Icon != b64)) { b.Icon = b64; changed = true; }
        if (changed) { Store.Save(); RefreshAll(); }
    }
    public void RememberFolder(string f) => lastFolder = f;

    public string AskFolderName(string title, string def = "")
    {
        var p = new PromptWindow(title, "Folder name", def) { Owner = this };
        return p.ShowDialog() == true ? Store.CleanName(p.Value) : null;
    }
    public void NewFolderPrompt(string parent = "")
    {
        var n = AskFolderName("New folder"); if (n == null || n == "") return;
        Store.AddFolder(parent, n); RefreshAll();
    }
    public void OpenAll(string folder, bool replaceFirst = false)
    {
        foreach (var b in Store.AllIn(folder).ToList()) NewTab(b.Url, false);
        if (Tabs.Count > 0) TabList.SelectedIndex = Tabs.Count - 1;
    }

    void FillFolderItems(ItemCollection items, string folder)
    {
        foreach (var f in Store.SubFolders(folder))
        {
            var fp = f; var mi = new MenuItem { Header = Store.LeafName(fp), Icon = Ic.Icon(MaterialDesignThemes.Wpf.PackIconKind.FolderOutline) };
            FillFolderItems(mi.Items, fp);
            items.Add(mi);
        }
        foreach (var b in Store.D.Bookmarks.Where(b => b.Folder == folder))
        {
            var bm = b; var mi = new MenuItem { Header = bm.Title.Length > 48 ? bm.Title[..48] + "…" : bm.Title, ToolTip = bm.Url, Icon = BmIcon(bm) };
            mi.Click += (_, _) => Navigate(bm.Url);
            mi.PreviewMouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { NewTab(bm.Url); e.Handled = true; } };
            items.Add(mi);
        }
        if (items.Count == 0) items.Add(Ic.Item("(empty)", null).Also(x => x.IsEnabled = false));
        else if (Store.AllIn(folder).Any())
        {
            items.Add(new Separator());
            var all = new MenuItem { Header = "Open all in new tabs" }; var fcopy = folder; all.Click += (_, _) => OpenAll(fcopy); items.Add(all);
        }
    }

    public void RefreshBookmarks()
    {
        bool any = Store.D.Bookmarks.Count > 0 || Store.D.Folders.Count > 0;
        BmBar.Visibility = Store.D.Settings.BookmarksBar && any && !full ? Visibility.Visible : Visibility.Collapsed;
        BmPanel.Children.Clear();
        var barMenu = new ContextMenu();
        MenuItem I(string h, Action a) => Ic.Item(h, a);
        barMenu.Items.Add(I("New folder…", () => NewFolderPrompt("")));
        barMenu.Items.Add(I("Bookmark manager", ShowBookmarks));
        barMenu.Items.Add(I("Hide bookmarks bar", () => { Store.D.Settings.BookmarksBar = false; Store.Save(); RefreshAll(); }));
        BmBar.ContextMenu = barMenu; BmPanel.ContextMenu = barMenu; BmPanel.Background = Brushes.Transparent;

        Button Mk(UIElement icon, string text, string tip)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 0) };
            if (icon != null) { if (icon is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 6, 0); sp.Children.Add(icon); }
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return new Button { Content = sp, ToolTip = tip, Style = (Style)FindResource("Flat"), Width = double.NaN, Height = 24, Padding = new Thickness(8, 0, 8, 0), FontSize = 12, Margin = new Thickness(0, 3, 4, 3) };
        }

        foreach (var f in Store.SubFolders("").ToList())
        {
            var fp = f; var btn = Mk(Ic.Icon(MaterialDesignThemes.Wpf.PackIconKind.FolderOutline, 16), Store.LeafName(fp), fp);
            btn.Click += (_, _) =>
            {
                var m = new ContextMenu { PlacementTarget = btn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                FillFolderItems(m.Items, fp); m.IsOpen = true;
            };
            var cm = new ContextMenu();
            cm.Items.Add(I("Open all in new tabs", () => OpenAll(fp)));
            cm.Items.Add(I("New subfolder…", () => NewFolderPrompt(fp)));
            cm.Items.Add(I("Rename…", () => { var n = AskFolderName("Rename folder", Store.LeafName(fp)); if (n != null && n != "") { Store.RenameFolder(fp, n); RefreshAll(); } }));
            cm.Items.Add(I("Delete folder", () => { if (MessageBox.Show(this, $"Delete folder \"{Store.LeafName(fp)}\" and its bookmarks?", "Delete folder", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { Store.DeleteFolder(fp); RefreshAll(); } }));
            btn.ContextMenu = cm; BmPanel.Children.Add(btn);
        }
        foreach (var b in Store.D.Bookmarks.Where(b => b.Folder == "").ToList())
        {
            var bm = b; var btn = Mk(BmIcon(bm), bm.Title.Length > 22 ? bm.Title[..22] + "…" : bm.Title, bm.Url);
            btn.Click += (_, _) => Navigate(bm.Url);
            btn.PreviewMouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { NewTab(bm.Url); e.Handled = true; } };
            var cm = new ContextMenu();
            cm.Items.Add(I("Open in new tab", () => NewTab(bm.Url)));
            cm.Items.Add(I("Edit…", () => { new BookmarkDialog(this, bm, false).ShowDialog(); RefreshAll(); }));
            cm.Items.Add(I("Delete", () => { Store.D.Bookmarks.Remove(bm); Store.Save(); RefreshAll(); }));
            btn.ContextMenu = cm; BmPanel.Children.Add(btn);
        }
    }

    // ---------------------------------------------------------------- history
    void AddHistory(Tab t)
    {
        if (t.Private || !(t.Url.StartsWith("http://") || t.Url.StartsWith("https://"))) return;
        var h = Store.D.History;
        if (h.Count > 0 && h[^1].Url == t.Url && (DateTime.Now - h[^1].Time).TotalSeconds < 5) return;
        h.Add(new HistoryItem { Url = t.Url, Title = t.Title, Time = DateTime.Now });
        if (h.Count > 5000) h.RemoveRange(0, h.Count - 5000);
        Store.Save();
    }
    void TouchHistoryTitle(Tab t) { if (t.Private) return; var h = Store.D.History.LastOrDefault(x => x.Url == t.Url); if (h != null) { h.Title = t.Title; Store.Save(); } }

    // ---------------------------------------------------------------- find
    public void ShowFind() { FindBar.Visibility = Visibility.Visible; FindBox.Focus(); FindBox.SelectAll(); if (FindBox.Text != "") Find_Changed(null, null); }
    void HideFind() { FindBar.Visibility = Visibility.Collapsed; FindCount.Text = ""; Cur?.Browser.GetBrowserHost()?.StopFinding(true); Cur?.Browser.Focus(); }
    void Find_Changed(object s, RoutedEventArgs e)
    {
        var h = Cur?.Browser.GetBrowserHost(); if (h == null) return;
        if (FindBox.Text == "") { h.StopFinding(true); FindCount.Text = ""; return; }
        h.Find(FindBox.Text, true, FindCase.IsChecked == true, false);
    }
    void FindStep(bool fwd) { if (FindBox.Text != "") Cur?.Browser.GetBrowserHost()?.Find(FindBox.Text, fwd, FindCase.IsChecked == true, true); }
    void Find_Key(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { FindStep(Keyboard.Modifiers != ModifierKeys.Shift); e.Handled = true; }
        else if (e.Key == Key.Escape) { HideFind(); e.Handled = true; }
    }
    void FindNext_Click(object s, RoutedEventArgs e) => FindStep(true);
    void FindPrev_Click(object s, RoutedEventArgs e) => FindStep(false);
    void FindClose_Click(object s, RoutedEventArgs e) => HideFind();
    public void OnFindResult(int active, int count) => FindCount.Text = FindBox.Text == "" ? "" : count == 0 ? "0/0" : $"{Math.Max(active, 1)}/{count}";

    // ---------------------------------------------------------------- actions
    public void Zoom(double delta, bool reset = false)
    {
        var b = Cur?.Browser; if (b == null || !b.IsBrowserInitialized) return;
        b.ZoomLevel = reset ? 0 : Math.Clamp(b.ZoomLevel + delta, -5, 5);
        UpdateZoomLabel();
    }
    public void Print() => Cur?.Browser.GetBrowserHost()?.Print();
    public async void SavePdf()
    {
        if (Cur == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PDF|*.pdf", FileName = (Cur.Title.Length > 0 ? string.Concat(Cur.Title.Split(System.IO.Path.GetInvalidFileNameChars())) : "page") + ".pdf" };
        if (dlg.ShowDialog(this) != true) return;
        await SavePdfTo(dlg.FileName);
    }
    public Task<bool> SavePdfTo(string path) => Cur.Browser.PrintToPdfAsync(path, new PdfPrintSettings { BackgroundsEnabled = true });
    public void DevTools() => Cur?.Browser.ShowDevTools();
    public void ViewSource() { if (Cur != null) NewTab("view-source:" + Cur.Url, true, Cur.Private, Cur); }
    public void OpenPrivateWindow(string url) { new MainWindow(true, url).Show(); }
    public async Task ClearCache()
    {
        var b = Tabs.FirstOrDefault(t => !t.Private)?.Browser;
        if (b?.IsBrowserInitialized == true) { using var c = b.GetDevToolsClient(); await c.Network.ClearBrowserCacheAsync(); }
    }

    // fullscreen
    bool full; WindowState prevState; WindowStyle prevStyle;
    public void ToggleFullscreen() => SetFullscreen(!full);
    public void SetHtmlFullscreen(bool fs) => SetFullscreen(fs);
    void SetFullscreen(bool fs)
    {
        if (fs == full) return;
        full = fs;
        var v = fs ? Visibility.Collapsed : Visibility.Visible;
        TabBar.Visibility = v; Toolbar.Visibility = v; Root.Margin = new Thickness(0);
        if (fs) { prevState = WindowState; prevStyle = WindowStyle; WindowStyle = WindowStyle.None; WindowState = WindowState.Normal; WindowState = WindowState.Maximized; BmBar.Visibility = Visibility.Collapsed; }
        else { WindowStyle = prevStyle; WindowState = prevState; RefreshBookmarks(); UpdateWindowState(); }
    }
    public bool IsFullscreen => full;

    void SaveSession()
    {
        if (PrivateWin || App.Browsers.Count > 1 && App.Browsers.Last() != this && false) return;
        Store.D.Session = Tabs.Where(t => !t.Private).Select(t => string.IsNullOrEmpty(Store.Display(t.Url)) ? Store.NewTab : t.Url).Where(u => u.StartsWith("http") || u == Store.NewTab).ToList();
        Store.SaveNow();
    }

    // ---------------------------------------------------------------- shortcuts
    public static bool IsShortcut(int vk, bool ctrl, bool shift, bool alt)
    {
        if (!ctrl && !alt) return vk is 116 or 114 or 122 or 123 || (vk == 27 && false);
        if (alt && !ctrl) return vk is 37 or 39 or 36 or 68;
        // ctrl
        return vk is 84 or 87 or 78 or 76 or 70 or 71 or 82 or 68 or 72 or 74 or 66 or 80 or 85 or 9 or 83 or 73 or 116 or 187 or 189 or 107 or 109 or 48 or 96 or 188 or 46 or (>= 49 and <= 57);
    }
    public void HandleShortcut(int vk, bool ctrl, bool shift, bool alt)
    {
        if (!ctrl && !alt)
        {
            switch (vk) { case 116: Reload_Click(null, null); break; case 114: FindStep(!shift); break; case 122: ToggleFullscreen(); break; case 123: DevTools(); break; }
            return;
        }
        if (alt) { switch (vk) { case 37: Back_Click(null, null); break; case 39: Fwd_Click(null, null); break; case 36: Home_Click(null, null); break; case 68: FocusUrl(); break; } return; }
        switch (vk)
        {
            case 84: if (shift) ReopenClosed(); else NewTab(null); break;
            case 87: if (Cur != null) CloseTab(Cur); break;
            case 78: if (shift) OpenPrivateWindow(null); else new MainWindow(false).Show(); break;
            case 76: FocusUrl(); break;
            case 70: ShowFind(); break;
            case 71: FindStep(!shift); break;
            case 82: case 116: if (shift || vk == 116) Cur?.Browser.Reload(true); else Cur?.Browser.Reload(); break;
            case 68: ToggleBookmark(); break;
            case 72: ShowHistory(); break;
            case 74: ShowDownloads(); break;
            case 66: ShowBookmarks(); break;
            case 80: Print(); break;
            case 83: SavePdf(); break;
            case 85: ViewSource(); break;
            case 73: if (shift) DevTools(); break;
            case 188: ShowSettings(); break;
            case 46: if (shift) ShowSettings(); break;
            case 187: case 107: Zoom(0.5); break;
            case 189: case 109: Zoom(-0.5); break;
            case 48: case 96: Zoom(0, true); break;
            case 9:
                if (Tabs.Count > 1) { int i = Tabs.IndexOf(Cur); i = (i + (shift ? -1 : 1) + Tabs.Count) % Tabs.Count; TabList.SelectedIndex = i; }
                break;
            default:
                if (vk >= 49 && vk <= 57) { int n = vk - 49; TabList.SelectedIndex = vk == 57 ? Tabs.Count - 1 : Math.Min(n, Tabs.Count - 1); }
                break;
        }
    }
    void Win_KeyDown(object s, KeyEventArgs e)
    {
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        int vk = KeyInterop.VirtualKeyFromKey(k);
        var m = Keyboard.Modifiers;
        if (k == Key.Escape && IsFullscreen) { SetFullscreen(false); e.Handled = true; return; }
        if (IsShortcut(vk, m.HasFlag(ModifierKeys.Control), m.HasFlag(ModifierKeys.Shift), m.HasFlag(ModifierKeys.Alt)))
        { HandleShortcut(vk, m.HasFlag(ModifierKeys.Control), m.HasFlag(ModifierKeys.Shift), m.HasFlag(ModifierKeys.Alt)); e.Handled = true; }
    }
}
