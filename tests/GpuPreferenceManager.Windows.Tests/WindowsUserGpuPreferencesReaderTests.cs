using GpuPreferenceManager.Core.Registry;
using GpuPreferenceManager.Windows.Registry;
using Microsoft.Win32;

namespace GpuPreferenceManager.Windows.Tests;

public sealed class WindowsUserGpuPreferencesReaderTests
{
    [Fact]
    public async Task ReadsCompleteSnapshotFromIsolatedTestKeyAndSeparatesGlobalSettings()
    {
        string testPath = $@"Software\GpuPreferenceManager.Tests\{Guid.NewGuid():N}";
        try
        {
            using (RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(testPath, writable: true))
            {
                key.SetValue(
                    @"C:\Fixture\Game.exe",
                    "SpecificAdapter=1002&73EF&1EFE;GpuPreference=1073741824;Future=1;",
                    RegistryValueKind.String);
                key.SetValue(
                    WindowsUserGpuPreferencesReader.GlobalSettingsValueName,
                    "HighPerfAdapter=1002&73EF&1EFE;",
                    RegistryValueKind.String);
                key.SetValue("BinaryFixture", new byte[] { 1, 2, 3 }, RegistryValueKind.Binary);
            }

            WindowsUserGpuPreferencesReader reader = new(testPath);
            RegistrySnapshot snapshot = await reader.ReadSnapshotAsync(CancellationToken.None);

            Assert.True(snapshot.KeyExists);
            Assert.Equal(3, snapshot.Values.Count);
            Assert.Equal(2, snapshot.ApplicationValues.Count());
            Assert.Equal("1002&73EF&1EFE", snapshot.GlobalSettings?.HighPerformanceAdapterKey);
            RegistryValueSnapshot app = Assert.Single(
                snapshot.Values,
                value => value.Name.EndsWith("Game.exe", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(GpuPreferenceKind.SpecificAdapter, app.Rule?.Kind);
            Assert.Contains(app.Rule!.Tokens, token => token.HasKey("Future"));
            RegistryValueSnapshot binary = Assert.Single(
                snapshot.Values,
                value => value.Name == "BinaryFixture");
            Assert.Equal(new byte[] { 1, 2, 3 }, binary.Data.BinaryValue);
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(testPath, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task MissingKeyProducesEmptySnapshot()
    {
        WindowsUserGpuPreferencesReader reader = new(
            $@"Software\GpuPreferenceManager.Tests\Missing-{Guid.NewGuid():N}");

        RegistrySnapshot snapshot = await reader.ReadSnapshotAsync(CancellationToken.None);

        Assert.False(snapshot.KeyExists);
        Assert.Empty(snapshot.Values);
        Assert.Null(snapshot.GlobalSettings);
    }
}
