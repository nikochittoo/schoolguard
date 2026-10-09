using System.Text.Json;
using System.Text.RegularExpressions;
using SchoolGuard.Common;

namespace SchoolGuard.Agent;

public record CatInfo(string Name, int Count, bool Enabled);

public class Blocklist
{
    /// <summary>Servidores DNS-sobre-HTTPS conocidos (y el "canario" de Firefox, que al recibir
    /// NXDOMAIN desactiva DoH automáticamente). No los puede anular la lista blanca.</summary>
    static readonly string[] DohHosts =
    {
        "use-application-dns.net", "dns.google", "dns.google.com", "cloudflare-dns.com",
        "mozilla.cloudflare-dns.com", "dns.quad9.net", "doh.opendns.com", "dns.adguard.com",
        "dns.nextdns.io", "doh.dns.sb"
    };

    // Foto inmutable de las reglas activas: el DNS la lee sin bloqueos y se reemplaza entera al cambiar algo.
    sealed record Snap(HashSet<string> Block, HashSet<string> White, HashSet<string> Doh, string[] Keywords);

    readonly object _l = new();
    readonly AppConfig _cfg;
    readonly HashSet<string> _manual;
    readonly HashSet<string> _white;
    readonly HashSet<string> _keywords;
    readonly Dictionary<string, HashSet<string>> _cats = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, bool> _enabled = new(StringComparer.OrdinalIgnoreCase);
    volatile Snap _snap = null!;

    public Blocklist(AppConfig cfg)
    {
        _cfg = cfg;
        Directory.CreateDirectory(AppPaths.ListsDir);
        _manual = ReadSet(AppPaths.BlocklistFile);
        _white = ReadSet(AppPaths.WhitelistFile);
        _keywords = ReadSet(AppPaths.KeywordsFile);
        foreach (var f in Directory.GetFiles(AppPaths.ListsDir, "*.txt"))
            _cats[Path.GetFileNameWithoutExtension(f)] = ReadSet(f);
        if (File.Exists(AppPaths.CategoriesFile))
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(AppPaths.CategoriesFile));
            if (d != null) _enabled = new Dictionary<string, bool>(d, StringComparer.OrdinalIgnoreCase);
        }
        Rebuild();
    }

    static HashSet<string> ReadSet(string path) =>
        File.Exists(path)
            ? new HashSet<string>(File.ReadAllLines(path)
                .Select(x => x.Trim().ToLowerInvariant())
                .Where(x => x.Length > 0 && !x.StartsWith('#')), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    static void WriteSet(string path, IEnumerable<string> items) =>
        File.WriteAllLines(path, items.OrderBy(x => x, StringComparer.Ordinal));

    bool IsEnabled(string cat) => !_enabled.TryGetValue(cat, out var e) || e;

    void Rebuild() // llamar con _l tomado (o desde el constructor)
    {
        var block = new HashSet<string>(_manual, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, set) in _cats)
            if (IsEnabled(name)) block.UnionWith(set);
        var doh = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_cfg.BlockDohHosts) foreach (var d in DohHosts) doh.Add(d);
        _snap = new Snap(block, new HashSet<string>(_white, StringComparer.OrdinalIgnoreCase), doh, _keywords.ToArray());
    }

    void SaveEnabled() => File.WriteAllText(AppPaths.CategoriesFile, JsonSerializer.Serialize(_enabled));

    // ---------- consulta (la usa el servidor DNS) ----------
    public bool IsBlocked(string name)
    {
        name = name.TrimEnd('.').ToLowerInvariant();
        var s = _snap;
        if (Matches(s.Doh, name)) return true;      // DoH: siempre bloqueado
        if (Matches(s.White, name)) return false;   // la lista blanca gana sobre todo lo demás
        foreach (var k in s.Keywords) if (name.Contains(k)) return true;
        return Matches(s.Block, name);
    }

    static bool Matches(HashSet<string> set, string name)
    {
        var n = name;
        while (true)
        {
            if (set.Contains(n)) return true;
            int i = n.IndexOf('.');
            if (i < 0) return false;
            n = n[(i + 1)..];
        }
    }

    // ---------- lista manual ----------
    public string[] All() { lock (_l) return _manual.OrderBy(x => x).ToArray(); }

    public bool Add(string host)
    {
        lock (_l)
        {
            if (!_manual.Add(host)) return false;
            WriteSet(AppPaths.BlocklistFile, _manual); Rebuild(); return true;
        }
    }

    public bool Remove(string host)
    {
        lock (_l)
        {
            if (!_manual.Remove(host)) return false;
            WriteSet(AppPaths.BlocklistFile, _manual); Rebuild(); return true;
        }
    }

    // ---------- lista blanca ----------
    public string[] Whitelist() { lock (_l) return _white.OrderBy(x => x).ToArray(); }

    public bool AddWhite(string host)
    {
        lock (_l)
        {
            if (!_white.Add(host)) return false;
            WriteSet(AppPaths.WhitelistFile, _white); Rebuild(); return true;
        }
    }

    public bool RemoveWhite(string host)
    {
        lock (_l)
        {
            if (!_white.Remove(host)) return false;
            WriteSet(AppPaths.WhitelistFile, _white); Rebuild(); return true;
        }
    }

    // ---------- palabras clave ----------
    public string[] Keywords() { lock (_l) return _keywords.OrderBy(x => x).ToArray(); }

    public bool AddKeyword(string word)
    {
        word = (word ?? "").Trim().ToLowerInvariant();
        if (word.Length < 3) return false;
        lock (_l)
        {
            if (!_keywords.Add(word)) return false;
            WriteSet(AppPaths.KeywordsFile, _keywords); Rebuild(); return true;
        }
    }

    public bool RemoveKeyword(string word)
    {
        lock (_l)
        {
            if (!_keywords.Remove((word ?? "").Trim().ToLowerInvariant())) return false;
            WriteSet(AppPaths.KeywordsFile, _keywords); Rebuild(); return true;
        }
    }

    // ---------- categorías importadas ----------
    public static bool ValidCategory(string? n) => n != null && Regex.IsMatch(n, "^[a-z0-9_-]{1,30}$");

    public CatInfo[] Categories()
    {
        lock (_l)
            return _cats.Select(kv => new CatInfo(kv.Key, kv.Value.Count, IsEnabled(kv.Key)))
                        .OrderBy(c => c.Name).ToArray();
    }

    /// <summary>Crea la categoría o, si ya existe, reemplaza su contenido (sirve para actualizar la lista).</summary>
    public void SetCategory(string name, HashSet<string> domains)
    {
        lock (_l)
        {
            _cats[name] = domains;
            WriteSet(Path.Combine(AppPaths.ListsDir, name + ".txt"), domains);
            Rebuild();
        }
    }

    public bool SetEnabled(string name, bool enabled)
    {
        lock (_l)
        {
            if (!_cats.ContainsKey(name)) return false;
            _enabled[name] = enabled; SaveEnabled(); Rebuild(); return true;
        }
    }

    public bool RemoveCategory(string name)
    {
        lock (_l)
        {
            if (!_cats.Remove(name)) return false;
            _enabled.Remove(name); SaveEnabled();
            try { File.Delete(Path.Combine(AppPaths.ListsDir, name + ".txt")); } catch { }
            Rebuild(); return true;
        }
    }

    /// <summary>Convierte una URL cualquiera en el dominio "madre" (sin esquema, ruta ni www).</summary>
    public static string? Normalize(string input)
    {
        input = (input ?? "").Trim().ToLowerInvariant();
        if (input.Length == 0) return null;
        if (!input.Contains("://")) input = "http://" + input;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var u)) return null;
        var h = u.IdnHost;
        if (h.StartsWith("www.")) h = h[4..];
        return h.Contains('.') ? h : null;
    }
}
