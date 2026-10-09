using System.Diagnostics;
using SchoolGuard.Common;

namespace SchoolGuard.Agent;

/// <summary>Deja la carpeta de datos accesible solo a SYSTEM y Administradores,
/// para que un usuario estándar no pueda leer ni editar listas, claves ni configuración.</summary>
public static class DataHardening
{
    public static void Apply(ILogger log)
    {
        try
        {
            var psi = new ProcessStartInfo("icacls.exe") { CreateNoWindow = true, UseShellExecute = false };
            psi.ArgumentList.Add(AppPaths.Data);
            psi.ArgumentList.Add("/inheritance:r");
            psi.ArgumentList.Add("/grant:r");
            psi.ArgumentList.Add("*S-1-5-18:(OI)(CI)F");     // SYSTEM
            psi.ArgumentList.Add("*S-1-5-32-544:(OI)(CI)F"); // Administradores
            using var p = Process.Start(psi)!;
            p.WaitForExit(30_000);
        }
        catch (Exception e) { log.LogWarning(e, "No se pudo restringir la carpeta de datos"); }
    }
}
