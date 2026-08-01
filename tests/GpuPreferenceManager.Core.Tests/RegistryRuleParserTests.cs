using GpuPreferenceManager.Core.Registry;

namespace GpuPreferenceManager.Core.Tests;

public sealed class RegistryRuleParserTests
{
    [Fact]
    public void NullRepresentsNoRule()
    {
        GpuPreferenceRule rule = RegistryRuleParser.Parse(null);

        Assert.Equal(GpuPreferenceKind.NoRule, rule.Kind);
        Assert.Empty(rule.Tokens);
    }

    [Theory]
    [InlineData("", GpuPreferenceKind.WindowsDecides, null)]
    [InlineData("GpuPreference=0;", GpuPreferenceKind.WindowsDecides, 0)]
    [InlineData("GpuPreference=1;", GpuPreferenceKind.GenericPowerSaving, 1)]
    [InlineData("GpuPreference=2;", GpuPreferenceKind.GenericHighPerformance, 2)]
    [InlineData("gpupreference=2;", GpuPreferenceKind.GenericHighPerformance, 2)]
    [InlineData("GpuPreference=7;", GpuPreferenceKind.Unknown, 7)]
    [InlineData("SpecificAdapter=1002&73EF&1EFE;", GpuPreferenceKind.Unknown, null)]
    [InlineData("MalformedToken;", GpuPreferenceKind.Unknown, null)]
    public void ClassifiesKnownAndMalformedRules(
        string raw,
        GpuPreferenceKind expectedKind,
        int? expectedPreference)
    {
        GpuPreferenceRule rule = RegistryRuleParser.Parse(raw);

        Assert.Equal(expectedKind, rule.Kind);
        Assert.Equal(expectedPreference, rule.RawGpuPreference);
    }

    [Fact]
    public void ParsesSpecificAdapterRegardlessOfFieldOrderAndCase()
    {
        const string raw = "SomeFutureField=1;GPUPREFERENCE=1073741824;specificadapter=1002&73ef&1efe;";

        GpuPreferenceRule rule = RegistryRuleParser.Parse(raw);

        Assert.Equal(GpuPreferenceKind.SpecificAdapter, rule.Kind);
        Assert.Equal("1002&73ef&1efe", rule.SpecificAdapterKey);
        Assert.Collection(
            rule.Tokens,
            token => Assert.Equal("SomeFutureField", token.Key),
            token => Assert.Equal("GPUPREFERENCE", token.Key),
            token => Assert.Equal("specificadapter", token.Key));
    }

    [Fact]
    public void DuplicateGpuFieldsUseLastValueForClassification()
    {
        GpuPreferenceRule rule = RegistryRuleParser.Parse("GpuPreference=1;GpuPreference=2;");

        Assert.Equal(GpuPreferenceKind.GenericHighPerformance, rule.Kind);
        Assert.Equal(2, rule.RawGpuPreference);
        Assert.Equal(2, rule.Tokens.Count);
    }
}
