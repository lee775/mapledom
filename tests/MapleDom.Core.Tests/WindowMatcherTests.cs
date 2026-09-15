using MapleDom.Core.Settings;
using MapleDom.Core.Windowing;
using Xunit;

namespace MapleDom.Core.Tests;

public class WindowMatcherTests
{
    private static WindowInfo W(int handle, string title, string process, bool topmost = false)
        => new(handle, title, process, handle * 10, topmost);

    private static WindowSelector S(string process, string title = "")
        => new() { ProcessName = process, TitleContains = title };

    [Fact]
    public void Matches_process_case_insensitively_and_ignores_exe_suffix()
    {
        var window = W(1, "MapleStory", "MapleStory");

        Assert.True(WindowMatcher.Matches(S("maplestory"), window));
        Assert.True(WindowMatcher.Matches(S("MapleStory.exe"), window));
        Assert.False(WindowMatcher.Matches(S("notepad"), window));
    }

    [Fact]
    public void Matches_title_substring_case_insensitively()
    {
        var window = W(1, "MapleStory - 리부트", "MapleStory");

        Assert.True(WindowMatcher.Matches(S("", "리부트"), window));
        Assert.True(WindowMatcher.Matches(S("maplestory", "maplestory -"), window));
        Assert.False(WindowMatcher.Matches(S("maplestory", "오로라"), window));
    }

    [Fact]
    public void Empty_selector_never_matches_and_reports_empty()
    {
        Assert.True(S("", "").IsEmpty);
        Assert.True(S("  ", "  ").IsEmpty);
        Assert.False(WindowMatcher.Matches(S("", ""), W(1, "x", "x")));
    }

    [Fact]
    public void Find_returns_first_match_in_z_order_or_null()
    {
        var windows = new[]
        {
            W(1, "Chrome - 유튜브", "chrome"),
            W(2, "MapleStory", "MapleStory"),
            W(3, "Chrome - 인벤", "chrome"),
        };

        Assert.Equal(2, WindowMatcher.Find(S("maplestory"), windows)!.Handle);
        Assert.Equal(3, WindowMatcher.Find(S("chrome", "인벤"), windows)!.Handle);
        Assert.Null(WindowMatcher.Find(S("notepad"), windows));
    }

    [Fact]
    public void Resolve_keeps_order_and_never_assigns_one_window_twice()
    {
        var windows = new[]
        {
            W(1, "Chrome - 유튜브", "chrome"),
            W(2, "MapleStory", "MapleStory"),
            W(3, "Chrome - 인벤", "chrome"),
        };
        var selectors = new[] { S("chrome", "인벤"), S("chrome"), S("maplestory"), S("maplestory") };

        var resolved = WindowMatcher.Resolve(selectors, windows);

        Assert.Equal(new nint?[] { 3, 1, 2, null }, resolved.Select(r => r.Window?.Handle));
        Assert.Same(selectors[3], resolved[3].Selector);
        Assert.False(resolved[3].Found);
    }

    [Fact]
    public void SelectorFor_uses_process_name_and_full_title()
    {
        var selector = WindowMatcher.SelectorFor(W(7, "MapleStory - 스카니아", "MapleStory.exe"));

        Assert.Equal("MapleStory", selector.ProcessName);
        Assert.Equal("MapleStory - 스카니아", selector.TitleContains);
    }

    [Fact]
    public void Settings_roundtrip_trims_and_ignores_computed_fields()
    {
        var s = AppSettings.CreateDefault();
        s.WindowOrder.ApplyOnStart = true;
        s.WindowOrder.Topmost = true;
        s.WindowOrder.Windows.Add(S(" chrome ", " 인벤 "));
        s.WindowOrder.Windows.Add(S("MapleStory"));

        var json = SettingsStore.Serialize(s);
        var loaded = SettingsStore.Deserialize(json);

        Assert.DoesNotContain("IsEmpty", json);
        Assert.True(loaded.WindowOrder.ApplyOnStart);
        Assert.True(loaded.WindowOrder.Topmost);
        Assert.Equal(2, loaded.WindowOrder.Windows.Count);
        Assert.Equal("chrome", loaded.WindowOrder.Windows[0].ProcessName);
        Assert.Equal("인벤", loaded.WindowOrder.Windows[0].TitleContains);
        Assert.Empty(loaded.Validate());
    }

    [Fact]
    public void Validate_rejects_empty_entries_and_too_many()
    {
        var s = AppSettings.CreateDefault();
        s.WindowOrder.Windows.Add(S("", ""));
        Assert.Contains(s.Validate(), e => e.Contains("1번"));

        s.WindowOrder.Windows.Clear();
        for (var i = 0; i <= WindowOrderSettings.MaxEntries; i++) s.WindowOrder.Windows.Add(S("p" + i));
        Assert.Contains(s.Validate(), e => e.Contains("최대"));
    }

    [Fact]
    public void Missing_or_null_window_order_section_defaults_to_empty()
    {
        var s = SettingsStore.Deserialize("{\"WindowOrder\":null}");

        Assert.NotNull(s.WindowOrder);
        Assert.Empty(s.WindowOrder.Windows);
        Assert.False(s.WindowOrder.ApplyOnStart);
        Assert.Empty(s.Validate());
    }

    [Fact]
    public void Old_settings_file_with_dongkkop_section_still_loads()
    {
        var s = SettingsStore.Deserialize("{\"SchemaVersion\":1,\"Dongkkop\":{\"Name\":\"동꼽방지\",\"IntervalSeconds\":58},\"Rune\":{\"IntervalSeconds\":600}}");

        Assert.Equal(600, s.Rune.IntervalSeconds);
        Assert.Equal(AppSettings.CurrentSchemaVersion, s.SchemaVersion);
        Assert.Empty(s.Validate());
    }
}
