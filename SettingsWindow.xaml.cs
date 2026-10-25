using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CefSharp;

namespace Lite;

public partial class SettingsWindow : ThemedWindow
{
    readonly MainWindow main;
    readonly Settings s = Store.D.Settings;
    readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    bool loading;

    public SettingsWindow(MainWindow owner)
    {
        InitializeComponent(); main = owner; Owner = owner;
        toastTimer.Tick += (_, _) => { toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; };
        LoadValues();
        Updater.Changed += SyncUpdate; Closed += (_, _) => Updater.Changed -= SyncUpdate;
        AdBlocker.Changed += SyncFilters; Closed += (_, _) => AdBlocker.Changed -= SyncFilters; SyncFilters();
        Nav.SelectedIndex = 0;
    }

    /// <summary>Opens the given page (General, Appearance, Privacy, Downloads, Updates, About).</summary>
    public void ShowPage(string name) { foreach (ListBoxItem i in Nav.Items) if ((string)i.Tag == name) Nav.SelectedItem = i; }

    void LoadValues()
    {
        loading = true;
        var match = Engines.Children.OfType<RadioButton>().FirstOrDefault(r => (string)r.Tag == s.Engine && (string)r.Tag != "custom");
        if (match != null) match.IsChecked = true; else { EngineCustom.IsChecked = true; CustomEngine.Text = s.Engine; CustomBox.Visibility = Visibility.Visible; }
        HomeBox.Text = s.Home == Store.NewTab ? "" : s.Home; HomeBox.Tag = null;
        TglRestore.IsChecked = s.Restore; TglBar.IsChecked = s.BookmarksBar; TglDark.IsChecked = s.DarkPages; TglAds.IsChecked = s.AdBlock; TglEasyList.IsChecked = s.BlockAds; TglEasyPrivacy.IsChecked = s.BlockTrackers;
        TglAsk.IsChecked = s.AskDownload; TglUpdate.IsChecked = s.AutoUpdate; DlDir.Text = s.DownloadDir;
        AboutVersion.Text = "Version " + Updater.CurrentText; AboutEngine.Text = "Chromium " + Cef.ChromiumVersion + " · CefSharp " + Cef.CefSharpVersion; AboutData.Text = Store.Dir;
        SyncUpdate();
        loading = false;
    }

    void Flash(string text) { ToastText.Text = text; Toast.Visibility = Visibility.Visible; toastTimer.Stop(); toastTimer.Start(); }
    void Save(string toast = "Saved") { Store.Save(); if (!loading) Flash(toast); }

    void Nav_Changed(object sender, SelectionChangedEventArgs e)
    {
        var page = (Nav.SelectedItem as ListBoxItem)?.Tag as string;
        PageGeneral.Visibility = page == "General" ? Visibility.Visible : Visibility.Collapsed;
        PageAppearance.Visibility = page == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        PagePrivacy.Visibility = page == "Privacy" ? Visibility.Visible : Visibility.Collapsed;
        PageDownloads.Visibility = page == "Downloads" ? Visibility.Visible : Visibility.Collapsed;
        PageUpdates.Visibility = page == "Updates" ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = page == "About" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- general
    void Engine_Checked(object sender, RoutedEventArgs e)
    {
        var tag = (string)((RadioButton)sender).Tag;
        CustomBox.Visibility = tag == "custom" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "custom") { if (!loading && string.IsNullOrEmpty(CustomEngine.Text)) CustomEngine.Text = s.Engine; return; }
        s.Engine = tag; Save("Search engine changed");
    }
    void CustomEngine_Changed(object sender, TextChangedEventArgs e)
    {
        bool ok = CustomEngine.Text.Contains("%s");
        CustomHint.Text = ok || CustomEngine.Text == "" ? "Put %s where the search words should go, for example https://example.com/?q=%s" : "The address needs %s where the search words should go.";
        CustomHint.Foreground = ok || CustomEngine.Text == "" ? (System.Windows.Media.Brush)FindResource("Mut") : (System.Windows.Media.Brush)FindResource("Danger");
        if (ok && !loading) { s.Engine = CustomEngine.Text.Trim(); Save("Search engine changed"); }
    }
    void Home_Commit(object sender, RoutedEventArgs e)
    {
        var v = HomeBox.Text.Trim(); var now = v == "" ? Store.NewTab : v;
        if (now == s.Home) return; s.Home = now; Save("Home page saved");
    }
    void Home_Key(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { Home_Commit(sender, e); Keyboard.ClearFocus(); } }
    void Home_Changed(object sender, TextChangedEventArgs e) => HomeHint.Visibility = HomeBox.Text == "" ? Visibility.Visible : Visibility.Collapsed;
    void HomeReset_Click(object sender, RoutedEventArgs e) { HomeBox.Text = ""; s.Home = Store.NewTab; Save("Home page reset"); }
    void Restore_Click(object sender, RoutedEventArgs e) { s.Restore = TglRestore.IsChecked == true; Save(); }

    // ---- appearance
    void Bar_Click(object sender, RoutedEventArgs e) { s.BookmarksBar = TglBar.IsChecked == true; Save(); MainWindow.RefreshAll(); }
    void Dark_Click(object sender, RoutedEventArgs e) { s.DarkPages = TglDark.IsChecked == true; Save("Saved. Restart Orbit to apply."); }

    // ---- privacy
    void Ads_Click(object sender, RoutedEventArgs e) { s.AdBlock = TglAds.IsChecked == true; Save(); }
    void EasyList_Click(object sender, RoutedEventArgs e) { s.BlockAds = TglEasyList.IsChecked == true; Save(); _ = ApplyLists(); }
    void EasyPrivacy_Click(object sender, RoutedEventArgs e) { s.BlockTrackers = TglEasyPrivacy.IsChecked == true; Save(); _ = ApplyLists(); }
    /// <summary>After a list is switched on or off: download it if it isn't saved yet, then rebuild the rules.</summary>
    async Task ApplyLists() { if (AdBlocker.Lists.Any(l => l.Enabled(s) && !File.Exists(Path.Combine(AdBlocker.Dir, l.File)))) await AdBlocker.UpdateAsync(); else await AdBlocker.RebuildAsync(); }
    async void FilterUpdate_Click(object sender, RoutedEventArgs e) { FilterUpdate.IsEnabled = false; var ok = await AdBlocker.UpdateAsync(); FilterUpdate.IsEnabled = true; Flash(ok ? "Filter lists updated" : "Couldn't update every list"); }
    void Resume_Click(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is string host) { AdBlocker.SetPaused(host, false); Flash("Blocking resumed on " + host); } }
    void SyncFilters()
    {
        FilterStatus.Text = string.IsNullOrEmpty(AdBlocker.Message) ? "Filter lists load when Orbit starts." : AdBlocker.Message;
        FilterProg.Visibility = AdBlocker.Busy ? Visibility.Visible : Visibility.Collapsed; FilterProg.IsIndeterminate = AdBlocker.Busy; FilterUpdate.IsEnabled = !AdBlocker.Busy;
        PausedList.ItemsSource = null; PausedList.ItemsSource = s.AdAllow.ToList(); PausedEmpty.Visibility = s.AdAllow.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        int n = Store.D.History.Count;
        if (n == 0) { Flash("Your history is already empty"); return; }
        if (!ConfirmWindow.Ask(this, "Clear browsing history?", $"This removes {n:N0} pages from your history on this computer. Bookmarks aren't affected.", "Clear history", destructive: true)) return;
        Store.D.History.Clear(); Store.Save(); Flash("History cleared");
    }
    async void ClearCookies_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmWindow.Ask(this, "Clear cookies and site data?", "You'll be signed out of most websites and saved site settings are removed.", "Clear cookies", destructive: true)) return;
        await Cef.GetGlobalCookieManager().DeleteCookiesAsync("", ""); Flash("Cookies cleared");
    }
    async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmWindow.Ask(this, "Clear cached files?", "Orbit will download images and scripts again as you browse, so some pages may load more slowly once.", "Clear cache", destructive: true)) return;
        foreach (var w in App.Browsers.ToList()) await w.ClearCache(); Flash("Cache cleared");
    }

    // ---- downloads
    void DlChange_Click(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = s.DownloadDir, Title = "Choose where to save downloads" };
        if (d.ShowDialog(this) == true) { s.DownloadDir = d.FolderName; DlDir.Text = d.FolderName; Save("Download folder changed"); }
    }
    void DlOpen_Click(object sender, RoutedEventArgs e) { try { Directory.CreateDirectory(s.DownloadDir); Process.Start("explorer.exe", $"\"{s.DownloadDir}\""); } catch { } }
    void Ask_Click(object sender, RoutedEventArgs e) { s.AskDownload = TglAsk.IsChecked == true; Save(); }

    // ---- updates
    void Update_Click(object sender, RoutedEventArgs e) { s.AutoUpdate = TglUpdate.IsChecked == true; Save(); }
    async void UpdCheck_Click(object sender, RoutedEventArgs e) { UpdCheck.IsEnabled = false; await Updater.CheckAsync(download: true); UpdCheck.IsEnabled = true; }
    void UpdRestart_Click(object sender, RoutedEventArgs e) => Updater.InstallAndRestart();
    void UpdNotes_Click(object sender, RoutedEventArgs e) { if (Updater.Latest?.PageUrl is { Length: > 0 } u) main.NewTab(u); }
    void SyncUpdate()
    {
        UpdVersion.Text = "Orbit " + Updater.CurrentText;
        UpdStatus.Text = Updater.Phase == UpdatePhase.Idle ? (s.AutoUpdate ? "Orbit checks for updates each time it starts." : "Automatic checks are off.") : Updater.Message;
        UpdStatus.Foreground = (System.Windows.Media.Brush)FindResource(Updater.Phase == UpdatePhase.Failed ? "Danger" : "Mut");
        UpdProg.Visibility = Updater.Phase == UpdatePhase.Downloading ? Visibility.Visible : Visibility.Collapsed; UpdProg.Value = Updater.Progress * 100;
        UpdRestart.Visibility = Updater.Phase == UpdatePhase.Ready ? Visibility.Visible : Visibility.Collapsed;
        UpdNotes.Visibility = Updater.Latest?.PageUrl is { Length: > 0 } && Updater.Phase is UpdatePhase.Ready or UpdatePhase.Available ? Visibility.Visible : Visibility.Collapsed;
        UpdCheck.IsEnabled = Updater.Phase is not (UpdatePhase.Checking or UpdatePhase.Downloading);
    }

    // ---- about
    void Link_Click(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is string u) main.NewTab(u); }
}
