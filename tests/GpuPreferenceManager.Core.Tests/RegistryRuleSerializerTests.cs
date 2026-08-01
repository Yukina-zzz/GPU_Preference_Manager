using GpuPreferenceManager.Core.Registry;

namespace GpuPreferenceManager.Core.Tests;

public sealed class RegistryRuleSerializerTests
{
    [Fact]
    public void SerializesWithTrailingSemicolonAndPreservesMalformedToken()
    {
        IReadOnlyList<RegistryRuleToken> tokens = RegistryRuleParser.Tokenize("Future=1;Malformed");

        string result = RegistryRuleSerializer.Serialize(tokens);

        Assert.Equal("Future=1;Malformed;", result);
    }

    [Fact]
    public void SettingSpecificAdapterOnlyReplacesGpuFields()
    {
        GpuPreferenceRule source = RegistryRuleParser.Parse(
            "SomeFutureField=1;GpuPreference=2;Malformed;Another=keep;");

        string result = RegistryRuleSerializer.SetSpecificAdapter(source, "1002&73EF&1EFE");

        Assert.Equal(
            "SomeFutureField=1;SpecificAdapter=1002&73EF&1EFE;GpuPreference=1073741824;Malformed;Another=keep;",
            result);
    }

    [Fact]
    public void ClearingGpuFieldsKeepsUnknownFieldsAndTheirOriginalText()
    {
        GpuPreferenceRule source = RegistryRuleParser.Parse(
            " SpecificAdapter =old;Unknown = value ;GpuPreference=1073741824;Malformed;");

        string result = RegistryRuleSerializer.ClearGpuPreference(source);

        Assert.Equal("Unknown = value ;Malformed;", result);
    }

    [Theory]
    [InlineData(GpuPreferenceKind.GenericPowerSaving, "GpuPreference=1;Future=yes;")]
    [InlineData(GpuPreferenceKind.GenericHighPerformance, "GpuPreference=2;Future=yes;")]
    public void SetsGenericPreferenceAndKeepsUnknownField(GpuPreferenceKind kind, string expected)
    {
        GpuPreferenceRule source = RegistryRuleParser.Parse("SpecificAdapter=old;Future=yes;GpuPreference=1073741824;");

        string result = RegistryRuleSerializer.SetGenericPreference(source, kind);

        Assert.Equal(expected, result);
    }
}
