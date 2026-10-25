using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Lite;

/// <summary>Extensions in the toolbar (icons that open the extension's pop-up) and the "Add to Orbit" bar on Chrome Web Store pages.</summary>
public partial class MainWindow
{
    internal Popup extPopup;
    readonly HashSet<string> storeDismissed = new();

    void InitExtensions()
    {
        void Changed() => Dispatcher.BeginInvoke(() => { RefreshExtBar(); UpdateStoreBar(); });
        Extensions.Changed += Changed; Closed += (_, _) => Extensions.Changed -= Changed;
        Deactivated += (_, _) => CloseExtPopup();
        RefreshExtBar();
    }

    void RefreshExtBar()
    {
        ExtBar.Children.Clear();
        if (PrivateWin) return;           // extensions can't see private tabs, so they stay out of private windows
        foreach (var x in Extensions.Loaded.ToList())
        {
            var ext = x;
            UIElement icon = ext.Icon != null ? new Image { Source = ext.Icon, Width = 18, Height = 18, SnapsToDevicePixels = true } : Ic.Icon(MaterialDesignThemes.Wpf.PackIconKind.PuzzleOutline, 18);
            var b = new Button { Content = icon, Style = (Style)FindResource("Flat"), ToolTip = ext.Name, Width = 30, Height = 30, Tag = ext };
            b.Click += (_, _) => ShowExtPopup(ext, b);
            var m = new ContextMenu();
            if (ext.OptionsPage != "") m.Items.Add(Ic.Item("Options", () => NewTab("chrome-extension://" + ext.Id + "/" + ext.OptionsPage.TrimStart('/'))));
            m.Items.Add(Ic.Item("Manage extensions", () => ShowSettings("Extensions")));
            m.Items.Add(Ic.Item("Remove from Orbit", async () => { if (ConfirmWindow.Ask(this, "Remove " + ext.Name + "?", "The extension and its saved settings are removed from Orbit.", "Remove", destructive: true)) await Extensions.RemoveAsync(ext); }));
            b.ContextMenu = m;
            ExtBar.Children.Add(b);
        }
    }

    void CloseExtPopup()
    {
        if (extPopup == null) return;
        var p = extPopup; extPopup = null; p.IsOpen = false;
        if (p.Child is Border { Child: Wv w }) { p.Child = null; w.Dispose(); }
    }

    /// <summary>Opens the extension's pop-up page under its toolbar button, sized to its content like Chrome does.</summary>
    public async void ShowExtPopup(ExtInfo x, FrameworkElement anchor)
    {
        var again = extPopup?.PlacementTarget == anchor;
        CloseExtPopup(); if (again) return;
        if (x.PopupPage == "") { await ClickExtension(x); return; }
        var wv = new Wv(PrivateWin) { Width = 380, Height = 420 };
        var pop = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = true, AllowsTransparency = false, Child = new Border { BorderThickness = new Thickness(1), BorderBrush = (Brush)FindResource("Outline"), Background = (Brush)FindResource("Bar"), Child = wv } };
        extPopup = pop; pop.IsOpen = true;
        try
        {
            await wv.InitAsync(await App.EnvTask);
            if (wv.Disposed || extPopup != pop) return;
            await wv.Core.AddScriptToExecuteOnDocumentCreatedAsync(ActiveTabScript(Cur?.Url ?? ""));
            wv.Core.NewWindowRequested += (_, e) => { e.Handled = true; NewTab(e.Uri); CloseExtPopup(); };
            wv.Core.WindowCloseRequested += (_, _) => CloseExtPopup();
            wv.Core.NavigationCompleted += async (_, _) =>
            {
                for (int i = 0; i < 4; i++)       // pop-ups often fill themselves in after load; follow their size for a moment
                {
                    await Task.Delay(250); if (wv.Disposed) return;
                    var r = await wv.EvalAsync("Math.ceil(Math.max(document.documentElement.scrollWidth,document.body?document.body.scrollWidth:0))+','+Math.ceil(Math.max(document.documentElement.scrollHeight,document.body?document.body.scrollHeight:0))");
                    var p = r?.Trim('"').Split(',');
                    if (p?.Length == 2 && double.TryParse(p[0], out var w) && double.TryParse(p[1], out var h)) { wv.Width = Math.Clamp(w, 280, 800); wv.Height = Math.Clamp(h, 120, 600); }
                }
            };
            wv.Go("chrome-extension://" + x.Id + "/" + x.PopupPage.TrimStart('/'));
        }
        catch { CloseExtPopup(); }
    }

    /// <summary>
    /// WebView2 treats every page view as its own window, so inside a pop-up "the active tab in this window" is the pop-up itself.
    /// This makes chrome.tabs.query({active:true}) answer with the page the user is actually looking at.
    /// </summary>
    static string ActiveTabScript(string url) => "(function(){var URL=" + System.Text.Json.JsonSerializer.Serialize(url) + @";
function fix(T){if(!T||!T.query||T.__orbit)return;T.__orbit=1;var o=T.query.bind(T);
T.query=function(q,cb){var r=o({}).then(function(all){if(q&&q.active){var m=all.filter(function(t){return t.url===URL});if(m.length)return[Object.assign({},m[0],{active:true,highlighted:true})]}return o(q||{})});
if(typeof cb==='function'){r.then(cb);return}return r}}
try{fix(chrome.tabs)}catch(e){}try{if(typeof browser!=='undefined')fix(browser.tabs)}catch(e){}})();";

    /// <summary>A toolbar button without a pop-up: run the extension's own click handler on the current page, waking its background script first if it has gone to sleep.</summary>
    async Task ClickExtension(ExtInfo x)
    {
        var core = Cur?.Wv.Core; if (core == null) return;
        string avoid = PrivateWin && App.Browsers.FirstOrDefault(b => !b.PrivateWin)?.Cur?.Wv.Core is { } nc ? await Extensions.ContextOf(nc) : null;
        int n = await Extensions.TriggerClickAsync(core, x, Cur.Url, avoid);
        if (n <= 0)      // not running (or a sleeping copy that hasn't registered its handlers yet)
        {
            var wake = new Wv(PrivateWin) { Width = 1, Height = 1, Visibility = Visibility.Hidden };   // opening any page of the extension starts its background script again
            Content_.Children.Add(wake);
            try
            {
                await wake.InitAsync(await App.EnvTask); wake.Go("chrome-extension://" + x.Id + "/orbit-wake.html");
                for (int i = 0; i < 10 && n <= 0; i++) { await Task.Delay(500); n = await Extensions.TriggerClickAsync(core, x, Cur.Url, avoid); }
            }
            catch { }
            finally { Content_.Children.Remove(wake); wake.Dispose(); }
        }
        if (n <= 0) { if (x.OptionsPage != "") NewTab("chrome-extension://" + x.Id + "/" + x.OptionsPage.TrimStart('/')); else ShowSettings("Extensions"); }   // nothing to run: show its settings instead
    }

    // ---------------------------------------------------------------- Chrome Web Store
    void UpdateStoreBar()
    {
        var t = Cur; var id = PrivateWin ? null : Extensions.StoreId(t?.Url);
        bool installed = id != null && Extensions.Loaded.Any(e => e.Folder.EndsWith(System.IO.Path.DirectorySeparatorChar + id, StringComparison.OrdinalIgnoreCase));
        bool show = id != null && !installed && !storeDismissed.Contains(id);
        if (show && StoreBar.Visibility != Visibility.Visible) { StoreText.Text = "This is a Chrome extension. Orbit can install it for you."; StoreAdd.IsEnabled = true; StoreAdd.Content = "Add to Orbit"; }
        StoreBar.Visibility = show || StoreAdd.Content as string == "Adding…" ? Visibility.Visible : Visibility.Collapsed;
        StoreBar.Tag = id;
    }
    void StoreHide_Click(object s, RoutedEventArgs e) { if (StoreBar.Tag is string id) storeDismissed.Add(id); StoreBar.Visibility = Visibility.Collapsed; }
    async void StoreAdd_Click(object s, RoutedEventArgs e)
    {
        if (StoreBar.Tag is not string id) return;
        StoreAdd.IsEnabled = false; StoreAdd.Content = "Adding…"; StoreText.Text = "Downloading the extension…";
        try
        {
            var info = await Extensions.InstallFromStoreAsync(id, m => StoreText.Text = m);
            StoreText.Text = info.Name + " was added to Orbit. Reload open pages to start using it.";
            storeDismissed.Add(id); StoreAdd.Content = "Done";
            await Task.Delay(4000); if (StoreAdd.Content as string == "Done") { StoreBar.Visibility = Visibility.Collapsed; StoreAdd.Content = "Add to Orbit"; }
        }
        catch (Exception ex) { StoreText.Text = "Couldn't add it: " + ex.Message; StoreAdd.Content = "Try again"; StoreAdd.IsEnabled = true; }
    }
}
