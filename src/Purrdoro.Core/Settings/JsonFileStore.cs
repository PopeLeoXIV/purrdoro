using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Purrdoro.Core.Settings;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(DailyProgress))]
internal sealed partial class PurrdoroJsonContext : JsonSerializerContext;

public enum JsonLoadOutcome
{
    Loaded,
    Missing,
    Corrupt,
}

/// <summary>
/// Small, dependable JSON file persistence: source-generated System.Text.Json,
/// atomic writes (write to a temp file, then replace), and corrupt files are
/// moved aside instead of crashing the app.
/// </summary>
public sealed class JsonFileStore<T>
    where T : class
{
    private readonly JsonTypeInfo<T> _typeInfo;

    public JsonFileStore(string filePath, JsonTypeInfo<T> typeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
        _typeInfo = typeInfo ?? throw new ArgumentNullException(nameof(typeInfo));
    }

    public string FilePath { get; }

    public T? Load(out JsonLoadOutcome outcome)
    {
        string json;
        try
        {
            if (!File.Exists(FilePath))
            {
                outcome = JsonLoadOutcome.Missing;
                return null;
            }

            json = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[Purrdoro] Could not read {FilePath}: {ex.Message}");
            outcome = JsonLoadOutcome.Corrupt;
            return null;
        }

        try
        {
            var value = JsonSerializer.Deserialize(json, _typeInfo);
            if (value is not null)
            {
                outcome = JsonLoadOutcome.Loaded;
                return value;
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            Debug.WriteLine($"[Purrdoro] {FilePath} is malformed: {ex.Message}");
        }

        BackUpCorruptFile();
        outcome = JsonLoadOutcome.Corrupt;
        return null;
    }

    public bool Save(T value)
    {
        var tempPath = FilePath + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(tempPath, JsonSerializer.Serialize(value, _typeInfo));
            File.Move(tempPath, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[Purrdoro] Could not save {FilePath}: {ex.Message}");
            TryDelete(tempPath);
            return false;
        }
    }

    private void BackUpCorruptFile()
    {
        try
        {
            var backup = Path.ChangeExtension(FilePath, ".corrupt.json");
            File.Copy(FilePath, backup, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[Purrdoro] Could not back up corrupt file: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort only.
        }
    }
}
