using System.Net.Http.Headers;
using System.Net.Http.Json;
using MailChecker.Configuration;
using MailChecker.Models;

namespace MailChecker.Services;

public sealed class LineMessagingService : IDisposable
{
    private const string PushEndpoint = "https://api.line.me/v2/bot/message/push";
    private const int LineTextLimit = 4900;

    private readonly LineConfig _config;
    private readonly HttpClient _http;

    public LineMessagingService(LineConfig config, HttpClient? http = null)
    {
        _config = config;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config.ChannelAccessToken) &&
        !string.IsNullOrWhiteSpace(_config.UserId);

    public async Task NotifyBillAsync(MailItem mail, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            Console.WriteLine("  ! LINE not configured — skipping notification.");
            return;
        }

        var text = BuildText(mail);

        using var req = new HttpRequestMessage(HttpMethod.Post, PushEndpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ChannelAccessToken);
        req.Content = JsonContent.Create(new
        {
            to = _config.UserId,
            messages = new[]
            {
                new { type = "text", text }
            }
        });

        using var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"LINE push failed: {(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
        }
    }

    private string BuildText(MailItem mail)
    {
        var preview = Truncate(
            string.IsNullOrWhiteSpace(mail.PlainBody) ? mail.BodyPreview : mail.PlainBody,
            Math.Max(40, _config.BodyPreviewMaxChars));

        var received = mail.ReceivedDateTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "(no date)";

        var text =
            $"[帳單提醒] 記得繳費\n" +
            $"寄件人：{mail.DisplaySender}\n" +
            $"標題：{mail.Subject}\n" +
            $"收件時間：{received}\n" +
            $"——\n" +
            $"{preview}";

        return text.Length > LineTextLimit ? text[..LineTextLimit] : text;
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "(無內文)";
        }

        var collapsed = text.Replace("\r\n", "\n").Trim();
        return collapsed.Length <= max ? collapsed : collapsed[..max] + "…";
    }

    public void Dispose() => _http.Dispose();
}
