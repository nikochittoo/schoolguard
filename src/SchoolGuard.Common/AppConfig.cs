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
    /// <summary>Si es true, el agente fuerza el DNS de la PC a 127.0.0.1. Ponelo en false para desarrollar.</summary>
    public bool EnforceDns { get; set; } = true;

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
