using System.Text;

namespace Lite;

/// <summary>Kinds of request a filter can be limited to (the options of an Adblock Plus rule).</summary>
[Flags]
public enum ReqType
{
    None = 0, Document = 1, SubDocument = 2, Stylesheet = 4, Script = 8, Image = 16, Font = 32, Object = 64, Media = 128, Xhr = 256, Ping = 512, Other = 1024, WebSocket = 2048,
    All = SubDocument | Stylesheet | Script | Image | Font | Object | Media | Xhr | Ping | Other | WebSocket
}

/// <summary>One network filter, e.g. <c>||ads.example.com^$third-party,script</c>.</summary>
public sealed class FilterRule
{
    public string Pattern = "";                 // lower-case unless MatchCase
    public string[] Parts = Array.Empty<string>();   // Pattern split at '*'
    public bool AnchorStart, AnchorEnd, AnchorDomain, MatchCase, ThirdParty, FirstParty, Important, Exception, DocumentException;
    public ReqType Types = ReqType.All;
    public string[] DomainsInc = Array.Empty<string>(), DomainsExc = Array.Empty<string>();
}

/// <summary>A "hide this element" rule (<c>##.ad-banner</c>), optionally limited to some domains.</summary>
public sealed class CosmeticRule { public string Selector = ""; public string[] Exc = Array.Empty<string>(); }

/// <summary>
/// A small ad-blocking engine for the Adblock Plus filter syntax used by EasyList, EasyPrivacy and most other lists:
/// <c>||domain^</c> anchors, <c>|</c> start/end anchors, <c>*</c> and <c>^</c> wildcards, exceptions (<c>@@</c>), the common options
/// (<c>third-party</c>, resource types, <c>domain=</c>, <c>important</c>, <c>match-case</c>) and element hiding (<c>##</c>, <c>#@#</c>).
/// Rules that need features it doesn't implement (regexes, redirects, scriptlets, procedural cosmetics...) are skipped and counted.
/// Instances are built once and then read-only, so they can be shared between threads.
/// </summary>
public sealed class FilterEngine
{
    // ------------------------------------------------------------------ statistics
    public int NetworkRules { get; private set; }
    public int ExceptionRules { get; private set; }
    public int CosmeticRules { get; private set; }
    public int Skipped { get; private set; }

    // ------------------------------------------------------------------ rule storage
    sealed class RuleSet
    {
        public readonly Dictionary<string, List<FilterRule>> ByHost = new(StringComparer.Ordinal);
        public readonly Dictionary<string, List<FilterRule>> ByToken = new(StringComparer.Ordinal);
        public readonly List<FilterRule> Generic = new();

        public void Add(FilterRule r)
        {
            if (r.AnchorDomain && TryHostKey(r.Pattern, out var host)) { Push(ByHost, host, r); return; }
            if (TryToken(r, out var tok)) { Push(ByToken, tok, r); return; }
            Generic.Add(r);
        }
        static void Push(Dictionary<string, List<FilterRule>> d, string k, FilterRule r) { if (!d.TryGetValue(k, out var l)) d[k] = l = new List<FilterRule>(2); l.Add(r); }

        /// <summary>For <c>||host/path</c> style rules: the host part, provided it ends at a real boundary (^ / : ?).</summary>
        static bool TryHostKey(string p, out string host)
        {
            host = "";
            int i = 0; while (i < p.Length && (char.IsAsciiLetterOrDigit(p[i]) || p[i] == '.' || p[i] == '-' || p[i] == '_')) i++;
            if (i < 3 || i >= p.Length || "^/:?".IndexOf(p[i]) < 0) return false;
            var h = p[..i]; if (h.IndexOf('.') <= 0 || h.StartsWith('.') || h.EndsWith('.')) return false;
            host = h; return true;
        }

        /// <summary>Pick the longest alphanumeric run in the pattern that must appear as a whole token in any matching URL.</summary>
        static bool TryToken(FilterRule r, out string token)
        {
            token = ""; var p = r.Pattern; int best = 0, bs = 0;
            for (int s = 0; s < p.Length;)
            {
                if (!char.IsAsciiLetterOrDigit(p[s])) { s++; continue; }
                int e = s; while (e < p.Length && char.IsAsciiLetterOrDigit(p[e])) e++;
                bool left = s == 0 ? (r.AnchorStart || r.AnchorDomain) : p[s - 1] != '*';
                bool right = e == p.Length ? r.AnchorEnd : p[e] != '*';
                if (left && right && e - s >= 3 && e - s > best) { best = e - s; bs = s; }
                s = e;
            }
            if (best == 0) return false; token = p.Substring(bs, best).ToLowerInvariant(); return true;
        }
    }

    readonly RuleSet block = new(), allow = new();
    readonly HashSet<string> documentAllow = new(StringComparer.Ordinal);           // @@||site^$document: never block on these sites

    // cosmetic filtering
    readonly List<string> genericHide = new();                    // selectors for every page
    readonly List<CosmeticRule> genericScoped = new();           // every page except some domains
    readonly Dictionary<string, List<CosmeticRule>> hideByDomain = new(StringComparer.Ordinal);
    readonly HashSet<string> genericUnhide = new(StringComparer.Ordinal);
    readonly Dictionary<string, HashSet<string>> unhideByDomain = new(StringComparer.Ordinal);
    string genericCss = "";

    // ------------------------------------------------------------------ parsing
    /// <summary>Adds every rule found in a filter list (one rule per line).</summary>
    public void AddList(string text)
    {
        int pos = 0;
        while (pos < text.Length)
        {
            int nl = text.IndexOf('\n', pos); if (nl < 0) nl = text.Length;
            var line = text.AsSpan(pos, nl - pos).Trim(); pos = nl + 1;
            if (line.Length > 0) AddLine(line.ToString());
        }
    }
    /// <summary>Call once after the last <see cref="AddList"/>.</summary>
    public FilterEngine Seal()
    {
        var sb = new StringBuilder();
        foreach (var s in genericHide.Distinct()) if (!genericUnhide.Contains(s)) sb.Append(s).Append("{display:none!important}");
        genericCss = sb.ToString(); return this;
    }

    void AddLine(string line)
    {
        if (line[0] == '!' || line[0] == '[') return;                       // comment / list header
        if (TryCosmetic(line)) return;
        var r = ParseNetwork(line);
        if (r == null) { Skipped++; return; }
        if (r.Exception)
        {
            ExceptionRules++;
            if (r.DocumentException) { if (r.AnchorDomain) { var h = HostOf(r.Pattern); if (h != null) documentAllow.Add(h); } return; }
            allow.Add(r);
        }
        else { NetworkRules++; block.Add(r); }
    }

    static readonly string[] UnsupportedCosmetic = { ":-abp-", ":has-text(", ":contains(", ":xpath(", ":matches-", ":upward(", ":remove(", ":style(", ":nth-ancestor(", ":watch-attr(", ":min-text-length(", ":others(", "+js(" };

    bool TryCosmetic(string line)
    {
        int i = line.IndexOf('#'); if (i < 0) return false;
        // the markers: ## (hide) and #@# (un-hide); #?# #$# #%# and friends are procedural/scriptlet forms we don't run
        string marker = null;
        for (int k = i; k < line.Length - 1 && k < i + 6; k++)
            if (line[k] == '#') { foreach (var m in new[] { "#@?#", "#@$#", "#@%#", "#@#", "#?#", "#$#", "#%#", "##" }) if (string.CompareOrdinal(line, k, m, 0, m.Length) == 0) { marker = m; i = k; goto found; } }
        return false;
    found:
        if (marker != "##" && marker != "#@#") { Skipped++; return true; }
        var sel = line[(i + marker.Length)..].Trim();
        if (sel.Length == 0 || UnsupportedCosmetic.Any(u => sel.Contains(u, StringComparison.Ordinal))) { Skipped++; return true; }
        var inc = new List<string>(); var exc = new List<string>();
        foreach (var d in line[..i].Split(',', StringSplitOptions.RemoveEmptyEntries)) { var x = d.Trim().ToLowerInvariant(); if (x.StartsWith('~')) exc.Add(x[1..]); else inc.Add(x); }
        if (marker == "#@#")
        {
            if (inc.Count == 0) genericUnhide.Add(sel);
            else foreach (var d in inc) { if (!unhideByDomain.TryGetValue(d, out var set)) unhideByDomain[d] = set = new HashSet<string>(StringComparer.Ordinal); set.Add(sel); }
            return true;
        }
        CosmeticRules++;
        if (inc.Count == 0 && exc.Count == 0) genericHide.Add(sel);
        else if (inc.Count == 0) genericScoped.Add(new CosmeticRule { Selector = sel, Exc = exc.ToArray() });
        else foreach (var d in inc) { if (!hideByDomain.TryGetValue(d, out var l)) hideByDomain[d] = l = new List<CosmeticRule>(); l.Add(new CosmeticRule { Selector = sel, Exc = exc.ToArray() }); }
        return true;
    }

    // options that make a rule unusable here (listed for documentation; any unknown option is skipped the same way)
    static readonly string[] SkipOptions = { "popup", "popunder", "csp", "redirect", "redirect-rule", "removeparam", "queryprune", "badfilter", "denyallow", "header", "replace", "permissions", "cname", "ghide", "generichide", "elemhide", "specifichide", "inline-script", "inline-font", "empty", "mp4", "to", "from", "uritransform", "urltransform", "rewrite", "all-urls" };

    /// <summary>True when the text after a '$' reads like an option list ("third-party,script", "domain=a.com|b.com", "csp=script-src 'none'").</summary>
    static bool LooksLikeOptions(string o)
    {
        if (o.Length == 0) return false;
        int i = 0; if (o[0] == '~') i++;
        int start = i; while (i < o.Length && (char.IsAsciiLetterOrDigit(o[i]) || o[i] == '-' || o[i] == '_')) i++;
        if (i == start || !char.IsAsciiLetter(o[start])) return false;
        return i == o.Length || o[i] == ',' || o[i] == '=';
    }

    internal static FilterRule ParseNetwork(string line)
    {
        var r = new FilterRule();
        if (line.StartsWith("@@")) { r.Exception = true; line = line[2..]; }
        string opts = null;
        int dollar = line.LastIndexOf('$');
        if (dollar >= 0)
        {
            var o = line[(dollar + 1)..];
            if (LooksLikeOptions(o)) { opts = o; line = line[..dollar]; }
        }
        if (line.Length >= 2 && line[0] == '/' && line[^1] == '/') return null;            // regular-expression rule
        if (line.StartsWith("||")) { r.AnchorDomain = true; line = line[2..]; } else if (line.StartsWith('|')) { r.AnchorStart = true; line = line[1..]; }
        if (line.EndsWith('|')) { r.AnchorEnd = true; line = line[..^1]; }

        ReqType inc = ReqType.None, exc = ReqType.None; var dInc = new List<string>(); var dExc = new List<string>();
        if (opts != null)
            foreach (var raw in opts.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var o = raw.Trim().ToLowerInvariant(); bool neg = o.StartsWith('~'); var n = neg ? o[1..] : o;
                if (n.StartsWith("domain=")) { foreach (var d in n[7..].Split('|', StringSplitOptions.RemoveEmptyEntries)) { if (d.StartsWith('~')) dExc.Add(d[1..]); else dInc.Add(d); } continue; }
                switch (n)
                {
                    case "third-party": case "3p": if (neg) r.FirstParty = true; else r.ThirdParty = true; continue;
                    case "first-party": case "1p": if (neg) r.ThirdParty = true; else r.FirstParty = true; continue;
                    case "match-case": r.MatchCase = true; continue;
                    case "important": r.Important = true; continue;
                    case "document": case "doc": if (r.Exception && !neg) { r.DocumentException = true; continue; } return null;
                    case "script": Add(ReqType.Script); continue;
                    case "image": Add(ReqType.Image); continue;
                    case "stylesheet": case "css": Add(ReqType.Stylesheet); continue;
                    case "xmlhttprequest": case "xhr": Add(ReqType.Xhr); continue;
                    case "subdocument": case "frame": Add(ReqType.SubDocument); continue;
                    case "media": Add(ReqType.Media); continue;
                    case "font": Add(ReqType.Font); continue;
                    case "object": case "object-subrequest": Add(ReqType.Object); continue;
                    case "ping": Add(ReqType.Ping); continue;
                    case "websocket": Add(ReqType.WebSocket); continue;
                    case "other": Add(ReqType.Other); continue;
                    case "all": if (!neg) inc |= ReqType.All; continue;
                }
                return null;                                  // an option we don't implement (see SkipOptions): drop the whole rule rather than guess
                void Add(ReqType t) { if (neg) exc |= t; else inc |= t; }
            }
        r.Types = (inc != ReqType.None ? inc : ReqType.All) & ~exc;
        r.DomainsInc = dInc.ToArray(); r.DomainsExc = dExc.ToArray();

        // collapse '**', trim '*' at the open ends
        while (line.Contains("**")) line = line.Replace("**", "*");
        if (!r.AnchorStart && !r.AnchorDomain) line = line.TrimStart('*');
        if (!r.AnchorEnd) line = line.TrimEnd('*');
        if (line.Length == 0) return null;                                                // "match everything" rules are too dangerous
        r.Pattern = r.MatchCase ? line : line.ToLowerInvariant();
        r.Parts = r.Pattern.Split('*');
        return r;
    }

    // ------------------------------------------------------------------ matching
    /// <summary>True if a request for <paramref name="url"/> made by a page on <paramref name="docHost"/> should be blocked.</summary>
    public bool ShouldBlock(string url, string docHost, ReqType type)
    {
        if (block.ByHost.Count == 0 && block.ByToken.Count == 0 && block.Generic.Count == 0) return false;
        if (!TryLocate(url, out int schemeEnd, out int hostEnd)) return false;
        var host = url.AsSpan(schemeEnd, hostEnd - schemeEnd).ToString().ToLowerInvariant();
        docHost = (docHost ?? "").ToLowerInvariant();
        if (docHost.Length > 0 && SiteAllowed(docHost)) return false;
        bool third = docHost.Length == 0 || RegDomain(host) != RegDomain(docHost);
        var lower = url.ToLowerInvariant();

        var hit = Find(block, url, lower, schemeEnd, hostEnd, host, docHost, type, third, importantOnly: false);
        if (hit == null) return false;
        if (hit.Important) return true;
        return Find(allow, url, lower, schemeEnd, hostEnd, host, docHost, type, third, importantOnly: false) == null;
    }

    bool SiteAllowed(string docHost) { for (var h = docHost; h.Length > 0;) { if (documentAllow.Contains(h)) return true; int dot = h.IndexOf('.'); if (dot < 0) break; h = h[(dot + 1)..]; } return false; }

    FilterRule Find(RuleSet set, string url, string lower, int schemeEnd, int hostEnd, string host, string docHost, ReqType type, bool third, bool importantOnly)
    {
        // 1) rules anchored on a host: look up each suffix of the request host
        if (set.ByHost.Count > 0)
            for (var h = host; h.Length > 0;)
            {
                if (set.ByHost.TryGetValue(h, out var list)) foreach (var r in list) if (Applies(r, url, lower, schemeEnd, hostEnd, docHost, type, third)) return r;
                int dot = h.IndexOf('.'); if (dot < 0) break; h = h[(dot + 1)..];
            }
        // 2) rules indexed by a token of the URL
        if (set.ByToken.Count > 0)
            for (int i = 0; i < lower.Length;)
            {
                if (!char.IsAsciiLetterOrDigit(lower[i])) { i++; continue; }
                int e = i; while (e < lower.Length && char.IsAsciiLetterOrDigit(lower[e])) e++;
                if (e - i >= 3 && set.ByToken.TryGetValue(lower.Substring(i, e - i), out var list)) foreach (var r in list) if (Applies(r, url, lower, schemeEnd, hostEnd, docHost, type, third)) return r;
                i = e;
            }
        // 3) the few rules without a usable token
        foreach (var r in set.Generic) if (Applies(r, url, lower, schemeEnd, hostEnd, docHost, type, third)) return r;
        return null;
    }

    static bool Applies(FilterRule r, string url, string lower, int schemeEnd, int hostEnd, string docHost, ReqType type, bool third)
    {
        if ((r.Types & type) == 0) return false;
        if (r.ThirdParty && !third) return false;
        if (r.FirstParty && third) return false;
        if (r.DomainsInc.Length > 0 || r.DomainsExc.Length > 0)
        {
            if (r.DomainsExc.Any(d => DomainMatches(docHost, d))) return false;
            if (r.DomainsInc.Length > 0 && !r.DomainsInc.Any(d => DomainMatches(docHost, d))) return false;
        }
        return MatchPattern(r, r.MatchCase ? url : lower, schemeEnd, hostEnd);
    }

    internal static bool DomainMatches(string host, string domain) => host.Length > 0 && (host == domain || host.EndsWith("." + domain, StringComparison.Ordinal));

    static bool IsSeparator(char c) => !(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == '%');

    /// <summary>Does <paramref name="part"/> (may contain '^') match <paramref name="u"/> exactly at <paramref name="i"/>? Returns the end position or -1.</summary>
    static int PartAt(string part, string u, int i)
    {
        for (int j = 0; j < part.Length; j++)
        {
            int k = i + j;
            if (k >= u.Length) return part[j] == '^' && j == part.Length - 1 ? u.Length : -1;
            char c = part[j];
            if (c == '^') { if (!IsSeparator(u[k])) return -1; }
            else if (u[k] != c) return -1;
        }
        return i + part.Length;
    }

    static int FindPart(string part, string u, int from, out int end)
    {
        end = -1;
        if (part.IndexOf('^') < 0) { int ix = u.IndexOf(part, from, StringComparison.Ordinal); if (ix >= 0) end = ix + part.Length; return ix; }
        for (int s = from; s <= u.Length; s++) { int e = PartAt(part, u, s); if (e >= 0) { end = e; return s; } }
        return -1;
    }

    static bool MatchPattern(FilterRule r, string u, int schemeEnd, int hostEnd)
    {
        var parts = r.Parts;
        if (r.AnchorDomain)
        {
            // the first part must begin at the start of the host or right after a dot inside it
            for (int s = schemeEnd; s <= hostEnd; s++)
                if ((s == schemeEnd || u[s - 1] == '.') && TryFrom(parts, u, s, true, r.AnchorEnd)) return true;
            return false;
        }
        if (r.AnchorStart) return TryFrom(parts, u, 0, true, r.AnchorEnd);
        return TryFrom(parts, u, 0, false, r.AnchorEnd);
    }

    /// <summary>Matches the '*'-separated parts left to right. <paramref name="pinned"/>: the first part must start exactly at <paramref name="pos"/>.</summary>
    static bool TryFrom(string[] parts, string u, int pos, bool pinned, bool anchorEnd)
    {
        for (int pi = 0; pi < parts.Length; pi++)
        {
            var part = parts[pi]; bool last = pi == parts.Length - 1;
            if (part.Length == 0) { if (last && anchorEnd) return true; continue; }
            if (last && anchorEnd && !(pi == 0 && pinned))
            {
                // the final part has to finish exactly at the end of the URL
                int start = u.Length - part.Length;
                for (int s = Math.Max(pos, start - 1); s <= start + 1; s++) { if (s < pos || s < 0) continue; if (PartAt(part, u, s) == u.Length) return true; }
                return false;
            }
            if (pi == 0 && pinned) { int e = PartAt(part, u, pos); if (e < 0 || (last && anchorEnd && e != u.Length)) return false; pos = e; }
            else { int s = FindPart(part, u, pos, out int e); if (s < 0) return false; pos = e; }
        }
        return true;
    }

    // ------------------------------------------------------------------ cosmetic filtering
    /// <summary>CSS that hides ad elements on <paramref name="host"/> (empty when there is nothing to hide).</summary>
    public string CosmeticCss(string host)
    {
        host = (host ?? "").ToLowerInvariant();
        if (host.Length == 0 || SiteAllowed(host)) return "";
        // selectors that a "#@#" rule switches back on, either everywhere or for this host
        HashSet<string> unhide = null;
        for (var h = host; h.Length > 0;)
        {
            if (unhideByDomain.TryGetValue(h, out var set)) (unhide ??= new HashSet<string>(StringComparer.Ordinal)).UnionWith(set);
            int dot = h.IndexOf('.'); if (dot < 0) break; h = h[(dot + 1)..];
        }
        bool Off(string sel) => genericUnhide.Contains(sel) || (unhide != null && unhide.Contains(sel));
        var sb = new StringBuilder();
        if (unhide == null) sb.Append(genericCss);                                         // the common case: the prebuilt string
        else foreach (var s in genericHide.Distinct()) if (!Off(s)) sb.Append(s).Append("{display:none!important}");
        for (var h = host; h.Length > 0;)
        {
            if (hideByDomain.TryGetValue(h, out var list))
                foreach (var c in list) if (!c.Exc.Any(d => DomainMatches(host, d)) && !Off(c.Selector)) sb.Append(c.Selector).Append("{display:none!important}");
            int dot = h.IndexOf('.'); if (dot < 0) break; h = h[(dot + 1)..];
        }
        foreach (var c in genericScoped) if (!c.Exc.Any(d => DomainMatches(host, d)) && !Off(c.Selector)) sb.Append(c.Selector).Append("{display:none!important}");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ helpers
    /// <summary>Finds where the scheme ends and where the host ends in an absolute URL.</summary>
    internal static bool TryLocate(string url, out int schemeEnd, out int hostEnd)
    {
        schemeEnd = hostEnd = 0;
        int i = url.IndexOf("://", StringComparison.Ordinal); if (i < 0 || i > 10) return false;
        schemeEnd = i + 3; hostEnd = schemeEnd;
        int at = url.IndexOf('@', schemeEnd); int slash = url.IndexOfAny(new[] { '/', '?', '#' }, schemeEnd); if (slash < 0) slash = url.Length;
        if (at >= 0 && at < slash) schemeEnd = at + 1;
        hostEnd = schemeEnd; while (hostEnd < slash && url[hostEnd] != ':') hostEnd++;
        return hostEnd > schemeEnd;
    }
    static string HostOf(string pattern) { int i = 0; while (i < pattern.Length && (char.IsAsciiLetterOrDigit(pattern[i]) || pattern[i] == '.' || pattern[i] == '-')) i++; return i > 2 ? pattern[..i] : null; }

    static readonly HashSet<string> SecondLevel = new() { "co", "com", "org", "net", "gov", "edu", "ac", "or", "ne", "go" };
    /// <summary>The site a host belongs to (example.com, example.co.uk): used to decide what counts as "third-party".</summary>
    public static string RegDomain(string host)
    {
        if (string.IsNullOrEmpty(host) || host.Length == 0 || char.IsDigit(host[^1])) return host ?? "";     // IP address
        var l = host.Split('.'); if (l.Length <= 2) return host;
        int take = l[^1].Length == 2 && SecondLevel.Contains(l[^2]) ? 3 : 2;
        return string.Join('.', l.Skip(l.Length - take));
    }

    /// <summary>The domains Orbit has always blocked, as filter rules: used until the downloaded lists are available and as a safety net.</summary>
    public const string BuiltIn = @"! Orbit built-in rules
||doubleclick.net^$third-party
||googlesyndication.com^$third-party
||googleadservices.com^$third-party
||google-analytics.com^$third-party
||googletagmanager.com^$third-party
||adservice.google.com^$third-party
||adnxs.com^$third-party
||taboola.com^$third-party
||outbrain.com^$third-party
||scorecardresearch.com^$third-party
||criteo.com^$third-party
||amazon-adsystem.com^$third-party
||moatads.com^$third-party
||quantserve.com^$third-party
||hotjar.com^$third-party
||adsrvr.org^$third-party
||pubmatic.com^$third-party
||rubiconproject.com^$third-party
||openx.net^$third-party
";
}
