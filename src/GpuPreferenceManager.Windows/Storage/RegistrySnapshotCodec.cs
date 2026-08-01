using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GpuPreferenceManager.Core.Registry;

namespace GpuPreferenceManager.Windows.Storage;

public static class RegistrySnapshotCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        WriteIndented = false,
    };

    public static string Serialize(RegistrySnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    public static RegistrySnapshot Deserialize(string json) =>
        JsonSerializer.Deserialize<RegistrySnapshot>(json, Options)
        ?? throw new InvalidDataException("baseline 注册表 JSON 无效。");

    public static string Hash(RegistrySnapshot snapshot) => Hash(Serialize(snapshot));

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
