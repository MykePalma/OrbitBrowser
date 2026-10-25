using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Lite;

public class ExtInfo
{
    public string Id = "", Name = "", Version = "", Folder = "";
    public bool Enabled = true;
    /// <summary>Page inside the extension where its settings live (empty when it has none).</summary>
    public string OptionsPage = "";
    public System.Windows.Visibility OptionsVis => OptionsPage != "" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
}

/// <summary>
/// Chromium extensions (unpacked folders in %APPDATA%\Orbit\extensions) loaded into the normal browsing profile.
/// WebView2 runs the extensions but has no extension toolbar or store, so Orbit installs them itself and shows them in Settings.
/// </summary>
public static class Extensions
{
    public static string Dir => Path.Combine(Store.Dir, "extensions");
    public static readonly List<ExtInfo> Loaded = new();
    public static event Action Changed;
    static CoreWebView2Profile profile;
    static Task startTask;

    /// <summary>uBlock Origin is published on GitHub as a ready-to-load Chromium build.</summary>
    public const string UBlockApi = "https://api.github.com/repos/gorhill/uBlock/releases/latest";

    static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) }.Also(c => c.DefaultRequestHeaders.UserAgent.ParseAdd("Orbit"));

    public static bool IsUBlockInstalled => Loaded.Any(e => e.Name.StartsWith("uBlock Origin", StringComparison.OrdinalIgnoreCase) && !e.Name.Contains("Lite"));

    /// <summary>Loads every extension folder into the profile; call once, before the first page is opened.</summary>
    public static Task StartAsync(CoreWebView2Profile p) => startTask ??= Start(p);
    static async Task Start(CoreWebView2Profile p)
    {
        profile = p;
        try { Directory.CreateDirectory(Dir); } catch { }
        foreach (var d in Directory.EnumerateDirectories(Dir))
        {
            var name = Path.GetFileName(d);
            if (name.StartsWith("_") || !File.Exists(Path.Combine(d, "manifest.json"))) continue;   // WebView2 refuses folders that start with an underscore
            try { await AddAsync(d); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("extension " + d + ": " + ex.Message); }
        }
        Changed?.Invoke();
    }

    /// <summary>Adds an unpacked extension that already lives in its final folder (WebView2 reads it from there on every start).</summary>
    public static async Task<ExtInfo> AddAsync(string folder)
    {
        if (profile == null) throw new InvalidOperationException("The browser engine is not ready yet.");
        var ext = await profile.AddBrowserExtensionAsync(folder);
        var info = new ExtInfo { Id = ext.Id, Name = Manifest(folder, "name") is { Length: > 0 } n && !n.StartsWith("__MSG_") ? n : ext.Name, Version = Manifest(folder, "version") ?? "", Folder = folder, Enabled = ext.IsEnabled, OptionsPage = OptionsOf(folder) };
        Loaded.RemoveAll(e => e.Id == info.Id); Loaded.Add(info);
        Changed?.Invoke();
        return info;
    }

    static string OptionsOf(string folder)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var r = doc.RootElement;
            if (r.TryGetProperty("options_ui", out var ui) && ui.TryGetProperty("page", out var pg)) return pg.GetString() ?? "";
            if (r.TryGetProperty("options_page", out var op)) return op.GetString() ?? "";
        }
        catch { }
        return "";
    }

    static string Manifest(string folder, string key)
    {
        try { using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); return doc.RootElement.TryGetProperty(key, out var v) ? v.GetString() : null; }
        catch { return null; }
    }

    /// <summary>Copies a folder the user picked into Orbit's own extensions folder and loads it from there.</summary>
    public static async Task<ExtInfo> InstallFromFolderAsync(string src)
    {
        if (!File.Exists(Path.Combine(src, "manifest.json"))) throw new InvalidDataException("That folder doesn't contain a manifest.json, so it isn't an unpacked extension.");
        var name = (Manifest(src, "name") ?? Path.GetFileName(src)); if (name.StartsWith("__MSG_")) name = Path.GetFileName(src);
        var target = Path.Combine(Dir, Safe(name));
        for (int i = 2; Directory.Exists(target); i++) target = Path.Combine(Dir, Safe(name) + " " + i);
        CopyDir(src, target);
        return await AddAsync(target);
    }

    public static async Task<ExtInfo> InstallFromZipAsync(string zip)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "orbit-ext-" + Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(zip, tmp);
            var root = Directory.EnumerateFiles(tmp, "manifest.json", SearchOption.AllDirectories).OrderBy(f => f.Length).FirstOrDefault()
                ?? throw new InvalidDataException("This file doesn't contain an extension (no manifest.json).");
            return await InstallFromFolderAsync(Path.GetDirectoryName(root));
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    /// <summary>Downloads the latest uBlock Origin (Chromium build) from its GitHub releases and installs it.</summary>
    public static async Task<ExtInfo> InstallUBlockAsync(Action<string> status = null)
    {
        status?.Invoke("Looking for the latest uBlock Origin…");
        using var rel = JsonDocument.Parse(await http.GetStringAsync(UBlockApi));
        string url = null, tag = rel.RootElement.GetProperty("tag_name").GetString();
        foreach (var a in rel.RootElement.GetProperty("assets").EnumerateArray())
            if (a.GetProperty("name").GetString() is { } n && n.EndsWith(".chromium.zip", StringComparison.OrdinalIgnoreCase)) url = a.GetProperty("browser_download_url").GetString();
        if (url == null) throw new InvalidDataException("Couldn't find the Chromium download in the latest uBlock Origin release.");
        status?.Invoke("Downloading uBlock Origin " + tag + "…");
        var zip = Path.Combine(Path.GetTempPath(), "orbit-ubo-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await File.WriteAllBytesAsync(zip, await http.GetByteArrayAsync(url));
            status?.Invoke("Installing…");
            foreach (var old in Loaded.Where(e => e.Name.StartsWith("uBlock Origin") && !e.Name.Contains("Lite")).ToList()) await RemoveAsync(old);
            return await InstallFromZipAsync(zip);
        }
        finally { try { File.Delete(zip); } catch { } }
    }

    public static async Task RemoveAsync(ExtInfo e)
    {
        try
        {
            if (profile != null) foreach (var x in await profile.GetBrowserExtensionsAsync()) if (x.Id == e.Id) await x.RemoveAsync();
        }
        catch { }
        Loaded.Remove(e);
        try { if (e.Folder.StartsWith(Dir, StringComparison.OrdinalIgnoreCase)) Directory.Delete(e.Folder, true); } catch { }
        Changed?.Invoke();
    }

    static string Safe(string n) => string.Concat(n.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '_' && false ? '-' : c)).Trim().TrimStart('_', '.');

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
