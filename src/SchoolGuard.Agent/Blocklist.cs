using SchoolGuard.Common;

namespace SchoolGuard.Agent;

public class Blocklist
{
    readonly object _l = new();
    HashSet<string> _domains = new(StringComparer.OrdinalIgnoreCase);
    HashSet<string> _keywords = new(StringComparer.OrdinalIgnoreCase);

    public Blocklist() => Load();

    void Load()
    {
        lock (_l)
        {
            _domains = ReadSet(AppPaths.BlocklistFile);
            _keywords = ReadSet(AppPaths.KeywordsFile);
        }
    }

    static HashSet<string> ReadSet(string path) =>
        File.Exists(path)
            ? new HashSet<string>(File.ReadAllLines(path)
                .Select(x => x.Trim().ToLowerInvariant())
                .Where(x => x.Length > 0 && !x.StartsWith('#')), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public string[] All() { lock (_l) return _domains.OrderBy(x => x).ToArray(); }
    public string[] Keywords() { lock (_l) return _keywords.OrderBy(x => x).ToArray(); }

    public bool Add(string host)
    {
        lock (_l)
        {
            if (!_domains.Add(host)) return false;
            File.WriteAllLines(AppPaths.BlocklistFile, _domains.OrderBy(x => x));
            return true;
        }
    }

    public bool Remove(string host)
    {
        lock (_l)
        {
            if (!_domains.Remove(host)) return false;
            File.WriteAllLines(AppPaths.BlocklistFile, _domains.OrderBy(x => x));
            return true;
        }
    }

    public bool AddKeyword(string word)
    {
        word = (word ?? "").Trim().ToLowerInvariant();
        if (word.Length < 3) return false;
        lock (_l)
        {
            if (!_keywords.Add(word)) return false;
            File.WriteAllLines(AppPaths.KeywordsFile, _keywords.OrderBy(x => x));
            return true;
        }
    }

    public bool RemoveKeyword(string word)
    {
        lock (_l)
        {
            if (!_keywords.Remove((word ?? "").Trim().ToLowerInvariant())) return false;
            File.WriteAllLines(AppPaths.KeywordsFile, _keywords.OrderBy(x => x));
            return true;
        }
    }

    /// <summary>Bloquea el dominio, TODOS sus subdominios y cualquier nombre que contenga una palabra clave.</summary>
    public bool IsBlocked(string name)
    {
        name = name.TrimEnd('.').ToLowerInvariant();
        lock (_l)
        {
            foreach (var k in _keywords) if (name.Contains(k)) return true;
            var n = name;
            while (true)
            {
                if (_domains.Contains(n)) return true;
                int i = n.IndexOf('.');
                if (i < 0) return false;
                n = n[(i + 1)..];
            }
        }
    }

    /// <summary>Convierte una URL cualquiera en el dominio "madre" a bloquear (sin esquema, ruta ni www).</summary>
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
