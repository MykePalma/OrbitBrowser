using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Web.WebView2.Core;

namespace Lite;

public class ExtInfo
{
    public string Id = "", Name = "", Version = "", Folder = "";
    public bool Enabled = true;
    /// <summary>Page inside the extension where its settings live (empty when it has none).</summary>
    public string OptionsPage = "";
    /// <summary>Pop-up page shown when the toolbar button is clicked (empty when there is none).</summary>
    public string PopupPage = "";
    /// <summary>Where updates come from: "store" (Chrome Web Store, with <see cref="StoreId"/>), "ublock" (GitHub) or "" (added by hand, never updated).</summary>
    public string Source = "", StoreId = "";
    public System.Windows.Media.ImageSource Icon;
    public System.Windows.Visibility OptionsVis => OptionsPage != "" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
}

/// <summary>
/// Chromium extensions (unpacked folders in %APPDATA%\Orbit\extensions) loaded into the browsing profiles.
/// WebView2 runs the extensions but has no extension toolbar, store or updater, so Orbit installs, shows and updates them itself.
/// </summary>
public static class Extensions
{
    public static string Dir => Path.Combine(Store.Dir, "extensions");
    public static readonly List<ExtInfo> Loaded = new();
    public static event Action Changed;
    static readonly List<CoreWebView2Profile> profiles = new();    // the normal profile first, the private one once a private window exists
    static Task startTask, privTask;

    /// <summary>uBlock Origin is published on GitHub as a ready-to-load Chromium build.</summary>
    public const string UBlockApi = "https://api.github.com/repos/gorhill/uBlock/releases/latest";
    const string SidecarName = "orbit-ext.json";

    static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) }.Also(c => c.DefaultRequestHeaders.UserAgent.ParseAdd("Orbit"));
    static readonly JsonDocumentOptions lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static bool IsUBlockInstalled => Loaded.Any(e => e.Source == "ublock" || (e.Name.StartsWith("uBlock Origin", StringComparison.OrdinalIgnoreCase) && !e.Name.Contains("Lite")));

    // ---------------------------------------------------------------- loading
    /// <summary>Loads every extension folder into the normal profile; call before the first page is opened.</summary>
    public static Task StartAsync(CoreWebView2Profile p) => startTask ??= Start(p);
    static async Task Start(CoreWebView2Profile p)
    {
        profiles.Insert(0, p);
        try { Directory.CreateDirectory(Dir); } catch { }
        ApplyPending();
        foreach (var d in Directory.EnumerateDirectories(Dir))
        {
            var name = Path.GetFileName(d);
            if (name.StartsWith("_") || !File.Exists(Path.Combine(d, "manifest.json"))) continue;   // WebView2 refuses folders that start with an underscore
            try { await AddAsync(d); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("extension " + d + ": " + ex.Message); }
        }
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- staged updates
    // An extension that is removed and added again loses its saved data, so a new version is unpacked beside the old one
    // (in extensions\_pending) and swapped in the next time Orbit starts, before anything is loaded.
    static string Pending => Path.Combine(Dir, "_pending");

    /// <summary>Swaps in updates that were staged during the previous run.</summary>
    public static void ApplyPending()
    {
        try
        {
            if (!Directory.Exists(Pending)) return;
            foreach (var d in Directory.GetDirectories(Pending))
            {
                var target = Path.Combine(Dir, Path.GetFileName(d));
                try { if (Directory.Exists(target)) Directory.Delete(target, true); Directory.Move(d, target); } catch { }
            }
            try { Directory.Delete(Pending, true); } catch { }
        }
        catch { }
    }

    /// <summary>Unpacks a new version beside the installed one; it takes over on the next start.</summary>
    static void Stage(string src, string folderName, string source, string storeId)
    {
        var dest = Path.Combine(Pending, Safe(folderName));
        try { Directory.Delete(dest, true); } catch { }
        CopyDir(src, dest);
        File.WriteAllText(Path.Combine(dest, SidecarName), JsonSerializer.Serialize(new { source, storeId }));
        EnsureClickShim(dest);
    }
    public static string StagedVersion(string folderName) => Manifest(Path.Combine(Pending, Safe(folderName)), "version");
    /// <summary>For tests: stages an update from a folder exactly as a download would.</summary>
    public static void StageFromFolder(string src, string folderName, string source = "") => Stage(src, folderName, source, "");

    /// <summary>Adds an unpacked extension that already lives in its final folder (WebView2 reads it from there on every start).</summary>
    public static async Task<ExtInfo> AddAsync(string folder)
    {
        if (profiles.Count == 0) throw new InvalidOperationException("The browser engine is not ready yet.");
        EnsureClickShim(folder);
        CoreWebView2BrowserExtension ext = null;
        foreach (var p in profiles.ToList()) { var e = await p.AddBrowserExtensionAsync(folder); ext ??= e; }
        var info = new ExtInfo { Id = ext.Id, Name = Manifest(folder, "name") is { Length: > 0 } n && !n.StartsWith("__MSG_") ? n : ext.Name, Version = Manifest(folder, "version") ?? "", Folder = folder, Enabled = ext.IsEnabled, OptionsPage = OptionsOf(folder) };
        try
        {
            var side = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, SidecarName)));
            info.Source = side?["source"]?.GetValue<string>() ?? ""; info.StoreId = side?["storeId"]?.GetValue<string>() ?? "";
        }
        catch { }
        ReadToolbar(info);
        Loaded.RemoveAll(e => e.Id == info.Id); Loaded.Add(info);
        Changed?.Invoke();
        return info;
    }

    static string OptionsOf(string folder)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")), lenient);
            var r = doc.RootElement;
            if (r.TryGetProperty("options_ui", out var ui) && ui.TryGetProperty("page", out var pg)) return pg.GetString() ?? "";
            if (r.TryGetProperty("options_page", out var op)) return op.GetString() ?? "";
        }
        catch { }
        return "";
    }

    /// <summary>Reads the toolbar icon and pop-up page from the manifest (action for Manifest V3, browser_action / page_action for V2).</summary>
    static void ReadToolbar(ExtInfo info)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(info.Folder, "manifest.json")), lenient);
            var r = doc.RootElement; string icon = null; var sizes = new List<(int, string)>();
            void AddIcons(JsonElement e)
            {
                if (e.ValueKind == JsonValueKind.String) icon ??= e.GetString();
                else if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) if (int.TryParse(p.Name, out var n) && p.Value.ValueKind == JsonValueKind.String) sizes.Add((n, p.Value.GetString()));
            }
            foreach (var key in new[] { "action", "browser_action", "page_action" })
                if (r.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Object)
                {
                    if (a.TryGetProperty("default_popup", out var pp) && pp.ValueKind == JsonValueKind.String && info.PopupPage == "") info.PopupPage = pp.GetString() ?? "";
                    if (a.TryGetProperty("default_icon", out var di)) AddIcons(di);
                }
            if (sizes.Count == 0 && icon == null && r.TryGetProperty("icons", out var ic)) AddIcons(ic);
            var path = sizes.Count > 0 ? sizes.OrderBy(s => Math.Abs(s.Item1 - 32)).First().Item2 : icon;
            if (path == null) return;
            var file = Path.Combine(info.Folder, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file) || file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return;
            var bi = new System.Windows.Media.Imaging.BitmapImage(); bi.BeginInit(); bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bi.UriSource = new Uri(file); bi.EndInit(); bi.Freeze();
            info.Icon = bi;
        }
        catch { }
    }

    static string Manifest(string folder, string key)
    {
        try { using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")), lenient); return doc.RootElement.TryGetProperty(key, out var v) ? v.GetString() : null; }
        catch { return null; }
    }

    // ---------------------------------------------------------------- toolbar clicks for extensions without a pop-up
    // WebView2 can't deliver chrome.action.onClicked, so the extension's background script gets a tiny shim that remembers its
    // click listeners, and Orbit calls them itself (through the DevTools protocol) when the toolbar button is pressed.
    const string ShimJs = "(function(g){g.__orbitClick=g.__orbitClick||[];['action','browserAction','pageAction'].forEach(function(k){try{var a=g.chrome&&g.chrome[k];var ev=a&&a.onClicked;if(ev&&ev.addListener&&!ev.__orbit){var o=ev.addListener.bind(ev);ev.addListener=function(f){g.__orbitClick.push(f);return o(f)};ev.__orbit=1}}catch(e){}})})(self);\n";

    static void EnsureClickShim(string folder)
    {
        try
        {
            var mf = Path.Combine(folder, "manifest.json");
            var root = JsonNode.Parse(File.ReadAllText(mf), null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
            if (root == null || root["background"] is not JsonObject bg) return;
            bool hasButton = root["action"] != null || root["browser_action"] != null || root["page_action"] != null;
            if (!hasButton) return;
            File.WriteAllText(Path.Combine(folder, "orbit-shim.js"), ShimJs);
            File.WriteAllText(Path.Combine(folder, "orbit-wake.js"), "chrome.runtime.sendMessage({orbitWake:1},function(){void chrome.runtime.lastError});\n");
            File.WriteAllText(Path.Combine(folder, "orbit-wake.html"), "<!doctype html><meta charset=utf-8><script src=\"orbit-wake.js\"></script>\n");
            if (bg["service_worker"] is JsonValue sw && sw.GetValue<string>() is { } orig)
            {
                if (orig == "orbit-sw.js") return;
                bool module = bg["type"]?.GetValue<string>() == "module";
                File.WriteAllText(Path.Combine(folder, "orbit-sw.js"), module ? $"import \"./orbit-shim.js\";\nimport \"./{orig.TrimStart('/')}\";\n" : $"importScripts(\"orbit-shim.js\", \"{orig.TrimStart('/')}\");\n");
                bg["service_worker"] = "orbit-sw.js";
            }
            else if (bg["scripts"] is JsonArray scripts && !scripts.Any(s => s?.GetValue<string>() == "orbit-shim.js")) scripts.Insert(0, "orbit-shim.js");
            else return;
            File.WriteAllText(mf, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>Calls the extension's toolbar-click listeners with the page the user is looking at. Returns how many listeners ran (0 = none, -1 = the extension's background isn't running).</summary>
    public static async Task<int> TriggerClickAsync(CoreWebView2 core, ExtInfo x, string activeUrl, string avoidContext = null)
    {
        try
        {
            var tg = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Target.getTargets", "{}")).RootElement.GetProperty("targetInfos");
            // normal and private windows each run their own copy of the extension: use the one that belongs to this page's profile
            // (a private page's own context differs from its extension copy's, so for private windows the normal profile's copy is ruled out instead)
            string ctx = null; try { ctx = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Target.getTargetInfo", "{}")).RootElement.GetProperty("targetInfo").GetProperty("browserContextId").GetString(); } catch { }
            string tid = null;
            foreach (var t in tg.EnumerateArray())
                if ((t.GetProperty("type").GetString() is "background_page" or "service_worker") && t.GetProperty("url").GetString().StartsWith("chrome-extension://" + x.Id + "/") && (avoidContext != null ? t.GetProperty("browserContextId").GetString() != avoidContext : ctx == null || t.GetProperty("browserContextId").GetString() == ctx)) tid = t.GetProperty("targetId").GetString();
            if (tid == null) return -1;
            var sid = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Target.attachToTarget", JsonSerializer.Serialize(new { targetId = tid, flatten = true }))).RootElement.GetProperty("sessionId").GetString();
            try
            {
                var js = "(async function(){var L=self.__orbitClick||[];if(!L.length)return 0;var all=await chrome.tabs.query({});var u=" + JsonSerializer.Serialize(activeUrl) + ";var pv=" + (avoidContext != null ? "true" : "false") + ";var t=all.find(function(x){return x.url===u&&!!x.incognito===pv})||all.find(function(x){return x.url===u})||all[0];t=Object.assign({},t,{active:true});for(var i=0;i<L.length;i++){try{L[i](t)}catch(e){}}return L.length})()";
                var res = await core.CallDevToolsProtocolMethodForSessionAsync(sid, "Runtime.evaluate", JsonSerializer.Serialize(new { expression = js, awaitPromise = true, returnByValue = true }));
                var v = JsonDocument.Parse(res).RootElement.GetProperty("result");
                return v.TryGetProperty("value", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0;
            }
            finally { try { await core.CallDevToolsProtocolMethodAsync("Target.detachFromTarget", JsonSerializer.Serialize(new { sessionId = sid })); } catch { } }
        }
        catch { return -1; }
    }

    /// <summary>The DevTools browser-context id of a page view (pages and extension copies of the normal profile share it).</summary>
    public static async Task<string> ContextOf(CoreWebView2 core)
    {
        try { return JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Target.getTargetInfo", "{}")).RootElement.GetProperty("targetInfo").GetProperty("browserContextId").GetString(); } catch { return null; }
    }

    // ---------------------------------------------------------------- installing
    /// <summary>The 32-letter id of an extension from a Chrome Web Store address, or null for any other address.</summary>
    public static string StoreId(string url)
    {
        if (url == null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(url, @"^https://(chromewebstore\.google\.com/detail/|chrome\.google\.com/webstore/detail/)(?:[^/?#]*/)?([a-p]{32})(?:[/?#]|$)");
        return m.Success ? m.Groups[2].Value : null;
    }

    static string ProdVersion => string.IsNullOrEmpty(App.EngineVersion) ? "150.0.0.0" : App.EngineVersion;

    /// <summary>Downloads an extension from the Chrome Web Store's update service, unpacks the .crx and installs it (replacing an older copy).</summary>
    public static async Task<ExtInfo> InstallFromStoreAsync(string id, Action<string> status = null, bool stage = false)
    {
        status?.Invoke("Downloading the extension…");
        var crx = await http.GetByteArrayAsync($"https://clients2.google.com/service/update2/crx?response=redirect&prodversion={ProdVersion}&acceptformat=crx2,crx3&x=id%3D{id}%26uc");
        // a .crx is a small header followed by an ordinary zip file
        int start;
        if (crx.Length > 16 && crx[0] == 'C' && crx[1] == 'r' && crx[2] == '2' && crx[3] == '4')
        {
            int ver3 = BitConverter.ToInt32(crx, 4);
            start = ver3 == 2 ? 16 + BitConverter.ToInt32(crx, 8) + BitConverter.ToInt32(crx, 12) : 12 + BitConverter.ToInt32(crx, 8);
        }
        else if (crx.Length > 4 && crx[0] == 'P' && crx[1] == 'K') start = 0;
        else throw new InvalidDataException("The Chrome Web Store didn't return an extension for this address.");
        status?.Invoke("Installing…");
        var tmp = Path.Combine(Path.GetTempPath(), "orbit-crx-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var ms = new MemoryStream(crx, start, crx.Length - start)) ZipFile.ExtractToDirectory(ms, tmp);
            try { Directory.Delete(Path.Combine(tmp, "_metadata"), true); } catch { }   // reserved folder: WebView2 refuses to load it
            if (stage) { Stage(tmp, id, "store", id); return null; }
            return await InstallFromFolderAsync(tmp, id, "store", id);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    /// <summary>Copies a folder into Orbit's own extensions folder and loads it from there. With a fixed <paramref name="folderName"/> an older copy is replaced.</summary>
    public static async Task<ExtInfo> InstallFromFolderAsync(string src, string folderName = null, string source = "", string storeId = "")
    {
        if (!File.Exists(Path.Combine(src, "manifest.json"))) throw new InvalidDataException("That folder doesn't contain a manifest.json, so it isn't an unpacked extension.");
        var name = (Manifest(src, "name") ?? Path.GetFileName(src)); if (name.StartsWith("__MSG_")) name = Path.GetFileName(src); if (folderName != null) name = folderName;
        var target = Path.Combine(Dir, Safe(name));
        if (folderName != null) { foreach (var old in Loaded.Where(e => string.Equals(e.Folder, target, StringComparison.OrdinalIgnoreCase)).ToList()) await UnloadAsync(old); try { Directory.Delete(target, true); } catch { } }
        for (int i = 2; Directory.Exists(target); i++) target = Path.Combine(Dir, Safe(name) + " " + i);
        CopyDir(src, target);
        if (source != "") File.WriteAllText(Path.Combine(target, SidecarName), JsonSerializer.Serialize(new { source, storeId }));
        return await AddAsync(target);
    }

    public static async Task<ExtInfo> InstallFromZipAsync(string zip, string folderName = null, string source = "")
    {
        var tmp = Path.Combine(Path.GetTempPath(), "orbit-ext-" + Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(zip, tmp);
            var root = Directory.EnumerateFiles(tmp, "manifest.json", SearchOption.AllDirectories).OrderBy(f => f.Length).FirstOrDefault()
                ?? throw new InvalidDataException("This file doesn't contain an extension (no manifest.json).");
            return await InstallFromFolderAsync(Path.GetDirectoryName(root), folderName, source);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    /// <summary>Downloads the latest uBlock Origin (Chromium build) from its GitHub releases and installs it.</summary>
    public static async Task<ExtInfo> InstallUBlockAsync(Action<string> status = null, bool stage = false)
    {
        status?.Invoke("Looking for the latest uBlock Origin…");
        var (tag, url) = await LatestUBlock();
        status?.Invoke("Downloading uBlock Origin " + tag + "…");
        var zip = Path.Combine(Path.GetTempPath(), "orbit-ubo-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await File.WriteAllBytesAsync(zip, await http.GetByteArrayAsync(url));
            status?.Invoke("Installing…");
            if (stage)
            {
                var tmp = Path.Combine(Path.GetTempPath(), "orbit-ubo-" + Guid.NewGuid().ToString("N"));
                try { ZipFile.ExtractToDirectory(zip, tmp); var root = Directory.EnumerateFiles(tmp, "manifest.json", SearchOption.AllDirectories).OrderBy(f => f.Length).First(); Stage(Path.GetDirectoryName(root), "uBlock Origin", "ublock", ""); }
                finally { try { Directory.Delete(tmp, true); } catch { } }
                return null;
            }
            return await InstallFromZipAsync(zip, "uBlock Origin", "ublock");
        }
        finally { try { File.Delete(zip); } catch { } }
    }

    static async Task<(string tag, string url)> LatestUBlock()
    {
        using var rel = JsonDocument.Parse(await http.GetStringAsync(UBlockApi));
        string url = null, tag = rel.RootElement.GetProperty("tag_name").GetString();
        foreach (var a in rel.RootElement.GetProperty("assets").EnumerateArray())
            if (a.GetProperty("name").GetString() is { } n && n.EndsWith(".chromium.zip", StringComparison.OrdinalIgnoreCase)) url = a.GetProperty("browser_download_url").GetString();
        if (url == null) throw new InvalidDataException("Couldn't find the Chromium download in the latest uBlock Origin release.");
        return (tag, url);
    }

    // ---------------------------------------------------------------- updates
    static bool Newer(string latest, string current)
    {
        static Version P(string s) { s = (s ?? "").TrimStart('v', 'V'); var parts = s.Split('.').Take(4).Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0).ToList(); while (parts.Count < 3) parts.Add(0); return new Version(string.Join('.', parts)); }
        try { return P(latest) > P(current); } catch { return false; }
    }

    /// <summary>Looks for newer versions of the extensions that came from the Chrome Web Store or GitHub and installs them. Returns the names of the ones with a new version, which takes over the next time Orbit starts.</summary>
    public static async Task<List<string>> UpdateAllAsync()
    {
        var done = new List<string>();
        foreach (var x in Loaded.ToList())
        {
            try
            {
                if (x.Source == "store" && x.StoreId != "")
                {
                    var xml = await http.GetStringAsync($"https://clients2.google.com/service/update2/crx?response=updatecheck&prodversion={ProdVersion}&acceptformat=crx3&x=id%3D{x.StoreId}%26v%3D{x.Version}%26uc");
                    var uc = XDocument.Parse(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == "updatecheck");
                    if (uc?.Attribute("status")?.Value == "ok" && uc.Attribute("version")?.Value is { } v && Newer(v, x.Version) && !(StagedVersion(x.StoreId) is { } sv && !Newer(v, sv))) { await InstallFromStoreAsync(x.StoreId, null, true); done.Add(x.Name); }
                }
                else if (x.Source == "ublock")
                {
                    var (tag, _) = await LatestUBlock();
                    if (Newer(tag, x.Version) && !(StagedVersion("uBlock Origin") is { } sv && !Newer(tag, sv))) { await InstallUBlockAsync(null, true); done.Add(x.Name); }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("extension update " + x.Name + ": " + ex.Message); }
        }
        return done;
    }

    // ---------------------------------------------------------------- removing
    /// <summary>Takes the extension out of every profile but leaves its files.</summary>
    static async Task UnloadAsync(ExtInfo e)
    {
        foreach (var p in profiles.ToList())
            try { foreach (var x in await p.GetBrowserExtensionsAsync()) if (x.Id == e.Id) await x.RemoveAsync(); } catch { }
        Loaded.Remove(e);
    }

    public static async Task RemoveAsync(ExtInfo e)
    {
        await UnloadAsync(e);
        try { if (e.Folder.StartsWith(Dir, StringComparison.OrdinalIgnoreCase)) Directory.Delete(e.Folder, true); } catch { }
        Changed?.Invoke();
    }

    static string Safe(string n) => string.Concat(n.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim().TrimStart('_', '.');

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
