namespace MailChecker.Models;

public sealed record MailItem(
    string Id,
    string SenderName,
    string SenderAddress,
    string Subject,
    string BodyPreview,
    string PlainBody,
    DateTimeOffset? ReceivedDateTime,
    bool IsRead,
    string ParentFolderId,
    IReadOnlyList<string> Labels)
{
    public string DisplaySender =>
        string.IsNullOrWhiteSpace(SenderName)
            ? SenderAddress
            : $"{SenderName} <{SenderAddress}>";

    public bool HasLabel(string label) =>
        Labels.Contains(label, StringComparer.Ordinal);
}
