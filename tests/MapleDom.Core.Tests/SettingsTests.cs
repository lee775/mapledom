using MapleDom.Core.Notifications;
using MapleDom.Core.Settings;
using Xunit;

namespace MapleDom.Core.Tests;

public class SettingsTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "mapledom-tests", Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Roundtrip_through_file_preserves_values_and_uses_enum_names()
    {
        var path = TempPath();
        try
        {
            var s = AppSettings.CreateDefault();
            s.Rune.IntervalSeconds = 720;
            s.Doping.Notification.Sound = AlarmSound.Alarm2;
            s.Dongkkop.AutoStart = true;
            SettingsStore.Save(s, path);

            var loaded = SettingsStore.Load(path);

            Assert.Equal(720, loaded.Rune.IntervalSeconds);
            Assert.Equal(AlarmSound.Alarm2, loaded.Doping.Notification.Sound);
            Assert.True(loaded.Dongkkop.AutoStart);
            Assert.Equal("동꼽방지", loaded.Dongkkop.Name);
            Assert.Contains("\"Alarm2\"", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = SettingsStore.Load(TempPath());

        Assert.Equal(58, s.Dongkkop.IntervalSeconds);
        Assert.Equal(2, s.Dongkkop.GapSeconds);
        Assert.Equal(900, s.Rune.IntervalSeconds);
        Assert.Equal(1800, s.Doping.IntervalSeconds);
        Assert.Equal(5, s.Doping.LeadSeconds);
        Assert.Equal(60, s.Rune.ReminderSeconds);
        Assert.Equal(2, s.Notifications.MinSpacingSeconds);
    }

    [Fact]
    public void Corrupt_file_reports_error_and_gives_defaults()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not json");
        try
        {
            var s = SettingsStore.LoadOrDefault(path, out var error);

            Assert.NotNull(error);
            Assert.Equal(900, s.Rune.IntervalSeconds);
            Assert.Throws<System.Text.Json.JsonException>(() => SettingsStore.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Partial_json_is_normalized_with_defaults()
    {
        var s = SettingsStore.Deserialize("{\"Rune\":{\"IntervalSeconds\":600},\"Doping\":null}");

        Assert.Equal(600, s.Rune.IntervalSeconds);
        Assert.Equal(60, s.Rune.ReminderSeconds);
        Assert.Equal("룬", s.Rune.Name);
        Assert.Equal("룬 타이머", s.Rune.Notification.Title);
        Assert.Equal(58, s.Dongkkop.IntervalSeconds);
        Assert.Equal(1800, s.Doping.IntervalSeconds);
        Assert.Empty(s.Validate());
    }

    [Fact]
    public void Validate_reports_lead_not_smaller_than_interval()
    {
        var s = AppSettings.CreateDefault();
        s.Doping.LeadSeconds = 1800;

        var errors = s.Validate();

        Assert.Single(errors);
        Assert.Contains("도핑", errors[0]);
    }

    [Fact]
    public void Validate_reports_multiple_errors()
    {
        var s = AppSettings.CreateDefault();
        s.Dongkkop.GapSeconds = -1;
        s.Rune.IntervalSeconds = 100_000;
        s.Notifications.MinSpacingSeconds = -1;

        Assert.Equal(3, s.Validate().Count);
    }

    [Fact]
    public void Clone_is_deep()
    {
        var s = AppSettings.CreateDefault();
        var c = s.Clone();

        c.Rune.IntervalSeconds = 1;
        c.Rune.Notification.Title = "x";

        Assert.Equal(900, s.Rune.IntervalSeconds);
        Assert.Equal("룬 타이머", s.Rune.Notification.Title);
    }
}
