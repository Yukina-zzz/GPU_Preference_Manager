using GpuPreferenceManager.Core.Adapters;

namespace GpuPreferenceManager.Core.Tests;

public sealed class SpecificAdapterKeyTests
{
    [Theory]
    [InlineData(0x1002u, 0x164Eu, 0x164E1002u, "1002&164E&164E1002")]
    [InlineData(0x1002u, 0x73EFu, 0x00001EFEu, "1002&73EF&1EFE")]
    [InlineData(0x10DEu, 0x2684u, 0x145A10DEu, "10DE&2684&145A10DE")]
    [InlineData(0x8086u, 0xA780u, 0u, "8086&A780&0")]
    public void BuildsCanonicalKey(uint vendor, uint device, uint subsystem, string expected)
    {
        Assert.Equal(expected, SpecificAdapterKey.Build(vendor, device, subsystem));
    }

    [Fact]
    public void MatchesCaseInsensitivelyButRequiresCompleteKey()
    {
        Assert.True(SpecificAdapterKey.Matches("1002&73EF&1EFE", "1002&73ef&1efe"));
        Assert.False(SpecificAdapterKey.Matches("1002&73EF", "1002&73EF&1EFE"));
    }
}
