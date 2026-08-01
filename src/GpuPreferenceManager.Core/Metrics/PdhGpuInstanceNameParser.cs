using System.Globalization;
using System.Text.RegularExpressions;

namespace GpuPreferenceManager.Core.Metrics;

public static partial class PdhGpuInstanceNameParser
{
    [GeneratedRegex(
        "^pid_(?<pid>[0-9]+)_luid_0x(?<first>[0-9a-f]+)_0x(?<second>[0-9a-f]+)_phys_(?<phys>[0-9]+)(?:_eng_(?<engine>[0-9]+)_engtype_(?<type>.+?))?(?:#(?<suffix>[0-9]+))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstancePattern();

    public static bool TryParse(string? instanceName, out PdhGpuInstance? instance)
    {
        instance = null;
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            return false;
        }

        Match match = InstancePattern().Match(instanceName);
        if (!match.Success
            || !int.TryParse(match.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
            || !uint.TryParse(match.Groups["first"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint first)
            || !uint.TryParse(match.Groups["second"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint second)
            || !int.TryParse(match.Groups["phys"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int physical))
        {
            return false;
        }

        int? engine = TryParseOptionalInt(match.Groups["engine"]);
        int? suffix = TryParseOptionalInt(match.Groups["suffix"]);
        string? engineType = match.Groups["type"].Success ? match.Groups["type"].Value : null;
        instance = new(pid, first, second, physical, engine, engineType, suffix, instanceName);
        return true;
    }

    private static int? TryParseOptionalInt(Group group) =>
        group.Success
        && int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
}
