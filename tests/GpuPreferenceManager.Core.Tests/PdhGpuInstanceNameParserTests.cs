using GpuPreferenceManager.Core.Metrics;

namespace GpuPreferenceManager.Core.Tests;

public sealed class PdhGpuInstanceNameParserTests
{
    [Theory]
    [InlineData("pid_1234_luid_0x00000000_0x0000ABCD_phys_0", 1234, null, null)]
    [InlineData("PID_7_LUID_0x1_0x2_PHYS_3_ENG_1_ENGTYPE_3D", 7, 1, "3D")]
    [InlineData("pid_8_luid_0x0_0x2_phys_0_eng_2_engtype_Copy#1", 8, 2, "Copy")]
    [InlineData("pid_9_luid_0x0_0x2_phys_0_eng_3_engtype_VideoDecode", 9, 3, "VideoDecode")]
    [InlineData("pid_10_luid_0x0_0x2_phys_0_eng_4_engtype_Compute", 10, 4, "Compute")]
    public void ParsesMemoryAndEngineInstances(string raw, int pid, int? engine, string? engineType)
    {
        bool success = PdhGpuInstanceNameParser.TryParse(raw, out PdhGpuInstance? result);

        Assert.True(success);
        Assert.Equal(pid, result!.ProcessId);
        Assert.Equal(engine, result.EngineIndex);
        Assert.Equal(engineType, result.EngineType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pid_nope")]
    [InlineData("pid_1_luid_X_Y_phys_0")]
    public void RejectsInvalidInput(string? raw)
    {
        Assert.False(PdhGpuInstanceNameParser.TryParse(raw, out _));
    }
}
