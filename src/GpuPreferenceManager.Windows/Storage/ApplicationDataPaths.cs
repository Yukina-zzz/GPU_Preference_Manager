namespace GpuPreferenceManager.Windows.Storage;

public sealed record ApplicationDataPaths(
    string Root,
    string DatabasePath,
    string SettingsPath,
    string BackupDirectory,
    string LogDirectory,
    string DiagnosticsDirectory)
{
    public static ApplicationDataPaths CreateDefault()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GpuPreferenceManager");
        return Create(root);
    }

    public static ApplicationDataPaths Create(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        return new(
            root,
            Path.Combine(root, "data.db"),
            Path.Combine(root, "settings.json"),
            Path.Combine(root, "Backups"),
            Path.Combine(root, "Logs"),
            Path.Combine(root, "Diagnostics"));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(DiagnosticsDirectory);
    }
}
