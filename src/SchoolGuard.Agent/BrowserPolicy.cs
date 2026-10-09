using Microsoft.Win32;

namespace SchoolGuard.Agent;

/// <summary>Desactiva DNS-sobre-HTTPS en los navegadores por política de Windows (HKLM),
/// para que no puedan saltarse el DNS local.</summary>
public static class BrowserPolicy
{
    public static void Apply(ILogger log)
    {
        try
        {
            SetString(@"SOFTWARE\Policies\Google\Chrome", "DnsOverHttpsMode", "off");
            SetString(@"SOFTWARE\Policies\Microsoft\Edge", "DnsOverHttpsMode", "off");
            SetString(@"SOFTWARE\Policies\BraveSoftware\Brave", "DnsOverHttpsMode", "off");
            SetDword(@"SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS", "Enabled", 0);
            SetDword(@"SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS", "Locked", 1);
        }
        catch (Exception e) { log.LogWarning(e, "No se pudieron aplicar las políticas de navegador"); }
    }

    static void SetString(string path, string name, string value)
    {
        using var k = Registry.LocalMachine.CreateSubKey(path, true);
        if (!Equals(k.GetValue(name), value)) k.SetValue(name, value, RegistryValueKind.String);
    }

    static void SetDword(string path, string name, int value)
    {
        using var k = Registry.LocalMachine.CreateSubKey(path, true);
        if (!Equals(k.GetValue(name), value)) k.SetValue(name, value, RegistryValueKind.DWord);
    }
}
