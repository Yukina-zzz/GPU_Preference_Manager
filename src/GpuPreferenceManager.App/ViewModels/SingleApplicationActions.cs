namespace GpuPreferenceManager.App.ViewModels;

public enum SinglePreferenceAction
{
    SpecificPowerSaving,
    SpecificHighPerformance,
    GenericPowerSaving,
    GenericHighPerformance,
    WindowsDecides,
}

public sealed record SinglePreferenceRequest(
    ApplicationRowViewModel Row,
    string ExecutablePath,
    SinglePreferenceAction Action);

public sealed record SingleIgnoreRequest(ApplicationRowViewModel Row, bool Ignored);
