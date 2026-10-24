using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CefSharp;

namespace Lite;

public partial class DownloadsWindow : ThemedWindow
{
    public static readonly ObservableCollection<DownloadEntry> Items = new();
    static readonly Dictionary<int, DownloadEntry> map = new();

    public static void Add(int id, string url, string path)
    {
        var e = new DownloadEntry { Id = id, Url = url, Path = path, Name = System.IO.Path.GetFileName(path) };
        map[id] = e; Items.Insert(0, e);
    }
    public static void Update(int id, long rec, long tot, int pct, bool done, bool cancel, string path, IDownloadItemCallback cb)
    {
        if (!map.TryGetValue(id, out var e)) return;
        e.Callback = cb; e.Received = rec; e.Total = tot; e.Percent = pct; e.Done = done; e.Canceled = cancel;
        if (!string.IsNullOrEmpty(path)) { e.Path = path; e.Name = System.IO.Path.GetFileName(path); }
    }

    public DownloadsWindow(Window owner)
    {
        InitializeComponent(); Owner = owner;
        Items.CollectionChanged += OnItems; OnItems(null, null);
        Closing += (_, e) => { e.Cancel = true; Hide(); };     // keep progress tracking alive; the window just hides
    }

    void OnItems(object s, NotifyCollectionChangedEventArgs e)
    {
        bool none = Items.Count == 0;
        Empty.Visibility = none ? Visibility.Visible : Visibility.Collapsed; ListView.Visibility = none ? Visibility.Collapsed : Visibility.Visible; ClearBtn.IsEnabled = !none;
    }

    static DownloadEntry Of(object sender) => (DownloadEntry)((FrameworkElement)sender).DataContext;
    void Open_Click(object sender, RoutedEventArgs e) { var d = Of(sender); if (File.Exists(d.Path)) Process.Start(new ProcessStartInfo(d.Path) { UseShellExecute = true }); }
    void Show_Click(object sender, RoutedEventArgs e) { var d = Of(sender); if (File.Exists(d.Path)) Process.Start("explorer.exe", "/select,\"" + d.Path + "\""); else Folder_Click(sender, e); }
    void Pause_Click(object sender, RoutedEventArgs e) { var d = Of(sender); try { if (d.Paused) d.Callback?.Resume(); else d.Callback?.Pause(); d.Paused = !d.Paused; } catch { } }
    void Cancel_Click(object sender, RoutedEventArgs e) { try { Of(sender).Callback?.Cancel(); } catch { } }
    void Remove_Click(object sender, RoutedEventArgs e) { var d = Of(sender); Items.Remove(d); map.Remove(d.Id); }
    void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var d in Items.Where(x => !x.Active).ToList()) { Items.Remove(d); map.Remove(d.Id); }   // never drop a download that is still running
    }
    void Folder_Click(object sender, RoutedEventArgs e) { try { var dir = Store.D.Settings.DownloadDir; Directory.CreateDirectory(dir); Process.Start("explorer.exe", $"\"{dir}\""); } catch { } }
}
