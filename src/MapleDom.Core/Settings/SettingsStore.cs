using System.Text.Json;
using System.Text.Json.Serialization;

namespace MapleDom.Core.Settings;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}

/// <summary>설정 파일 입출력. 기본 위치: %LocalAppData%\MapleDom\settings.json</summary>
public static class SettingsStore
{
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDom");

    public static string DefaultPath => Path.Combine(DefaultDirectory, "settings.json");

    public static string Serialize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
    }

    /// <summary>역직렬화 후 <see cref="AppSettings.Normalize"/>까지 적용한다. 잘못된 JSON이면 <see cref="JsonException"/>.</summary>
    public static AppSettings Deserialize(string json)
    {
        var settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        return settings.Normalize();
    }

    /// <summary>파일이 없으면 기본값. 파일이 깨졌거나 읽을 수 없으면 예외.</summary>
    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return AppSettings.CreateDefault();
        return Deserialize(File.ReadAllText(path));
    }

    /// <summary>실패해도 예외를 던지지 않고 기본값을 돌려준다. 실패 원인은 error로 전달.</summary>
    public static AppSettings LoadOrDefault(string? path, out Exception? error)
    {
        try
        {
            error = null;
            return Load(path);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            error = ex;
            return AppSettings.CreateDefault();
        }
    }

    /// <summary>임시 파일에 쓴 뒤 교체(원자적). 디렉터리는 자동 생성.</summary>
    public static void Save(AppSettings settings, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        path ??= DefaultPath;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(settings));
        File.Move(temp, path, overwrite: true);
    }
}
