using SchoolGuard.Common;

namespace SchoolGuard.Agent;

/// <summary>La autorización para desinstalar (archivo-bandera creado desde el panel) vence a los 30 minutos.</summary>
public class UninstallGate : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var f = AppPaths.UninstallFlag;
                if (File.Exists(f) && DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromMinutes(30))
                    File.Delete(f);
            }
            catch { }
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}
