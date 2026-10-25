using System.IO;
using System.Net.Http;

namespace Lite;

public class FilterListInfo
{
    public string Id = "", Name = "", Description = "", Url = "", File = "";
    public Func<Settings, bool> Enabled = _ => true;
}

/// <summary>
/// Orbit's ad and tracker blocker. It keeps the downloaded filter lists (EasyList for ads, EasyPrivacy for trackers) in
/// %APPDATA%\Orbit\filters, turns them into a <see cref="FilterEngine"/>, refreshes them every few days and lets the user
/// pause blocking on individual sites.
/// </summary>
public static class AdBlocker
{
    public static readonly FilterListInfo[] Lists =
    {
        new() { Id = "easylist", Name = "Ads (EasyList)", Description = "Blocks adverts and hides the empty boxes they leave behind.", Url = "https://easylist.to/easylist/easylist.txt", File = "easylist.txt", Enabled = s => s.BlockAds },
        new() { Id = "easyprivacy", Name = "Trackers (EasyPrivacy)", Description = "Blocks scripts and pixels that follow you from site to site.", Url = "https://easylist.to/easylist/easyprivacy.txt", File = "easyprivacy.txt", Enabled = s => s.BlockTrackers },
    };

    /// <summary>Diagnostics only: when set (self-test), every request and the decision taken is appended here.</summary>
    public static System.Collections.Concurrent.ConcurrentQueue<string> Trace;

    public static string Dir => Path.Combine(Store.Dir, "filters");
    static FilterEngine engine = new FilterEngine().Also(e => e.AddList(FilterEngine.BuiltIn)).Seal();
    /// <summary>The rules currently in force.</summary>
    public static FilterEngine Engine => engine;
    public static void UseEngine(FilterEngine e) { engine = e; Changed?.Invoke(); }

    // ---- status shown in Settings
    public static bool Busy { get; private set; }
    public static string Message { get; private set; } = "";
    public static event Action Changed;
    static void Report(string msg, bool busy) { Message = msg; Busy = busy; UI.Run(() => Changed?.Invoke()); }
    static readonly HttpClient http = MakeClient();
    static HttpClient MakeClient() { var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) }; c.DefaultRequestHeaders.UserAgent.ParseAdd("Orbit-FilterUpdater/1.0"); return c; }
    static int working;

    // ---- per-site pause
    public static bool IsPaused(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        var allow = Store.D.Settings.AdAllow; if (allow.Count == 0) return false;
        for (var h = host.ToLowerInvariant(); h.Length > 0;) { if (allow.Contains(h)) return true; int dot = h.IndexOf('.'); if (dot < 0) break; h = h[(dot + 1)..]; }
        return false;
    }
    public static void SetPaused(string host, bool paused)
    {
        host = (host ?? "").ToLowerInvariant(); if (host.Length == 0) return;
        var allow = Store.D.Settings.AdAllow;
        if (paused) { if (!allow.Contains(host)) allow.Add(host); }
        else { allow.RemoveAll(a => a == host || host.EndsWith("." + a)); }
        Store.Save(); Changed?.Invoke();
    }

    /// <summary>Whether a request or page on this host is currently subject to blocking at all.</summary>
    public static bool Active(string host) => Store.D.Settings.AdBlock && !IsPaused(host);

    // ---- building the engine
    static string CachePath(FilterListInfo l) => Path.Combine(Dir, l.File);
    public static (int network, int cosmetic, int lists, DateTime? newest) Summary()
    {
        var e = engine; int n = 0; DateTime? newest = null;
        foreach (var l in Lists) if (File.Exists(CachePath(l))) { n++; var t = File.GetLastWriteTimeUtc(CachePath(l)); if (newest == null || t > newest) newest = t; }
        return (e.NetworkRules + e.ExceptionRules, e.CosmeticRules, n, newest);
    }

    /// <summary>Parses the cached lists that are switched on (plus Orbit's built-in rules) into a new engine.</summary>
    public static FilterEngine Build()
    {
        var e = new FilterEngine(); e.AddList(FilterEngine.BuiltIn);
        foreach (var l in Lists)
            if (l.Enabled(Store.D.Settings) && File.Exists(CachePath(l))) { try { e.AddList(File.ReadAllText(CachePath(l))); } catch { } }
        return e.Seal();
    }

    /// <summary>Rebuilds the engine from the cached lists (call after a list is switched on or off).</summary>
    public static Task RebuildAsync() => Task.Run(() =>
    {
        var sw = System.Diagnostics.Stopwatch.StartNew(); var e = Build(); engine = e;
        Report(Describe(), false);
        _ = sw;
    });

    static string Describe()
    {
        var (n, c, lists, newest) = Summary();
        if (lists == 0) return "Using Orbit's basic built-in rules. Filter lists haven't been downloaded yet.";
        var age = newest == null ? "" : (DateTime.UtcNow - newest.Value).TotalDays < 1 ? "updated today" : $"updated {(int)(DateTime.UtcNow - newest.Value).TotalDays} day(s) ago";
        return $"{n:N0} blocking rules and {c:N0} page-cleaning rules · {age}";
    }

    public static bool ShouldRefresh() => !Store.IsTestRun && Store.D.Settings.AdBlock && (DateTime.UtcNow - Store.D.Settings.LastFilterUpdateUtc).TotalDays >= 4;

    /// <summary>Start-up: load whatever is cached, then download or refresh the lists if they are missing or older than four days.</summary>
    public static async Task InitAsync()
    {
        if (Interlocked.Exchange(ref working, 1) == 1) return;
        try
        {
            Report("Loading filter lists…", true);
            await RebuildAsync();
            bool missing = Lists.Any(l => l.Enabled(Store.D.Settings) && !File.Exists(CachePath(l)));
            if (Store.D.Settings.AdBlock && (missing || ShouldRefresh())) { Interlocked.Exchange(ref working, 0); await UpdateAsync(); }
        }
        finally { Interlocked.Exchange(ref working, 0); if (Busy) Report(Describe(), false); }
    }

    /// <summary>Downloads the enabled lists, keeping the old copy of any list that can't be fetched.</summary>
    public static async Task<bool> UpdateAsync()
    {
        if (Interlocked.Exchange(ref working, 1) == 1) return false;
        bool ok = true;
        try
        {
            Directory.CreateDirectory(Dir);
            foreach (var l in Lists.Where(l => l.Enabled(Store.D.Settings)))
            {
                Report($"Downloading {l.Name}…", true);
                try
                {
                    var text = await http.GetStringAsync(l.Url);
                    if (text.Length < 2000 || !(text.StartsWith("[Adblock") || text.StartsWith("!"))) throw new InvalidDataException("Not a filter list");
                    var tmp = CachePath(l) + ".tmp"; await File.WriteAllTextAsync(tmp, text); File.Move(tmp, CachePath(l), true);
                }
                catch (Exception) { ok = false; }
            }
            if (ok) { Store.D.Settings.LastFilterUpdateUtc = DateTime.UtcNow; Store.Save(); }
            Report("Applying filter lists…", true);
            var e = await Task.Run(Build); engine = e;
            Report(ok ? Describe() : "Couldn't download every list, so the last saved copy is being used. " + Describe(), false);
        }
        finally { Interlocked.Exchange(ref working, 0); }
        return ok;
    }
}
