using MailChecker.Configuration;
using MailChecker.Models;
using Microsoft.Graph;
using Microsoft.Graph.Me.Messages.Item.Move;
using Microsoft.Graph.Models;

namespace MailChecker.Services;

public sealed class GraphService
{
    private static readonly string[] MessageSelectFields =
    {
        "id", "subject", "bodyPreview", "body",
        "from", "sender", "receivedDateTime", "isRead", "parentFolderId"
    };

    private readonly GraphServiceClient _client;
    private readonly OutlookConfig _config;

    public GraphService(GraphServiceClient client, OutlookConfig config)
    {
        _client = client;
        _config = config;
    }

    public async Task<string> GetSignedInUserAsync(CancellationToken ct = default)
    {
        var me = await _client.Me.GetAsync(cancellationToken: ct);
        return me?.UserPrincipalName ?? me?.Mail ?? "(unknown user)";
    }

    public async Task<string> EnsureBillFolderAsync(CancellationToken ct = default)
    {
        var name = _config.BillFolderName;

        var existing = await _client.Me.MailFolders.GetAsync(req =>
        {
            req.QueryParameters.Filter = $"displayName eq '{EscapeOData(name)}'";
            req.QueryParameters.Top = 1;
        }, ct);

        var match = existing?.Value?.FirstOrDefault();
        if (match?.Id is { Length: > 0 } id)
        {
            return id;
        }

        var created = await _client.Me.MailFolders.PostAsync(new MailFolder
        {
            DisplayName = name,
            IsHidden = false
        }, cancellationToken: ct);

        if (created?.Id is null)
        {
            throw new InvalidOperationException($"Failed to create mail folder '{name}'.");
        }

        return created.Id;
    }

    public async IAsyncEnumerable<MailItem> EnumerateInboxAsync(
        DateTimeOffset? receivedAfter,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var filter = receivedAfter is { } cutoff
            ? $"receivedDateTime gt {cutoff.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}"
            : null;

        var page = await _client.Me.MailFolders["inbox"].Messages.GetAsync(req =>
        {
            req.QueryParameters.Top = _config.PageSize;
            req.QueryParameters.Select = MessageSelectFields;
            req.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
            if (filter is not null)
            {
                req.QueryParameters.Filter = filter;
            }
        }, ct);

        while (page is not null)
        {
            foreach (var msg in page.Value ?? Enumerable.Empty<Message>())
            {
                if (TryProject(msg) is { } item)
                {
                    yield return item;
                }
            }

            if (string.IsNullOrEmpty(page.OdataNextLink))
            {
                yield break;
            }

            page = await _client.Me.MailFolders["inbox"].Messages
                .WithUrl(page.OdataNextLink)
                .GetAsync(cancellationToken: ct);
        }
    }

    public async Task MoveToFolderAsync(string messageId, string folderId, CancellationToken ct = default)
    {
        await _client.Me.Messages[messageId].Move.PostAsync(new MovePostRequestBody
        {
            DestinationId = folderId
        }, cancellationToken: ct);
    }

    public async Task MarkAsReadAsync(string messageId, CancellationToken ct = default)
    {
        await _client.Me.Messages[messageId].PatchAsync(new Message
        {
            IsRead = true
        }, cancellationToken: ct);
    }

    private static MailItem? TryProject(Message msg)
    {
        if (msg.Id is null)
        {
            return null;
        }

        var fromAddress = msg.From?.EmailAddress;
        var senderAddress = msg.Sender?.EmailAddress;
        var address = fromAddress?.Address ?? senderAddress?.Address ?? "";
        var name = fromAddress?.Name ?? senderAddress?.Name ?? "";

        var bodyContent = msg.Body?.Content ?? "";
        var plain = msg.Body?.ContentType == BodyType.Html
            ? HtmlToText.Convert(bodyContent)
            : bodyContent.Trim();

        return new MailItem(
            Id: msg.Id,
            SenderName: name,
            SenderAddress: address,
            Subject: msg.Subject ?? "",
            BodyPreview: msg.BodyPreview ?? "",
            PlainBody: plain,
            ReceivedDateTime: msg.ReceivedDateTime,
            IsRead: msg.IsRead ?? false,
            ParentFolderId: msg.ParentFolderId ?? "",
            Labels: Array.Empty<string>());
    }

    private static string EscapeOData(string value) =>
        value.Replace("'", "''");
}
