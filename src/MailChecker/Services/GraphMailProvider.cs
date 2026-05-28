using MailChecker.Configuration;
using MailChecker.Models;

namespace MailChecker.Services;

public sealed class GraphMailProvider : IMailProvider
{
    private readonly GraphConfig _graphConfig;
    private readonly OutlookConfig _outlookConfig;
    private GraphService? _service;
    private string _billFolderId = "";

    public GraphMailProvider(GraphConfig graphConfig, OutlookConfig outlookConfig)
    {
        _graphConfig = graphConfig;
        _outlookConfig = outlookConfig;
    }

    public string ProviderKey => "graph";

    public string DisplayName => "Microsoft 帳號";

    public async Task<string> InitializeAsync(CancellationToken ct)
    {
        var client = new GraphAuthProvider(_graphConfig).CreateClient();
        _service = new GraphService(client, _outlookConfig);

        var who = await _service.GetSignedInUserAsync(ct);
        _billFolderId = await _service.EnsureBillFolderAsync(ct);
        return who;
    }

    public IAsyncEnumerable<MailItem> EnumerateInboxAsync(
        DateTimeOffset? receivedAfter,
        CancellationToken ct)
    {
        EnsureInitialized();
        return _service!.EnumerateInboxAsync(receivedAfter, ct);
    }

    public async Task ProcessBillAsync(MailItem mail, CancellationToken ct)
    {
        EnsureInitialized();

        if (!mail.IsRead)
        {
            await _service!.MarkAsReadAsync(mail.Id, ct);
        }

        if (!string.Equals(mail.ParentFolderId, _billFolderId, StringComparison.Ordinal))
        {
            await _service!.MoveToFolderAsync(mail.Id, _billFolderId, ct);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void EnsureInitialized()
    {
        if (_service is null)
        {
            throw new InvalidOperationException(
                $"{nameof(GraphMailProvider)} not initialized. Call InitializeAsync first.");
        }
    }
}
