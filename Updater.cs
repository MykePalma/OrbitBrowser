using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace Lite;

public enum UpdatePhase { Idle, Checking, UpToDate, Downloading, Ready, Available, Failed }

public class ReleaseInfo
{
    public Version Version { get; set; } public string Tag { get; set; } = ""; public string Notes { get; set; } = ""; public string PageUrl { get; set; } = "";
    public string AssetUrl { get; set; } = ""; public long Size { get; set; } public string Sha256 { get; set; } = "";
}

/// <summary>What a finished, verified download looks like on disk (update\staged.json).</summary>
public class StagedUpdate { public string Version { get; set; } = ""; public string Dir { get; set; } = ""; public string PageUrl { get; set; } = ""; }

/// <summary>
/// Checks GitHub for a newer Orbit release, downloads it, verifies its SHA-256 and unpacks it into a staging folder.
/// A small script then copies the staged files over the install once Orbit has exited, and starts Orbit again.
/// Nothing is installed unless the checksum published by GitHub matches the downloaded file.
/// </summary>
public static class Updater
{
    public const string AssetName = "Orbit-win-x64.zip";
    /// <summary>Overridable so the self-test can point at a local fake server.</summary>
    public static string ApiBase = "https://api.github.com/repos/MykePalma/OrbitBrowser";
    public static bool AllowInsecureForTests;

    public static Version Current => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Math.Max(0, Current.Build)}";
    public static string AppDir => AppContext.BaseDirectory.TrimEnd('\\');
    public static string WorkDir => Path.Combine(Store.Dir, "update");
    static string StagedFile => Path.Combine(WorkDir, "staged.json");

    // ---- state shared by all windows
    public static UpdatePhase Phase { get; private set; } = UpdatePhase.Idle;
    public static double Progress { get; private set; }
    public static string Message { get; private set; } = "";
    public static ReleaseInfo Latest { get; private set; }
    public static event Action Changed;
    static void Set(UpdatePhase p, string msg = "", double progress = 0) { Phase = p; Message = msg; Progress = progress; UI.Run(() => Changed?.Invoke()); }
    static int busy;

    static readonly HttpClient http = MakeClient();
    static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Orbit-Updater/" + (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0"));
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    // ---------------------------------------------------------------- version logic
    public static bool TryParseVersion(string tag, out Version v)
    {
        v = null; if (string.IsNullOrWhiteSpace(tag)) return false;
        tag = tag.Trim().TrimStart('v', 'V'); int dash = tag.IndexOfAny(new[] { '-', '+' }); if (dash >= 0) tag = tag[..dash];
        if (!Version.TryParse(tag, out var p)) return false;
        v = new Version(p.Major, p.Minor, Math.Max(0, p.Build), Math.Max(0, p.Revision)); return true;
    }
    public static bool IsNewer(Version candidate) => candidate != null && candidate > new Version(Current.Major, Current.Minor, Math.Max(0, Current.Build), Math.Max(0, Current.Revision));

    // ---------------------------------------------------------------- talking to GitHub
    /// <summary>Latest published (non-draft, non-prerelease) release, or null if it has no usable Windows build.</summary>
    public static async Task<ReleaseInfo> FetchLatestAsync()
    {
        if (!ApiBase.StartsWith("https://") && !AllowInsecureForTests) throw new InvalidOperationException("Update source must use https.");
        using var doc = JsonDocument.Parse(await http.GetStringAsync(ApiBase + "/releases/latest"));
        var root = doc.RootElement;
        if (!TryParseVersion(root.GetProperty("tag_name").GetString(), out var ver)) return null;
        var r = new ReleaseInfo { Version = ver, Tag = root.GetProperty("tag_name").GetString(), Notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "", PageUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "" };
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            if (a.GetProperty("name").GetString() != AssetName) continue;
            r.AssetUrl = a.GetProperty("browser_download_url").GetString() ?? ""; r.Size = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            var d = a.TryGetProperty("digest", out var dg) ? dg.GetString() ?? "" : "";
            if (d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) r.Sha256 = d[7..].ToLowerInvariant();
        }
        return r.AssetUrl == "" ? null : r;
    }

    /// <summary>Downloads the release zip, checks its SHA-256 against the one GitHub published, unpacks it and records it as staged.</summary>
    public static async Task<StagedUpdate> DownloadAndStageAsync(ReleaseInfo r, Action<double> progress = null)
    {
        if (string.IsNullOrEmpty(r.Sha256)) throw new InvalidOperationException("The release has no published checksum, so it can't be verified.");
        if (!r.AssetUrl.StartsWith("https://") && !AllowInsecureForTests) throw new InvalidOperationException("Download must use https.");
        var root = Path.Combine(WorkDir, r.Version.ToString(3)); try { Directory.Delete(root, true); } catch { }
        Directory.CreateDirectory(root);
        var zip = Path.Combine(root, AssetName);
        try
        {
            using (var resp = await http.GetAsync(r.AssetUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? r.Size; long got = 0;
                using var sha = SHA256.Create();
                using var src = await resp.Content.ReadAsStreamAsync(); using var dst = File.Create(zip);
                var buf = new byte[1 << 16]; int n; var tick = Environment.TickCount;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n)); sha.TransformBlock(buf, 0, n, null, 0); got += n;
                    if (Environment.TickCount - tick > 200) { tick = Environment.TickCount; if (total > 0) progress?.Invoke((double)got / total); }
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var actual = Convert.ToHexString(sha.Hash).ToLowerInvariant();
                if (actual != r.Sha256) throw new InvalidDataException("The downloaded file doesn't match its published checksum.");
            }
            var files = Path.Combine(root, "files"); ExtractSafely(zip, files);
            var srcDir = FindAppRoot(files) ?? throw new InvalidDataException("The download doesn't contain Orbit.exe.");
            File.Delete(zip);
            var st = new StagedUpdate { Version = r.Version.ToString(3), Dir = srcDir, PageUrl = r.PageUrl };
            File.WriteAllText(StagedFile, JsonSerializer.Serialize(st));
            return st;
        }
        catch { try { Directory.Delete(root, true); } catch { } throw; }
    }

    /// <summary>Unpacks a zip, refusing any entry that would land outside the target folder.</summary>
    public static void ExtractSafely(string zipPath, string target)
    {
        Directory.CreateDirectory(target); var full = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        using var z = ZipFile.OpenRead(zipPath);
        foreach (var e in z.Entries)
        {
            var dest = Path.GetFullPath(Path.Combine(target, e.FullName));
            if (!dest.StartsWith(full, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe path in update: " + e.FullName);
            if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(dest); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)); e.ExtractToFile(dest, true);
        }
    }
    static string FindAppRoot(string dir)
    {
        if (File.Exists(Path.Combine(dir, "Orbit.exe"))) return dir;
        foreach (var d in Directory.GetDirectories(dir)) if (File.Exists(Path.Combine(d, "Orbit.exe"))) return d;
        return null;
    }

    // ---------------------------------------------------------------- staged update on disk
    /// <summary>The verified update waiting to be installed, if one exists and is newer than what is running.</summary>
    public static StagedUpdate Staged()
    {
        try
        {
            if (!File.Exists(StagedFile)) return null;
            var s = JsonSerializer.Deserialize<StagedUpdate>(File.ReadAllText(StagedFile));
            if (s != null && TryParseVersion(s.Version, out var v) && IsNewer(v) && File.Exists(Path.Combine(s.Dir, "Orbit.exe"))) return s;
            DiscardStaged();
        }
        catch { DiscardStaged(); }
        return null;
    }
    public static void DiscardStaged() { try { File.Delete(StagedFile); } catch { } try { Directory.Delete(WorkDir, true); } catch { } }

    /// <summary>True when Orbit can overwrite its own files (it can't if it sits in Program Files and isn't elevated).</summary>
    public static bool CanWriteInstall(string dir = null)
    {
        try { var f = Path.Combine(dir ?? AppDir, ".write-test-" + Guid.NewGuid().ToString("N")); File.WriteAllText(f, "x"); File.Delete(f); return true; } catch { return false; }
    }

    // ---------------------------------------------------------------- installing
    /// <summary>Script that waits for Orbit to exit, copies the staged files over the install, forgets the staged update and starts Orbit again.</summary>
    public static string BuildScript(int pid, string src, string dst, string stagedFile, string workDir, string cleanup, bool relaunch, string exe = "Orbit.exe")
    {
        static string Q(string s) => s.Replace("%", "%%");
        return $@"@echo off
setlocal
:wait
tasklist /FI ""PID eq {pid}"" 2>nul | find ""{pid}"" >nul
if not errorlevel 1 (
  ping -n 2 127.0.0.1 >nul
  goto wait
)
robocopy ""{Q(src)}"" ""{Q(dst)}"" /E /NFL /NDL /NJH /NJS /NP /R:30 /W:1 >nul
set RC=%ERRORLEVEL%
del /Q ""{Q(stagedFile)}"" >nul 2>&1
echo robocopy exit %RC% > ""{Q(workDir)}\last-update.log""
{(relaunch ? $@"start """" ""{Q(dst)}\{exe}""" : "rem relaunch disabled")}
rmdir /S /Q ""{Q(cleanup)}"" >nul 2>&1
exit /b %RC%
";
    }

    /// <summary>Starts the install script (hidden). The caller must exit Orbit right after.</summary>
    public static void LaunchInstaller(StagedUpdate s, bool relaunch = true)
    {
        Directory.CreateDirectory(WorkDir);
        var script = Path.Combine(WorkDir, "apply-update.cmd");
        File.WriteAllText(script, BuildScript(Environment.ProcessId, s.Dir, AppDir, StagedFile, WorkDir, Path.Combine(WorkDir, s.Version), relaunch));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
    }

    // ---------------------------------------------------------------- high level
    /// <summary>Checks for a newer release and, if <paramref name="download"/> is set and the install folder is writable, stages it.</summary>
    public static async Task CheckAsync(bool download)
    {
        if (Interlocked.Exchange(ref busy, 1) == 1) return;
        try
        {
            Set(UpdatePhase.Checking, "Checking for updates…");
            var staged = Staged();
            var r = await FetchLatestAsync();
            Store.D.Settings.LastUpdateCheckUtc = DateTime.UtcNow; Store.Save();
            if (r == null || !IsNewer(r.Version)) { Latest = null; Set(UpdatePhase.UpToDate, $"Orbit {CurrentText} is up to date."); return; }
            Latest = r;
            if (staged != null && staged.Version == r.Version.ToString(3)) { Set(UpdatePhase.Ready, $"Orbit {staged.Version} is ready to install."); return; }
            if (!download || !CanWriteInstall()) { Set(UpdatePhase.Available, $"Orbit {r.Version.ToString(3)} is available."); return; }
            Set(UpdatePhase.Downloading, $"Downloading Orbit {r.Version.ToString(3)}…", 0);
            await DownloadAndStageAsync(r, p => Set(UpdatePhase.Downloading, $"Downloading Orbit {r.Version.ToString(3)}… {p:P0}", p));
            Set(UpdatePhase.Ready, $"Orbit {r.Version.ToString(3)} is ready to install.");
        }
        catch (Exception ex)
        {
            Set(UpdatePhase.Failed, "Update failed: " + (ex is HttpRequestException || ex is TaskCanceledException ? "couldn't reach GitHub."
                : ex is JsonException || ex is KeyNotFoundException || ex is InvalidOperationException && ex.Message.Contains("JSON") ? "GitHub sent an unexpected response." : ex.Message));
        }
        finally { Interlocked.Exchange(ref busy, 0); }
    }

    /// <summary>Whether the automatic check should run now (enabled, not a test run, and not checked in the last few hours).</summary>
    public static bool ShouldAutoCheck() => !Store.IsTestRun && Store.D.Settings.AutoUpdate && (DateTime.UtcNow - Store.D.Settings.LastUpdateCheckUtc).TotalHours >= 3;

    /// <summary>Restarts into the staged update. Returns false if there isn't one.</summary>
    public static bool InstallAndRestart()
    {
        var s = Staged(); if (s == null) return false;
        if (!CanWriteInstall()) { Set(UpdatePhase.Available, "Orbit can't update itself in this folder; download the new version instead."); return false; }
        LaunchInstaller(s); Store.SaveNow(); Application.Current.Shutdown(); return true;
    }
}
