namespace SchoolGuard.Common;

/// <summary>Datos y configuración viven en ProgramData, separados del programa,
/// así actualizar el programa nunca borra listas, usuarios ni configuración.</summary>
public static class AppPaths
{
    public static string Data { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SchoolGuard");

    public static string ConfigFile => Path.Combine(Data, "config.json");
    public static string BlocklistFile => Path.Combine(Data, "blocklist.txt");
    public static string KeywordsFile => Path.Combine(Data, "keywords.txt");
    public static string UsersFile => Path.Combine(Data, "users.json");
    public static string BadVersionsFile => Path.Combine(Data, "bad-versions.txt");
}
