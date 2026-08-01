using System.Text.Json;
using GpuPreferenceManager.Core.Registry;

namespace GpuPreferenceManager.Core.Tests;

public sealed class CurrentMachineRegistryFixtureTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void CurrentMachineRulesAreAllClassifiedAsExpected()
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CurrentMachineRegistry.json");
        string json = File.ReadAllText(fixturePath);
        IReadOnlyList<RegistryFixtureEntry> entries = JsonSerializer.Deserialize<List<RegistryFixtureEntry>>(
            json,
            SerializerOptions)!;

        Assert.Equal(19, entries.Count);
        Assert.Equal(8, entries.Count(entry => entry.ExpectedKind == "GenericHighPerformance"));
        Assert.Equal(10, entries.Count(entry => entry.ExpectedKind == "SpecificAdapter"));

        foreach (RegistryFixtureEntry entry in entries)
        {
            if (entry.ExpectedKind == "GlobalSettings")
            {
                RegistryRuleToken? token = RegistryRuleParser.Tokenize(entry.Value)
                    .SingleOrDefault(candidate => candidate.HasKey("HighPerfAdapter"));
                Assert.NotNull(token);
                Assert.Equal(entry.ExpectedAdapterKey, token.Value);
                continue;
            }

            GpuPreferenceRule rule = RegistryRuleParser.Parse(entry.Value);
            Assert.Equal(entry.ExpectedKind, rule.Kind.ToString());
            Assert.Equal(entry.ExpectedAdapterKey, rule.SpecificAdapterKey);
        }
    }

    private sealed record RegistryFixtureEntry(
        string Name,
        string Value,
        string ExpectedKind,
        string? ExpectedAdapterKey);
}
