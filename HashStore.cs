using System;
using System.IO;
using System.Text.Json;

namespace OpenCoffee;

/// <summary>
/// Persists and loads the machine session hash from hash.json.
/// The hash is a session token returned by the machine on first successful
/// authentication. Storing it avoids needing physical confirmation on the
/// machine for every connection.
/// </summary>
public static class HashStore
{
    private static readonly string DefaultPath = Path.Combine(
        AppContext.BaseDirectory, "hash.json");

    private class HashData
    {
        public string Hash { get; set; } = "";
    }

    /// <summary>Load the stored hash, or return empty string if not found.</summary>
    public static string Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return "";

        try
        {
            string json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<HashData>(json);
            return data?.Hash ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Save the hash to disk. Only writes if the value is non-empty.</summary>
    public static void Save(string hash, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(hash)) return;

        path ??= DefaultPath;
        var data = new HashData { Hash = hash };
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
