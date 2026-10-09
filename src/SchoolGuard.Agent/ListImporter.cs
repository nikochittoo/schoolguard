using System.Text.RegularExpressions;

namespace SchoolGuard.Agent;

/// <summary>Descarga una lista pública y extrae los dominios. Entiende formato "hosts"
/// (0.0.0.0 dominio), un dominio por línea y formato adblock (||dominio^).</summary>
public static class ListImporter
{
    static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(3),
        MaxResponseContentBufferSize = 64 * 1024 * 1024
    };

    static readonly Regex Dom = new(
        @"^(?=.{4,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,24}$", RegexOptions.Compiled);

    public static async Task<HashSet<string>> FetchAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http"))
            throw new ArgumentException("La URL de la lista no es válida.");

        var text = await Http.GetStringAsync(u, ct);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var d = ParseLine(raw);
            if (d != null) set.Add(d);
        }
        return set;
    }

    static string? ParseLine(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0 || line[0] == '#' || line[0] == '!') return null;
        int h = line.IndexOf('#');
        if (h > 0) line = line[..h].Trim();

        string cand;
        if (line.StartsWith("||")) cand = line[2..].TrimEnd('^');
        else
        {
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && (parts[0] == "0.0.0.0" || parts[0] == "127.0.0.1")) cand = parts[1];
            else if (parts.Length == 1) cand = parts[0];
            else return null;
        }
        cand = cand.ToLowerInvariant();
        return cand == "localhost" || !Dom.IsMatch(cand) ? null : cand;
    }
}
