using MapleDom.Core.Settings;
using MapleDom.Core.WindowOrder;
using Xunit;

namespace MapleDom.Core.Tests;

public class WindowOrderResolverTests
{
    private static WindowInfo W(int handle, string title, string process, bool topmost = false)
        => new(handle, title, process, handle * 10, topmost);

    private static WindowOrderEntry E(string process, string title = "")
        => new() { ProcessName = process, TitleContains = title };

    [Fact]
    public void Matches_process_case_insensitively_and_ignores_exe_suffix()
    {
        var window = W(1, "MapleStory", "MapleStory");

        Assert.True(WindowOrderResolver.Matches(E("maplestory"), window));
        Assert.True(WindowOrderResolver.Matches(E("MapleStory.exe"), window));
        Assert.False(WindowOrderResolver.Matches(E("notepad"), window));
    }

    [Fact]
    public void Matches_title_substring_case_insensitively()
    {
        var window = W(1, "MapleStory - 리부트", "MapleStory");

        Assert.True(WindowOrderResolver.Matches(E("", "리부트"), window));
        Assert.True(WindowOrderResolver.Matches(E("maplestory", "maplestory -"), window));
        Assert.False(WindowOrderResolver.Matches(E("maplestory", "오로라"), window));
    }

    [Fact]
    public void Empty_entry_never_matches()
    {
        Assert.False(WindowOrderResolver.Matches(E("", ""), W(1, "x", "x")));
        Assert.False(WindowOrderResolver.Matches(E("  ", "  "), W(1, "x", "x")));
    }

    [Fact]
    public void Resolve_keeps_entry_order_and_takes_first_match_in_z_order()
    {
        var windows = new[]
        {
            W(1, "Chrome - 유튜브", "chrome"),
            W(2, "MapleStory", "MapleStory"),
            W(3, "Chrome - 인벤", "chrome"),
        };
        var entries = new[] { E("maplestory"), E("chrome", "인벤"), E("chrome") };

        var resolved = WindowOrderResolver.Resolve(entries, windows);

        Assert.Equal(new nint[] { 2, 3, 1 }, resolved.Select(r => r.Window!.Handle));
        Assert.All(resolved, r => Assert.True(r.Found));
    }

    [Fact]
    public void Resolve_does_not_assign_one_window_to_two_entries()
    {
        var windows = new[] { W(1, "MapleStory", "MapleStory") };
        var entries = new[] { E("maplestory"), E("maplestory") };

        var resolved = WindowOrderResolver.Resolve(entries, windows);

        Assert.True(resolved[0].Found);
        Assert.False(resolved[1].Found);
        Assert.Same(entries[1], resolved[1].Entry);
    }

    [Fact]
    public void EntryFor_uses_process_name_and_full_title()
    {
        var entry = WindowOrderResolver.EntryFor(W(7, "MapleStory - 스카니아", "MapleStory.exe"));

        Assert.Equal("MapleStory", entry.ProcessName);
        Assert.Equal("MapleStory - 스카니아", entry.TitleContains);
    }

    [Fact]
    public void Settings_roundtrip_and_validation()
    {
        var s = AppSettings.CreateDefault();
        s.WindowOrder.Enabled = true;
        s.WindowOrder.Topmost = true;
        s.WindowOrder.Windows.Add(E(" MapleStory ", " 스카니아 "));
        s.WindowOrder.Windows.Add(E("", ""));

        var loaded = SettingsStore.Deserialize(SettingsStore.Serialize(s));

        Assert.True(loaded.WindowOrder.Enabled);
        Assert.True(loaded.WindowOrder.Topmost);
        Assert.Equal(2, loaded.WindowOrder.Windows.Count);
        Assert.Equal("MapleStory", loaded.WindowOrder.Windows[0].ProcessName);
        Assert.Equal("스카니아", loaded.WindowOrder.Windows[0].TitleContains);
        var errors = loaded.Validate();
        Assert.Single(errors);
        Assert.Contains("2번", errors[0]);
    }

    [Fact]
    public void Missing_window_order_section_defaults_to_empty()
    {
        var s = SettingsStore.Deserialize("{\"WindowOrder\":null}");

        Assert.NotNull(s.WindowOrder);
        Assert.Empty(s.WindowOrder.Windows);
        Assert.False(s.WindowOrder.Enabled);
        Assert.Empty(s.Validate());
    }

    [Fact]
    public void Too_many_entries_are_rejected()
    {
        var s = AppSettings.CreateDefault();
        for (var i = 0; i <= WindowOrderSettings.MaxEntries; i++) s.WindowOrder.Windows.Add(E("p" + i));

        Assert.Contains(s.Validate(), e => e.Contains("최대"));
    }
}
