using System.Diagnostics;
using System.Text;
using SchoolGuard.Common;

namespace SchoolGuard.Agent;

/// <summary>Modo estricto: cada minuto revisa que las placas de red usen 127.0.0.1 como DNS,
/// reaplica las políticas de navegador y (cada 10 minutos) las reglas de firewall.</summary>
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
        await Task.Delay(3000, ct); // deja que el servidor DNS arranque primero

        if (cfg.HardenDataDir) DataHardening.Apply(logger);

        if (!cfg.EnforceDns)
        {
            logger.LogWarning("EnforceDns=false: modo de prueba, no se toca el DNS, el firewall ni los navegadores.");
            FirewallGuard.Remove(logger);
            return;
        }

        int tick = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                ApplyDns();
                BrowserPolicy.Apply(logger);
                if (tick % 10 == 0) FirewallGuard.Apply(cfg, logger);
            }
            catch (Exception e) { logger.LogWarning(e, "No se pudo aplicar el modo estricto"); }
            tick++;
            await Task.Delay(TimeSpan.FromSeconds(60), ct);
        }
    }

    static void ApplyDns()
    {
        var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(Script));
        var psi = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {enc}")
        { CreateNoWindow = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        p.WaitForExit(30_000);
    }
}
