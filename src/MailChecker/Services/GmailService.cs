using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Google.Apis.Gmail.v1.Data;
using MailChecker.Configuration;
using MailChecker.Models;
using GoogleGmailService = Google.Apis.Gmail.v1.GmailService;

namespace MailChecker.Services;

public sealed partial class GmailService : IMailProvider
{
    private const string InboxLabelId = "INBOX";
    private const string UnreadLabelId = "UNREAD";

    private readonly GmailConfig _config;
    private GoogleGmailService? _client;
    private string _billLabelId = "";
    private string _userEmail = "";

    public GmailService(GmailConfig config)
    {
        _config = config;
    }

    public string ProviderKey => "gmail";

    public string DisplayName => "Google 帳號";

    public async Task<string> InitializeAsync(CancellationToken ct)
    {
        _client = await new GmailAuthProvider(_config).CreateClientAsync(ct);

        var profile = await _client.Users.GetProfile("me").ExecuteAsync(ct);
        _userEmail = profile?.EmailAddress ?? "(unknown user)";

        _billLabelId = await EnsureBillLabelAsync(ct);
        return _userEmail;
    }

    public async IAsyncEnumerable<MailItem> EnumerateInboxAsync(
        DateTimeOffset? receivedAfter,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        EnsureInitialized();

        string? query = null;
        if (receivedAfter is { } cutoff)
        {
            query = $"after:{cutoff.ToUnixTimeSeconds()}";
        }

        string? pageToken = null;
        do
        {
            var listRequest = _client!.Users.Messages.List("me");
            listRequest.LabelIds = new Google.Apis.Util.Repeatable<string>(new[] { InboxLabelId });
            listRequest.MaxResults = _config.PageSize;
            listRequest.PageToken = pageToken;
            if (query is not null)
            {
                listRequest.Q = query;
            }

            var listResponse = await listRequest.ExecuteAsync(ct);

            if (listResponse?.Messages is { Count: > 0 } refs)
            {
                foreach (var msgRef in refs)
                {
                    ct.ThrowIfCancellationRequested();
                    var getRequest = _client.Users.Messages.Get("me", msgRef.Id);
                    getRequest.Format = Google.Apis.Gmail.v1.UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
                    var msg = await getRequest.ExecuteAsync(ct);

                    if (TryProject(msg) is { } item)
                    {
                        yield return item;
                    }
                }
            }

            pageToken = listResponse?.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));
    }

    public async Task ProcessBillAsync(MailItem mail, CancellationToken ct)
    {
        EnsureInitialized();

        var addLabels = new List<string>();
        var removeLabels = new List<string>();

        if (!mail.HasLabel(_billLabelId))
        {
            addLabels.Add(_billLabelId);
        }

        if (mail.HasLabel(InboxLabelId))
        {
            removeLabels.Add(InboxLabelId);
        }

        if (mail.HasLabel(UnreadLabelId))
        {
            removeLabels.Add(UnreadLabelId);
        }

        if (addLabels.Count == 0 && removeLabels.Count == 0)
        {
            return;
        }

        var modifyRequest = new ModifyMessageRequest
        {
            AddLabelIds = addLabels.Count > 0 ? addLabels : null,
            RemoveLabelIds = removeLabels.Count > 0 ? removeLabels : null,
        };

        await _client!.Users.Messages.Modify(modifyRequest, "me", mail.Id).ExecuteAsync(ct);
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<string> EnsureBillLabelAsync(CancellationToken ct)
    {
        var name = _config.BillLabelName;
        var labels = await _client!.Users.Labels.List("me").ExecuteAsync(ct);
        var existing = labels?.Labels?
            .FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.Ordinal));

        if (existing?.Id is { Length: > 0 } id)
        {
            return id;
        }

        var created = await _client.Users.Labels.Create(new Label
        {
            Name = name,
            LabelListVisibility = "labelShow",
            MessageListVisibility = "show",
        }, "me").ExecuteAsync(ct);

        if (created?.Id is null)
        {
            throw new InvalidOperationException($"Failed to create Gmail label '{name}'.");
        }

        return created.Id;
    }

    private MailItem? TryProject(Message msg)
    {
        if (msg.Id is null)
        {
            return null;
        }

        var headers = msg.Payload?.Headers ?? new List<MessagePartHeader>();
        var subject = HeaderValue(headers, "Subject");
        var fromRaw = HeaderValue(headers, "From");
        var (address, name) = ParseFromHeader(fromRaw);

        var (rawBody, isHtml) = ExtractBody(msg.Payload);
        var plainBody = isHtml ? HtmlToText.Convert(rawBody) : rawBody.Trim();

        var labels = (IReadOnlyList<string>?)msg.LabelIds?.ToList() ?? Array.Empty<string>();
        var isRead = !labels.Contains(UnreadLabelId, StringComparer.Ordinal);

        DateTimeOffset? received = msg.InternalDate.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(msg.InternalDate.Value)
            : null;

        return new MailItem(
            Id: msg.Id,
            SenderName: name,
            SenderAddress: address,
            Subject: subject,
            BodyPreview: msg.Snippet ?? "",
            PlainBody: plainBody,
            ReceivedDateTime: received,
            IsRead: isRead,
            ParentFolderId: "",
            Labels: labels);
    }

    private static string HeaderValue(IList<MessagePartHeader> headers, string name) =>
        headers.FirstOrDefault(h =>
            string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value ?? "";

    [GeneratedRegex(@"^\s*(?:""?([^""<]*?)""?)\s*<\s*([^>]+?)\s*>\s*$")]
    private static partial Regex NamedAddressRegex();

    private static (string address, string name) ParseFromHeader(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ("", "");
        }

        var match = NamedAddressRegex().Match(raw);
        if (match.Success)
        {
            return (match.Groups[2].Value.Trim(), match.Groups[1].Value.Trim());
        }

        return (raw.Trim(), "");
    }

    private static (string body, bool isHtml) ExtractBody(MessagePart? payload)
    {
        if (payload is null)
        {
            return ("", false);
        }

        var direct = TryDecodePartBody(payload);
        if (direct is not null)
        {
            var isHtmlDirect = string.Equals(payload.MimeType, "text/html", StringComparison.OrdinalIgnoreCase);
            return (direct, isHtmlDirect);
        }

        if (payload.Parts is null || payload.Parts.Count == 0)
        {
            return ("", false);
        }

        var plain = FindPart(payload.Parts, "text/plain");
        var plainBody = plain is null ? null : TryDecodePartBody(plain);
        if (!string.IsNullOrEmpty(plainBody))
        {
            return (plainBody, false);
        }

        var html = FindPart(payload.Parts, "text/html");
        var htmlBody = html is null ? null : TryDecodePartBody(html);
        if (!string.IsNullOrEmpty(htmlBody))
        {
            return (htmlBody, true);
        }

        return ("", false);
    }

    private static MessagePart? FindPart(IList<MessagePart> parts, string mimeType)
    {
        foreach (var part in parts)
        {
            if (string.Equals(part.MimeType, mimeType, StringComparison.OrdinalIgnoreCase))
            {
                return part;
            }
        }

        foreach (var part in parts)
        {
            if (part.Parts is { Count: > 0 } children)
            {
                var nested = FindPart(children, mimeType);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? TryDecodePartBody(MessagePart part)
    {
        var data = part.Body?.Data;
        if (string.IsNullOrEmpty(data))
        {
            return null;
        }

        var normalized = data.Replace('-', '+').Replace('_', '/');
        switch (normalized.Length % 4)
        {
            case 2: normalized += "=="; break;
            case 3: normalized += "="; break;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private void EnsureInitialized()
    {
        if (_client is null)
        {
            throw new InvalidOperationException(
                $"{nameof(GmailService)} not initialized. Call InitializeAsync first.");
        }
    }
}
