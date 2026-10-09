using System.Text.Json;

namespace SchoolGuard.Common;

public class AppConfig
{
    /// <summary>Repositorio de GitHub (público) donde publicás los Releases: "usuario/repo".</summary>
    public string UpdateRepo { get; set; } = "nikochittoo/schoolguard";
    /// <summary>Cada cuántos minutos busca actualizaciones (la API de GitHub limita por IP).</summary>
    public int UpdateCheckMinutes { get; set; } = 180;
    /// <summary>DNS real al que se reenvían las consultas no bloqueadas.</summary>
    public string UpstreamDns { get; set; } = "8.8.8.8";
    public int PanelPort { get; set; } = 8787;

    /// <summary>Modo estricto: fuerza el DNS de la PC a 127.0.0.1, aplica políticas de navegador
    /// y reglas de firewall. Ponelo en false para probar sin tocar la PC.</summary>
    public bool EnforceDns { get; set; } = true;
    /// <summary>(v0.2) Bloquea nombres de servidores DNS-sobre-HTTPS conocidos.</summary>
    public bool BlockDohHosts { get; set; } = true;
    /// <summary>(v0.2) En modo estricto, bloquea consultas DNS (puerto 53) hacia cualquier servidor
    /// que no sea UpstreamDns, y DNS-sobre-TLS (puerto 853).</summary>
    public bool BlockExternalDns { get; set; } = true;
    /// <summary>(v0.2) Restringe la carpeta de datos a SYSTEM y Administradores.</summary>
    public bool HardenDataDir { get; set; } = true;

    public static AppConfig Load()
    {
        Directory.CreateDirectory(AppPaths.Data);
        if (!File.Exists(AppPaths.ConfigFile))
        {
            var c = new AppConfig();
            File.WriteAllText(AppPaths.ConfigFile,
                JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = true }));
            return c;
        }
        return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(AppPaths.ConfigFile)) ?? new AppConfig();
    }
}
