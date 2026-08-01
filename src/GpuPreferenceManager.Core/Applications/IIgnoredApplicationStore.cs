namespace GpuPreferenceManager.Core.Applications;

public interface IIgnoredApplicationStore
{
    Task<IReadOnlySet<string>> ReadAllAsync(CancellationToken cancellationToken);

    Task SetIgnoredAsync(string executablePath, bool ignored, CancellationToken cancellationToken);
}
