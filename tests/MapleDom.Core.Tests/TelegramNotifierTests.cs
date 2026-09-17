using MapleDom.Core.Notifications;
using MapleDom.Core.Settings;
using MapleDom.Core.Timers;
using Xunit;

namespace MapleDom.Core.Tests;

public class TelegramNotifierTests
{
    private static TelegramSettings Configured() => new()
    {
        Enabled = true,
        BotToken = "123456:token",
        ChatId = "99",
    };

    private static AlarmNotification N(string timerId, string title = "제목", string message = "본문")
        => new(timerId, timerId, AlarmKind.Main, title, message, AlarmSound.Default, AlarmScenario.Default, null);

    [Fact]
    public void Disabled_or_unconfigured_never_sends()
    {
        var off = Configured();
        off.Enabled = false;
        Assert.False(TelegramNotifier.ShouldSend(off, TimerService.RuneId));

        var noToken = Configured();
        noToken.BotToken = "  ";
        Assert.False(noToken.IsConfigured);
        Assert.False(TelegramNotifier.ShouldSend(noToken, TimerService.RuneId));

        var noChat = Configured();
        noChat.ChatId = "";
        Assert.False(TelegramNotifier.ShouldSend(noChat, TimerService.RuneId));
    }

    [Fact]
    public void Per_timer_toggles_are_respected()
    {
        var s = Configured();

        Assert.False(TelegramNotifier.ShouldSend(s, TimerService.MinuteId));   // 기본 끔
        Assert.True(TelegramNotifier.ShouldSend(s, TimerService.RuneId));
        Assert.True(TelegramNotifier.ShouldSend(s, TimerService.DopingId));
        Assert.True(TelegramNotifier.ShouldSend(s, "selftest"));               // 테스트 알림은 항상

        s.SendMinute = true;
        s.SendRune = false;
        Assert.True(TelegramNotifier.ShouldSend(s, TimerService.MinuteId));
        Assert.False(TelegramNotifier.ShouldSend(s, TimerService.RuneId));
    }

    [Fact]
    public void FormatText_includes_title_and_message()
    {
        var text = TelegramNotifier.FormatText(N(TimerService.RuneId, "룬 타이머", "룬 생성!"));

        Assert.Contains("룬 타이머", text);
        Assert.Contains("룬 생성!", text);
        Assert.DoesNotContain("\n\n", text);
    }

    [Fact]
    public void SendText_rejects_empty_token_or_chat_without_calling_network()
    {
        Assert.False(TelegramNotifier.SendText("", "1", "x").Ok);
        Assert.False(TelegramNotifier.SendText("123:abc", " ", "x").Ok);
        Assert.False(TelegramNotifier.FindChatId("").Ok);
    }

    [Fact]
    public void Notifier_drops_messages_when_disabled()
    {
        using var notifier = new TelegramNotifier();
        var off = Configured();
        off.Enabled = false;
        notifier.Update(off);

        notifier.Notify(N(TimerService.RuneId));   // 큐에 들어가지 않으므로 네트워크 호출 없음

        Assert.False(notifier.Settings.Enabled);
    }

    [Fact]
    public void Settings_roundtrip_keeps_token_and_toggles()
    {
        var s = AppSettings.CreateDefault();
        s.Telegram.Enabled = true;
        s.Telegram.BotToken = "  123:abc  ";
        s.Telegram.ChatId = " 42 ";
        s.Telegram.SendMinute = true;

        var loaded = SettingsStore.Deserialize(SettingsStore.Serialize(s));

        Assert.True(loaded.Telegram.Enabled);
        Assert.Equal("123:abc", loaded.Telegram.BotToken);
        Assert.Equal("42", loaded.Telegram.ChatId);
        Assert.True(loaded.Telegram.SendMinute);
        Assert.True(loaded.Telegram.SendRune);
        Assert.Empty(loaded.Validate());
    }

    [Fact]
    public void Validate_requires_token_and_chat_when_enabled()
    {
        var s = AppSettings.CreateDefault();
        s.Telegram.Enabled = true;

        var errors = s.Validate();

        Assert.Contains(errors, e => e.Contains("봇 토큰"));
        Assert.Contains(errors, e => e.Contains("채팅 ID"));

        s.Telegram.BotToken = "no-colon";
        s.Telegram.ChatId = "42";
        Assert.Contains(s.Validate(), e => e.Contains("형식"));
    }

    [Fact]
    public void Disabled_telegram_needs_no_token()
    {
        var s = AppSettings.CreateDefault();

        Assert.False(s.Telegram.Enabled);
        Assert.Empty(s.Validate());
    }

    [Fact]
    public void Missing_section_defaults_to_disabled()
    {
        var s = SettingsStore.Deserialize("{\"Telegram\":null}");

        Assert.NotNull(s.Telegram);
        Assert.False(s.Telegram.Enabled);
        Assert.False(s.Telegram.IsConfigured);
    }
}
