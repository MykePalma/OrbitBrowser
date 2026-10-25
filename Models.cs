using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CefSharp;
using MaterialDesignThemes.Wpf;
using CefSharp.Wpf.HwndHost;

namespace Lite;

public class Bookmark
{
    public string Title { get; set; } = ""; public string Url { get; set; } = ""; public string Folder { get; set; } = "";
    /// <summary>Site icon as base64 PNG (kept so it shows offline and survives restarts).</summary>
    public string Icon { get; set; } = "";
    ImageSource img; string imgFor;
    [System.Text.Json.Serialization.JsonIgnore]
    public ImageSource IconImage
    {
        get
        {
            if (string.IsNullOrEmpty(Icon)) return null;
            if (img != null && imgFor == Icon) return img;
            try
            {
                var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(Convert.FromBase64String(Icon)); bi.EndInit(); bi.Freeze();
                img = bi; imgFor = Icon; return bi;
            }
            catch { return null; }
        }
    }
}
public class HistoryItem { public string Title { get; set; } = ""; public string Url { get; set; } = ""; public DateTime Time { get; set; } public string When => Time.ToString("g"); }

public class Settings
{
    public string Home { get; set; } = "lite://newtab";
    public string Engine { get; set; } = "https://duckduckgo.com/?q=%s";
    public bool AdBlock { get; set; } = true;
    public bool BlockAds { get; set; } = true;          // EasyList
    public bool BlockTrackers { get; set; } = true;     // EasyPrivacy
    /// <summary>Sites (and their sub-domains) where blocking is paused.</summary>
    public List<string> AdAllow { get; set; } = new();
    public DateTime LastFilterUpdateUtc { get; set; }
    public bool Restore { get; set; } = true;
    public bool DarkPages { get; set; }
    public bool AskDownload { get; set; }
    public bool BookmarksBar { get; set; } = true;
    /// <summary>Look for a newer Orbit on GitHub at start-up, download it in the background and install it on the next restart.</summary>
    public bool AutoUpdate { get; set; } = true;
    public DateTime LastUpdateCheckUtc { get; set; }
    public string DownloadDir { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
}

public class Data
{
    public List<Bookmark> Bookmarks { get; set; } = new();
    public List<string> Folders { get; set; } = new();
    public List<HistoryItem> History { get; set; } = new();
    public List<string> Session { get; set; } = new();
    public Settings Settings { get; set; } = new();
}

public static class Store
{
    /// <summary>Automated test runs (--selftest*) use a throwaway folder so they never touch the real profile.</summary>
    static readonly bool TestRun = Environment.GetCommandLineArgs().Any(x => x.StartsWith("--selftest"));
    public static bool IsTestRun => TestRun;
    /// <summary>ORBIT_DATA_DIR relocates the whole profile (handy for portable use and for testing updates without touching the real one).</summary>
    public static readonly string Dir = TestRun
        ? Path.Combine(Path.GetTempPath(), "Orbit-selftest")
        : Environment.GetEnvironmentVariable("ORBIT_DATA_DIR") is { Length: > 0 } custom ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Orbit");
    static readonly string LegacyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiteBrowser");
    static string File_ => Path.Combine(Dir, "data.json");
    public static Data D = new();
    static System.Threading.Timer timer;

    public static void Load()
    {
        // the app used to be called LiteBrowser: carry its bookmarks, history and settings over once
        if (TestRun) { try { Directory.Delete(Dir, true); } catch { } }
        else try { if (!Directory.Exists(Dir) && Directory.Exists(LegacyDir)) Directory.Move(LegacyDir, Dir); } catch { }
        Directory.CreateDirectory(Dir);
        try { D = JsonSerializer.Deserialize<Data>(File.ReadAllText(File_)) ?? new Data(); } catch { D = new Data(); }
    }
    public static void Save()
    {
        timer?.Dispose();
        timer = new System.Threading.Timer(_ => SaveNow(), null, 400, Timeout.Infinite);
    }
    public static void SaveNow()
    {
        try { lock (D) File.WriteAllText(File_, JsonSerializer.Serialize(D)); } catch { }
    }

    // ---- bookmark folders: stored as "/"-separated paths; "" is the bookmarks bar root
    public static string CleanName(string n) => (n ?? "").Replace("/", "-").Trim();
    public static string Parent(string path) { int i = path.LastIndexOf('/'); return i < 0 ? "" : path[..i]; }
    public static string LeafName(string path) { int i = path.LastIndexOf('/'); return i < 0 ? path : path[(i + 1)..]; }
    public static string Join(string parent, string name) => parent == "" ? name : parent + "/" + name;
    public static bool InFolder(string path, string folder) => path == folder || path.StartsWith(folder + "/");
    public static IEnumerable<string> SubFolders(string parent) =>
        D.Folders.Where(f => Parent(f) == parent).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
    public static IEnumerable<string> FoldersSorted() => D.Folders.OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
    public static string AddFolder(string parent, string name)
    {
        name = CleanName(name); if (name == "") return null;
        var path = Join(parent, name);
        for (int i = 2; D.Folders.Contains(path, StringComparer.OrdinalIgnoreCase); i++) path = Join(parent, name + " " + i);
        // make sure all ancestors exist too
        for (var p = Parent(path); p != ""; p = Parent(p)) if (!D.Folders.Contains(p)) D.Folders.Add(p);
        D.Folders.Add(path); Save(); return path;
    }
    public static string RenameFolder(string path, string newName)
    {
        newName = CleanName(newName); if (newName == "" || newName == LeafName(path)) return path;
        var np = Join(Parent(path), newName);
        if (D.Folders.Contains(np, StringComparer.OrdinalIgnoreCase)) return path;
        return Relocate(path, np);
    }
    /// <summary>Moves folder <paramref name="path"/> under <paramref name="newParent"/> (refuses to move into itself).</summary>
    public static string MoveFolder(string path, string newParent)
    {
        if (InFolder(newParent, path) || Parent(path) == newParent) return path;
        var np = Join(newParent, LeafName(path));
        if (D.Folders.Contains(np, StringComparer.OrdinalIgnoreCase)) return path;
        return Relocate(path, np);
    }
    static string Relocate(string from, string to)
    {
        string Fix(string p) => p == from ? to : p.StartsWith(from + "/") ? to + p[from.Length..] : p;
        D.Folders = D.Folders.Select(Fix).ToList();
        foreach (var b in D.Bookmarks) b.Folder = Fix(b.Folder);
        Save(); return to;
    }
    public static void DeleteFolder(string path)
    {
        D.Folders.RemoveAll(f => InFolder(f, path));
        D.Bookmarks.RemoveAll(b => InFolder(b.Folder, path));
        Save();
    }
    public static IEnumerable<Bookmark> AllIn(string folder) => D.Bookmarks.Where(b => InFolder(b.Folder, folder));

    public const string NewTab = "lite://newtab";
    public static string Real(string url)
    {
        if (url == null) return "about:blank";
        if (url.StartsWith("lite://newtab")) return NewTabFile();
        return url;
    }
    public static string Display(string url)
    {
        if (string.IsNullOrEmpty(url) || url == "about:blank") return "";
        if (url.EndsWith("/newtab.html", StringComparison.OrdinalIgnoreCase) && url.StartsWith("file:")) return "";
        return url;
    }
    public static string Resolve(string input)
    {
        input = (input ?? "").Trim();
        if (input == "") return Real(D.Settings.Home);
        if (input.StartsWith("lite://")) return Real(input);
        if (System.Text.RegularExpressions.Regex.IsMatch(input, @"^(https?|file|about|data|view-source|chrome|devtools|blob|ftp):", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return input;
        if (System.Text.RegularExpressions.Regex.IsMatch(input, @"^(localhost|(\d{1,3}\.){3}\d{1,3})(:\d+)?([/?#].*)?$")) return "http://" + input;
        if (!input.Contains(' ') && System.Text.RegularExpressions.Regex.IsMatch(input, @"^[^\s/]+\.[a-zA-Z]{2,}(:\d+)?([/?#].*)?$")) return "https://" + input;
        return D.Settings.Engine.Replace("%s", Uri.EscapeDataString(input));
    }

    /// <summary>The Orbit logo (same artwork as Assets/orbit.svg), inlined for the new-tab page.</summary>
    const string LogoSvg = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none'><defs><linearGradient id='og' gradientUnits='userSpaceOnUse' x1='2' y1='22' x2='22' y2='2'><stop offset='0' stop-color='#FF8A1F'/><stop offset='.5' stop-color='#E8559A'/><stop offset='1' stop-color='#2F80FF'/></linearGradient></defs><circle cx='12' cy='12' r='2.4' stroke='url(#og)' stroke-width='1.7'/><ellipse cx='12' cy='12' rx='9.2' ry='4.2' transform='rotate(-35 12 12)' stroke='url(#og)' stroke-width='1.5'/><circle cx='19.2' cy='6.1' r='1.5' fill='url(#og)'/></svg>";
    static string FaviconSvg => Uri.EscapeDataString(LogoSvg);

    public static string NewTabFile()
    {
        var sites = D.Bookmarks.Select(b => b.Url).Concat(D.History.Select(h => h.Url)).Select(u => { try { var x = new Uri(u); return x.Scheme.StartsWith("http") ? x.GetLeftPart(UriPartial.Authority) : null; } catch { return null; } })
            .Where(x => x != null).Distinct().Take(8).ToList();
        var tiles = string.Concat(sites.Select(s => { var h = new Uri(s).Host.Replace("www.", ""); return $"<a href='{s}'><b>{char.ToUpper(h[0])}</b>{h}</a>"; }));
        var engine = D.Settings.Engine.Replace("\\", "\\\\").Replace("'", "\\'");
        var html = $@"<!doctype html><meta charset=utf8><title>New Tab</title><link rel=icon href='data:image/svg+xml,{FaviconSvg}'><style>
:root{{color-scheme:dark light}}body{{margin:0;background:#202124;color:#e8eaed;font:16px/1.5 'Segoe UI Variable Text','Segoe UI',system-ui,sans-serif;text-align:center;padding-top:16vh}}
@media(prefers-color-scheme:light){{body{{background:#fff;color:#202124}}}}
h1{{font-weight:300;font-size:46px;margin:0 0 26px;display:flex;align-items:center;justify-content:center;gap:14px}}h1 svg{{width:54px;height:54px}}input{{width:min(560px,86vw);height:48px;box-sizing:border-box;padding:0 24px;border-radius:26px;border:1px solid #8886;background:#8883;color:inherit;font:inherit;outline:0}}
input::placeholder{{color:#9aa0a6;opacity:1}}input:focus-visible{{outline:2px solid #8ab4f8;outline-offset:2px;border-color:#8ab4f8}}a:focus-visible{{outline:2px solid #8ab4f8;outline-offset:2px}}@media(prefers-reduced-motion:reduce){{*{{transition:none!important;animation:none!important}}}}#g{{display:grid;grid-template-columns:repeat(auto-fill,110px);gap:14px;justify-content:center;margin:40px auto 0;max-width:640px}}
a{{background:#8882;border-radius:12px;padding:16px 8px;color:inherit;text-decoration:none;font-size:13px;overflow:hidden;white-space:nowrap;text-overflow:ellipsis}}a:hover{{background:#8884}}
b{{display:block;font-size:22px;margin-bottom:6px;color:#8ab4f8}}</style>
<h1>{LogoSvg}Orbit</h1><form id=f><input id=q autofocus placeholder='Search the web or enter an address' autocomplete=off></form><div id=g>{tiles}</div>
<script>f.onsubmit=e=>{{e.preventDefault();var v=q.value.trim();if(!v)return;location.href=/^https?:\/\//i.test(v)?v:/^[^\s\/]+\.[a-z]{{2,}}(\/\S*)?$/i.test(v)?'https://'+v:'{engine}'.replace('%s',encodeURIComponent(v))}}</script>";
        var path = Path.Combine(Dir, "newtab.html");
        try { File.WriteAllText(path, html); } catch { }
        return new Uri(path).AbsoluteUri;
    }
}

public class Tab : INotifyPropertyChanged
{
    public ChromiumWebBrowser Browser;
    public bool Private;
    /// <summary>Tab shown side by side with this one (split view); links are always mutual.</summary>
    Tab partner; public Tab Partner { get => partner; set { partner = value; P(); P(nameof(SplitVis)); } }
    public bool SplitLeft;
    /// <summary>Set by the window that accepted this tab when it was dragged there.</summary>
    public object MovedTo;
    public Visibility SplitVis => Partner != null ? Visibility.Visible : Visibility.Collapsed;
    public event PropertyChangedEventHandler PropertyChanged;
    void P([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    void Pn(params string[] n) { foreach (var x in n) P(x); }

    string title = "New Tab"; public string Title { get => title; set { title = string.IsNullOrWhiteSpace(value) ? "New Tab" : value; P(); } }
    string url = ""; public string Url { get => url; set { url = value ?? ""; try { Host = url.StartsWith("http") ? new Uri(url).Host.ToLowerInvariant() : ""; } catch { Host = ""; } P(); } }
    /// <summary>Host of the page in this tab (lower-case), or empty for non-web pages; read by the ad blocker on other threads.</summary>
    public volatile string Host = "";
    /// <summary>Requests the ad blocker has stopped for the current page.</summary>
    public int BlockedCount; public int BlockedDirty;
    /// <summary>Distinct URLs blocked on the current page (guarded by its own lock).</summary>
    public readonly HashSet<string> BlockedUrls = new();
    bool loading; public bool Loading { get => loading; set { loading = value; Pn(nameof(Loading), nameof(Glyph), nameof(FavVis), nameof(GlyphVis)); } }
    ImageSource fav; public ImageSource Favicon { get => fav; set { fav = value; if (value == null) Tint = null; Pn(nameof(Favicon), nameof(FavVis), nameof(GlyphVis)); } }
    /// <summary>Dominant colour of the site icon, used to tint the tab; null when there is no icon or it has no clear colour.</summary>
    Color? tint; public Color? Tint { get => tint; set { tint = value; P(nameof(TintBrush)); } }
    public Brush TintBrush => tint is Color c ? new SolidColorBrush(c) : Brushes.Transparent;

    /// <summary>Picks the strongest hue in the icon (weighted by saturation, brightness and opacity) and returns a lifted version of it.</summary>
    public static Color? DominantColor(BitmapSource src)
    {
        try
        {
            var bmp = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = bmp.PixelWidth, h = bmp.PixelHeight, stride = w * 4; var px = new byte[stride * h]; bmp.CopyPixels(px, stride, 0);
            const int Bins = 12; var weight = new double[Bins]; var sr = new double[Bins]; var sg = new double[Bins]; var sb = new double[Bins];
            for (int i = 0; i < px.Length; i += 4)
            {
                double a = px[i + 3] / 255.0, b = px[i] / 255.0, g = px[i + 1] / 255.0, r = px[i + 2] / 255.0;
                double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
                if (a < 0.5 || max < 0.25 || d < 0.2) continue;                 // transparent, near-black or grey/white: no colour information
                double sat = d / max, hue = max == r ? ((g - b) / d + 6) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
                int bin = (int)(hue / 6 * Bins) % Bins; double wt = a * sat * max;
                weight[bin] += wt; sr[bin] += r * wt; sg[bin] += g * wt; sb[bin] += b * wt;
            }
            int best = 0; for (int i = 1; i < Bins; i++) if (weight[i] > weight[best]) best = i;
            if (weight[best] < 1.0) return null;
            double R = sr[best] / weight[best], G = sg[best] / weight[best], B = sb[best] / weight[best];
            double mx = Math.Max(R, Math.Max(G, B)); if (mx > 0 && mx < 0.8) { double k = 0.8 / mx; R = Math.Min(1, R * k); G = Math.Min(1, G * k); B = Math.Min(1, B * k); }
            return Color.FromRgb((byte)(R * 255), (byte)(G * 255), (byte)(B * 255));
        }
        catch { return null; }
    }
    bool pinned; public bool Pinned { get => pinned; set { pinned = value; Pn(nameof(Pinned), nameof(TitleVis), nameof(CloseVis), nameof(TabWidth)); } }
    bool muted; public bool Muted { get => muted; set { muted = value; Pn(nameof(Muted), nameof(MuteVis), nameof(MuteGlyph)); } }
    bool canBack, canFwd; public bool CanBack { get => canBack; set { canBack = value; P(); } } public bool CanFwd { get => canFwd; set { canFwd = value; P(); } }
    double width = 200; public double TabWidth { get => Pinned ? 44 : width; set { width = value; P(); } }

    public string Glyph => Loading ? "⟳" : Private ? "🕶" : "";
    public Visibility FavVis => Favicon != null && !Loading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GlyphVis => FavVis == Visibility.Visible || Glyph == "" ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TitleVis => Pinned ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CloseVis => Pinned ? Visibility.Collapsed : Visibility.Visible;
    public Visibility MuteVis => Muted && !Pinned ? Visibility.Visible : Visibility.Collapsed;
    public string MuteGlyph => Muted ? "🔇" : "🔊";

    static readonly HttpClient http = MakeClient();
    static HttpClient MakeClient() { var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) }; c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36"); return c; }
    public string FaviconB64 = "";
    public async void LoadFavicon(string u, Action<string> done = null)
    {
        try
        {
            var bytes = await http.GetByteArrayAsync(u);
            // .ico files hold several sizes: pick the one closest to 32px instead of letting WPF guess
            var dec = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = dec.Frames.OrderBy(f => Math.Abs(f.PixelWidth - 32)).First();
            frame.Freeze(); ImageSource bi = frame;
            var enc = new PngBitmapEncoder(); enc.Frames.Add(frame);
            using var ms = new MemoryStream(); enc.Save(ms);
            FaviconB64 = Convert.ToBase64String(ms.ToArray());
            Favicon = bi; Tint = DominantColor(frame); done?.Invoke(FaviconB64);
        }
        catch { Favicon = null; FaviconB64 = ""; }
    }
}

public class DownloadEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    void P([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    public int Id; public string Url = "";
    public IDownloadItemCallback Callback;
    string name = ""; public string Name { get => name; set { name = value; P(); P(nameof(FileKind)); } }
    string path = ""; public string Path { get => path; set { path = value; P(); } }
    long rec, tot; double pct; bool done, canceled, paused;
    public long Received { get => rec; set { rec = value; P(); P(nameof(Status)); } }
    public long Total { get => tot; set { tot = value; P(); P(nameof(Status)); } }
    public double Percent { get => pct; set { pct = value; P(); } }
    public bool Done { get => done; set { done = value; P(); P(nameof(Status)); StateChanged(); } }
    public bool Canceled { get => canceled; set { canceled = value; P(); P(nameof(Status)); StateChanged(); } }
    public bool Paused { get => paused; set { paused = value; P(); P(nameof(Status)); P(nameof(PauseKind)); } }
    void StateChanged() { P(nameof(Active)); P(nameof(Finished)); P(nameof(Inactive)); }
    public bool Active => !Done && !Canceled;
    public bool Finished => Done && !Canceled;
    public bool Inactive => !Active;
    public PackIconKind PauseKind => Paused ? PackIconKind.Play : PackIconKind.Pause;
    public PackIconKind FileKind => System.IO.Path.GetExtension(Name ?? "").ToLowerInvariant() switch
    {
        ".pdf" => PackIconKind.FilePdfBox, ".zip" or ".7z" or ".rar" or ".gz" or ".tar" => PackIconKind.FolderZipOutline,
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".bmp" => PackIconKind.FileImageOutline,
        ".mp4" or ".mkv" or ".webm" or ".mov" or ".avi" => PackIconKind.FileVideoOutline, ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" => PackIconKind.FileMusicOutline,
        ".doc" or ".docx" or ".txt" or ".rtf" or ".odt" => PackIconKind.FileDocumentOutline, ".xls" or ".xlsx" or ".csv" => PackIconKind.FileExcelOutline,
        ".exe" or ".msi" => PackIconKind.ApplicationOutline, _ => PackIconKind.FileOutline
    };
    static string Mb(long n) => (n / 1048576.0).ToString("0.0") + " MB";
    public string Status => Canceled ? "Canceled" : Done ? "Completed · " + Mb(Total > 0 ? Total : Received) : Paused ? "Paused" : Total > 0 ? $"{Percent:0}% of {Mb(Total)}" : Mb(Received);
}
