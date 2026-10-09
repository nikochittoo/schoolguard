using System.Net;
using System.Net.Sockets;
using System.Text;
using SchoolGuard.Common;

namespace SchoolGuard.Agent;

/// <summary>Servidor DNS local (127.0.0.1:53). Los dominios bloqueados reciben NXDOMAIN;
/// el resto se reenvía al DNS real configurado.</summary>
public class DnsServer(Blocklist blocklist, BlockLog blockLog, AppConfig cfg, ILogger<DnsServer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 53));
        // Evita que Windows lance SocketException cuando un cliente cierra su puerto (ICMP port unreachable).
        try { udp.Client.IOControl((IOControlCode)(-1744830452), new byte[] { 0 }, null); } catch { }
        logger.LogInformation("DNS local escuchando en 127.0.0.1:53");

        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await udp.ReceiveAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { continue; }
            _ = Task.Run(() => HandleAsync(udp, r, ct), ct);
        }
    }

    async Task HandleAsync(UdpClient server, UdpReceiveResult r, CancellationToken ct)
    {
        try
        {
            var q = r.Buffer;
            var name = ParseQName(q, out int end);

            if (name != null && blocklist.IsBlocked(name))
            {
                blockLog.Add(name);
                var resp = new byte[end];
                Array.Copy(q, resp, end);
                resp[2] = (byte)(0x80 | (q[2] & 0x01)); // QR=1, conserva RD
                resp[3] = 0x83;                         // RA=1, RCODE=3 (NXDOMAIN)
                for (int i = 6; i < 12; i++) resp[i] = 0; // sin respuestas
                await server.SendAsync(resp, resp.Length, r.RemoteEndPoint);
                return;
            }

            using var up = new UdpClient();
            up.Connect(cfg.UpstreamDns, 53);
            await up.SendAsync(q, q.Length);
            var recv = up.ReceiveAsync(ct).AsTask();
            if (await Task.WhenAny(recv, Task.Delay(3000, ct)) == recv)
            {
                var b = recv.Result.Buffer;
                await server.SendAsync(b, b.Length, r.RemoteEndPoint);
            }
        }
        catch (Exception e) { logger.LogDebug(e, "Error manejando consulta DNS"); }
    }

    static string? ParseQName(byte[] b, out int endOfQuestion)
    {
        endOfQuestion = 0;
        if (b.Length < 17) return null;
        int pos = 12;
        var sb = new StringBuilder();
        while (pos < b.Length)
        {
            int len = b[pos++];
            if (len == 0) break;
            if ((len & 0xC0) != 0 || pos + len > b.Length) return null;
            if (sb.Length > 0) sb.Append('.');
            sb.Append(Encoding.ASCII.GetString(b, pos, len));
            pos += len;
        }
        if (pos + 4 > b.Length) return null;
        endOfQuestion = pos + 4; // QTYPE + QCLASS
        return sb.ToString();
    }
}
