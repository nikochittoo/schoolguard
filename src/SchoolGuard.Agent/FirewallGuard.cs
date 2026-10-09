using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SchoolGuard.Common;

namespace SchoolGuard.Agent;

/// <summary>Reglas de salida del Firewall de Windows: bloquea DNS (puerto 53) hacia cualquier
/// servidor que no sea el configurado en UpstreamDns, y DNS-sobre-TLS (puerto 853).
/// Solo IPv4. El tráfico a 127.0.0.1 (nuestro propio DNS) no pasa por el firewall.</summary>
public static class FirewallGuard
{
    static readonly string[] Names = { "SchoolGuard-DNS-UDP", "SchoolGuard-DNS-TCP", "SchoolGuard-DoT" };

    public static void Remove(ILogger log)
    {
        foreach (var n in Names) Netsh(log, "advfirewall", "firewall", "delete", "rule", $"name={n}");
    }

    public static void Apply(AppConfig cfg, ILogger log)
    {
        Remove(log);
        if (!cfg.BlockExternalDns) return;
        if (!IPAddress.TryParse(cfg.UpstreamDns, out var up) || up.AddressFamily != AddressFamily.InterNetwork)
        {
            log.LogWarning("UpstreamDns no es una IPv4 válida; no se aplican reglas de firewall.");
            return;
        }
        var remote = Complement(up);
        Netsh(log, "advfirewall", "firewall", "add", "rule", "name=SchoolGuard-DNS-UDP", "dir=out",
              "action=block", "protocol=UDP", "remoteport=53", $"remoteip={remote}");
        Netsh(log, "advfirewall", "firewall", "add", "rule", "name=SchoolGuard-DNS-TCP", "dir=out",
              "action=block", "protocol=TCP", "remoteport=53", $"remoteip={remote}");
        Netsh(log, "advfirewall", "firewall", "add", "rule", "name=SchoolGuard-DoT", "dir=out",
              "action=block", "protocol=TCP", "remoteport=853");
    }

    // Todas las IPv4 menos la del DNS permitido (el firewall no tiene "excepto", hay que dar rangos).
    static string Complement(IPAddress ip)
    {
        uint v = ToUInt(ip);
        var parts = new List<string>();
        if (v > 0) parts.Add($"0.0.0.0-{FromUInt(v - 1)}");
        if (v < uint.MaxValue) parts.Add($"{FromUInt(v + 1)}-255.255.255.255");
        return string.Join(",", parts);
    }

    static uint ToUInt(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    static string FromUInt(uint v) => $"{v >> 24}.{(v >> 16) & 255}.{(v >> 8) & 255}.{v & 255}";

    static void Netsh(ILogger log, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh.exe") { CreateNoWindow = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            p.WaitForExit(15_000);
        }
        catch (Exception e) { log.LogWarning(e, "netsh falló"); }
    }
}
