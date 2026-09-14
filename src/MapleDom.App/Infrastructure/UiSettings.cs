using System.IO;
using System.Text.Json;
using MapleDom.Core.Settings;

namespace MapleDom.App.Infrastructure;

public sealed class UiSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Topmost { get; set; } = true;
    public double Opacity { get; set; } = 1;
    public bool MinimizeToTray { get; set; } = true;
    public bool HotkeysEnabled { get; set; }

    public UiSettings Clone() => (UiSettings)MemberwiseClone();

    public static UiSettings Load(out Exception? error, string? path = null)
    {
        try
        {
            error = null;
            path ??= Path.Combine(SettingsStore.DefaultDirectory, "ui.json");
            var settings = File.Exists(path)
                ? JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(path)) ?? new()
                : new UiSettings();
            if (settings.Left is { } left && !double.IsFinite(left)) settings.Left = null;
            if (settings.Top is { } top && !double.IsFinite(top)) settings.Top = null;
            settings.Opacity = double.IsFinite(settings.Opacity) ? Math.Clamp(settings.Opacity, 0.4, 1) : 1;
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = ex;
            return new UiSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= Path.Combine(SettingsStore.DefaultDirectory, "ui.json");
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }
}

internal static class ErrorLog
{
    public static void Write(Exception error)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DefaultDirectory);
            File.AppendAllText(Path.Combine(SettingsStore.DefaultDirectory, "error.log"),
                $"[{DateTimeOffset.Now:O}] {error}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"오류 로그를 기록하지 못했습니다: {ex}");
        }
    }
}
