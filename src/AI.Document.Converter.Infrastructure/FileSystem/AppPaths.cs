namespace AI.Document.Converter.Infrastructure.FileSystem;

// Central place for every path the app derives on its own, so a default location
// is never hard-coded in more than one place (docs/11-CODING-STANDARDS.md Section 4).
public static class AppPaths
{
    private const string AppFolderName = "AIDocumentConverter";

    public static string AppDataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName);

    // Deliberately outside the install directory (docs/19-DEPLOYMENT-PLAN.md
    // Section 6) so an application upgrade never wipes user settings.
    public static string SettingsFilePath => Path.Combine(AppDataDirectory, "settings.json");

    public static string DefaultOutputDirectory => Path.Combine(AppDataDirectory, "Output");

    public static string DefaultLogDirectory => Path.Combine(AppDataDirectory, "Logs");

    public static string TempDirectory => Path.Combine(AppDataDirectory, "Temp");

    // Where the bundled, PyInstaller-built Python engine ships relative to the
    // application's own executable (ADR-001; docs/19-DEPLOYMENT-PLAN.md Section 1).
    public static string BundledPythonEnginePath =>
        Path.Combine(AppContext.BaseDirectory, "PythonEngine", "AIDocumentConverter.PythonEngine.exe");
}
