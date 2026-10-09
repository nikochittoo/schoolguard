using System.Diagnostics;
using System.Text;
using SchoolGuard.Common;

namespace SchoolGuard.Agent;

/// <summary>Cada minuto revisa que todas las placas de red activas usen 127.0.0.1 como DNS
/// y, si alguien lo cambió, lo vuelve a poner.</summary>
public class DnsEnforcer(AppConfig cfg, ILogger<DnsEnforcer> logger) : BackgroundService
{
    const string Script =
        "$a = Get-NetAdapter | Where-Object { $_.Status -eq 'Up' }; " +
        "foreach ($i in $a) { " +
        "$d = (Get-DnsClientServerAddress -InterfaceIndex $i.ifIndex -AddressFamily IPv4).ServerAddresses; " +
        "if (-not ($d.Count -eq 1 -and $d[0] -eq '127.0.0.1')) { " +
        "Set-DnsClientServerAddress -InterfaceIndex $i.ifIndex -ServerAddresses 127.0.0.1; Clear-DnsClientCache } }";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!cfg.EnforceDns) { logger.LogWarning("EnforceDns=false: no se fuerza el DNS de la PC."); return; }
        await Task.Delay(3000, ct); // deja que el servidor DNS arranque primero
        while (!ct.IsCancellationRequested)
        {
            try { Apply(); } catch (Exception e) { logger.LogWarning(e, "No se pudo aplicar el DNS"); }
            await Task.Delay(TimeSpan.FromSeconds(60), ct);
        }
    }

    static void Apply()
    {
        var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(Script));
        var psi = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {enc}")
        { CreateNoWindow = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        p.WaitForExit(30_000);
    }
}
