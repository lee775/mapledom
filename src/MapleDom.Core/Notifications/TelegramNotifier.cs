using System.Net.Http;
using System.Text.Json;
using MapleDom.Core.Settings;

namespace MapleDom.Core.Notifications;

/// <summary>텔레그램 API 호출 결과. 실패해도 예외 대신 이 값으로 알린다(UI에 그대로 보여 준다).</summary>
public sealed record TelegramResult(bool Ok, string Message, string? ChatId = null);

/// <summary>
/// 알림을 텔레그램 봇 메시지로도 보낸다. Windows 토스트와 함께 <see cref="CompositeNotifier"/>에 넣어 쓴다.
/// <para>
/// 전송은 내부 큐(전용 스레드)에서 이뤄지므로 <see cref="Notify"/>는 즉시 돌아온다.
/// 네트워크가 느리거나 끊겨도 토스트 알림이 밀리지 않는다.
/// </para>
/// </summary>
public sealed class TelegramNotifier : IAlarmNotifier, IDisposable
{
    private const string ApiBase = "https://api.telegram.org/bot";

    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly NotificationDispatcher _queue;
    private readonly object _gate = new();
    private TelegramSettings _settings = new();
    private bool _disposed;

    public TelegramNotifier()
    {
        _queue = new NotificationDispatcher(new Sender(this), TimeSpan.Zero, maxQueueLength: 32);
        _queue.DeliveryFailed += (notification, error) => SendFailed?.Invoke(notification, error);
    }

    /// <summary>전송 실패(네트워크, 잘못된 토큰 등). 큐 워커 스레드에서 발생.</summary>
    public event Action<AlarmNotification, Exception>? SendFailed;

    public TelegramSettings Settings
    {
        get { lock (_gate) return _settings.Clone(); }
    }

    /// <summary>설정을 실행 중에 바꾼다(토큰, 채팅 ID, 타이머별 on/off).</summary>
    public void Update(TelegramSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate) _settings = settings.Clone();
    }

    public void Notify(AlarmNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        TelegramSettings settings;
        lock (_gate)
        {
            if (_disposed) return;
            settings = _settings;
        }

        if (!ShouldSend(settings, notification.TimerId)) return;
        _queue.Notify(notification);
    }

    /// <summary>이 설정으로 이 타이머의 알림을 보낼지.</summary>
    public static bool ShouldSend(TelegramSettings settings, string timerId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Enabled || !settings.IsConfigured) return false;
        return timerId switch
        {
            "minute" => settings.SendMinute,
            "rune" => settings.SendRune,
            "doping" => settings.SendDoping,
            _ => true,   // 테스트 알림 등
        };
    }

    public static string FormatText(AlarmNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var time = DateTime.Now.ToString("HH:mm:ss");
        return string.IsNullOrWhiteSpace(notification.Message)
            ? $"[{time}] {notification.Title}"
            : $"[{time}] {notification.Title}\n{notification.Message}";
    }

    /// <summary>메시지 1건 전송(동기). 호출한 스레드를 블록하므로 UI 스레드에서 직접 부르지 말 것.</summary>
    public static TelegramResult SendText(string botToken, string chatId, string text)
    {
        if (string.IsNullOrWhiteSpace(botToken)) return new TelegramResult(false, "봇 토큰이 비어 있습니다.");
        if (string.IsNullOrWhiteSpace(chatId)) return new TelegramResult(false, "채팅 ID가 비어 있습니다.");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chat_id"] = chatId.Trim(),
            ["text"] = text,
        });

        try
        {
            using var response = SharedHttp.PostAsync($"{ApiBase}{botToken.Trim()}/sendMessage", form).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return response.IsSuccessStatusCode
                ? new TelegramResult(true, "보냈습니다.")
                : new TelegramResult(false, Describe(body, (int)response.StatusCode));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new TelegramResult(false, "전송 실패: " + ex.Message);
        }
    }

    /// <summary>봇이 받은 최근 메시지에서 채팅 ID를 찾는다. 사용자가 봇에게 먼저 말을 걸어야 한다.</summary>
    public static TelegramResult FindChatId(string botToken)
    {
        if (string.IsNullOrWhiteSpace(botToken)) return new TelegramResult(false, "봇 토큰을 먼저 입력하세요.");

        try
        {
            using var response = SharedHttp.GetAsync($"{ApiBase}{botToken.Trim()}/getUpdates").GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return new TelegramResult(false, Describe(body, (int)response.StatusCode));

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("result", out var updates) || updates.GetArrayLength() == 0)
                return new TelegramResult(false, "받은 메시지가 없습니다. 텔레그램에서 봇에게 아무 메시지나 보낸 뒤 다시 누르세요.");

            // 가장 최근 메시지의 채팅을 쓴다.
            for (var i = updates.GetArrayLength() - 1; i >= 0; i--)
            {
                var update = updates[i];
                foreach (var key in (ReadOnlySpan<string>)["message", "edited_message", "channel_post", "my_chat_member"])
                {
                    if (!update.TryGetProperty(key, out var message)) continue;
                    if (!message.TryGetProperty("chat", out var chat)) continue;
                    if (!chat.TryGetProperty("id", out var id)) continue;
                    var name = chat.TryGetProperty("title", out var title) ? title.GetString()
                        : chat.TryGetProperty("first_name", out var first) ? first.GetString()
                        : "";
                    return new TelegramResult(true, $"채팅 ID를 찾았습니다: {name}", id.ToString());
                }
            }

            return new TelegramResult(false, "메시지에서 채팅 ID를 찾지 못했습니다.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new TelegramResult(false, "조회 실패: " + ex.Message);
        }
    }

    private static string Describe(string body, int statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("description", out var description))
                return $"텔레그램 오류 {statusCode}: {description.GetString()}";
        }
        catch (JsonException)
        {
            // 본문이 JSON이 아니면 상태 코드만 알린다.
        }
        return $"텔레그램 오류 {statusCode}";
    }

    private void Send(AlarmNotification notification)
    {
        TelegramSettings settings;
        lock (_gate) settings = _settings;

        var result = SendText(settings.BotToken, settings.ChatId, FormatText(notification));
        if (!result.Ok) throw new InvalidOperationException(result.Message);
    }

    /// <summary>큐 워커가 실제 전송을 호출하는 통로.</summary>
    private sealed class Sender(TelegramNotifier owner) : IAlarmNotifier
    {
        public void Notify(AlarmNotification notification) => owner.Send(notification);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _queue.Dispose();
    }
}
