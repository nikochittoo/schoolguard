using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SchoolGuard.Common;

namespace SchoolGuard.Updater;

/// <summary>Servicio independiente que busca el último Release en GitHub y actualiza el agente
/// en el lugar: descarga, verifica SHA-256, hace copia, reemplaza, comprueba que arranque
/// y, si falla, vuelve a la versión anterior (rollback).</summary>
public class UpdateWorker(ILogger<UpdateWorker> log) : BackgroundService
{
    const string AgentService = "SchoolGuardAgent";
    const string AssetZip = "agent-win-x64.zip";
    const string AssetSums = "SHA256SUMS.txt";

    // El updater vive en {instalación}\updater\, el agente en {instalación}\agent\
    static readonly string Install = Directory.GetParent(AppContext.BaseDirectory.TrimEnd('\\', '/'))!.FullName;
    static string AgentDir => Path.Combine(Install, "agent");
    static string VersionFile => Path.Combine(Install, "version.txt");

    static readonly HttpClient Http = CreateHttp();
    static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("SchoolGuard-Updater");
        h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return h;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), ct);
        while (!ct.IsCancellationRequested)
        {
            var cfg = AppConfig.Load();
            try { await CheckOnce(cfg, ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { log.LogWarning(e, "No se pudo buscar actualizaciones"); }

            // Margen aleatorio: muchas PCs comparten IP y la API de GitHub limita por IP.
            var wait = TimeSpan.FromMinutes(Math.Max(15, cfg.UpdateCheckMinutes)) + TimeSpan.FromSeconds(Random.Shared.Next(0, 600));
            await Task.Delay(wait, ct);
        }
    }

    static Version ReadVersion() =>
        File.Exists(VersionFile) && Version.TryParse(File.ReadAllText(VersionFile).Trim(), out var v) ? v : new Version(0, 0, 0);

    async Task CheckOnce(AppConfig cfg, CancellationToken ct)
    {
        var current = ReadVersion();
        using var doc = JsonDocument.Parse(
            await Http.GetStringAsync($"https://api.github.com/repos/{cfg.UpdateRepo}/releases/latest", ct));
        var tag = doc.RootElement.GetProperty("tag_name").GetString()!;
        var clean = tag.TrimStart('v', 'V');

        if (!Version.TryParse(clean, out var latest) || latest <= current) return;
        if (File.Exists(AppPaths.BadVersionsFile) && File.ReadAllLines(AppPaths.BadVersionsFile).Contains(tag)) return;

        string? zipUrl = null, sumsUrl = null;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var n = a.GetProperty("name").GetString();
            var u = a.GetProperty("browser_download_url").GetString();
            if (n == AssetZip) zipUrl = u; else if (n == AssetSums) sumsUrl = u;
        }
        if (zipUrl is null || sumsUrl is null) { log.LogWarning("El release {Tag} no tiene los archivos esperados", tag); return; }

        log.LogInformation("Nueva versión {Tag} (actual {Cur}). Descargando…", tag, current);

        var sums = await Http.GetStringAsync(sumsUrl, ct);
        var expected = sums.Split('\n')
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(p => p.Length >= 2 && p[1] == AssetZip)
            .Select(p => p[0].ToLowerInvariant())
            .FirstOrDefault();

        var bytes = await Http.GetByteArrayAsync(zipUrl, ct);
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (expected is null || expected != actual) { log.LogError("Hash SHA-256 inválido, se descarta {Tag}", tag); return; }

        var tmp = Path.Combine(Path.GetTempPath(), $"schoolguard-{clean}.zip");
        await File.WriteAllBytesAsync(tmp, bytes, ct);
        try { await Apply(tmp, tag, clean, cfg); }
        finally { try { File.Delete(tmp); } catch { } }
    }

    async Task Apply(string zip, string tag, string clean, AppConfig cfg)
    {
        var bak = AgentDir + ".bak";

        Sc("stop", AgentService);
        await WaitStopped();
        try
        {
            if (Directory.Exists(bak)) Directory.Delete(bak, true);
            Directory.Move(AgentDir, bak);
            ZipFile.ExtractToDirectory(zip, AgentDir);
        }
        catch (Exception e)
        {
            log.LogError(e, "Falló el reemplazo de archivos; se restaura la versión anterior");
            Restore(bak);
            Sc("start", AgentService);
            return;
        }

        Sc("start", AgentService);
        if (await WaitHealthy(clean, cfg))
        {
            File.WriteAllText(VersionFile, clean);
            try { Directory.Delete(bak, true); } catch { }
            log.LogInformation("Actualizado a {Tag}", tag);
        }
        else
        {
            log.LogError("La versión {Tag} no arrancó bien. Rollback.", tag);
            Sc("stop", AgentService);
            await WaitStopped();
            Restore(bak);
            File.AppendAllLines(AppPaths.BadVersionsFile, new[] { tag }); // no reintentar esta versión
            Sc("start", AgentService);
        }
    }

    static void Restore(string bak)
    {
        if (!Directory.Exists(bak)) return;
        if (Directory.Exists(AgentDir)) Directory.Delete(AgentDir, true);
        Directory.Move(bak, AgentDir);
    }

    static async Task WaitStopped()
    {
        for (int i = 0; i < 30; i++)
        {
            if (ScOut("query", AgentService).Contains("STOPPED")) break;
            await Task.Delay(1000);
        }
        await Task.Delay(1000); // margen para que Windows libere los archivos
    }

    static async Task<bool> WaitHealthy(string expectedVersion, AppConfig cfg)
    {
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(2000);
            try
            {
                using var cts = new CancellationTokenSource(3000);
                using var doc = JsonDocument.Parse(await Http.GetStringAsync($"http://127.0.0.1:{cfg.PanelPort}/api/health", cts.Token));
                if (doc.RootElement.GetProperty("version").GetString()!.StartsWith(expectedVersion)) return true;
            }
            catch { }
        }
        return false;
    }

    static int Sc(params string[] args) { ScOut(args, out var code); return code; }
    static string ScOut(params string[] args) => ScOut(args, out _);
    static string ScOut(string[] args, out int code)
    {
        var psi = new ProcessStartInfo("sc.exe") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        code = p.ExitCode;
        return o;
    }
}
