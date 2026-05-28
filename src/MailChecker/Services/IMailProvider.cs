using MailChecker.Models;

namespace MailChecker.Services;

public interface IMailProvider : IAsyncDisposable
{
    string ProviderKey { get; }

    string DisplayName { get; }

    Task<string> InitializeAsync(CancellationToken ct);

    IAsyncEnumerable<MailItem> EnumerateInboxAsync(
        DateTimeOffset? receivedAfter,
        CancellationToken ct);

    Task ProcessBillAsync(MailItem mail, CancellationToken ct);
}
