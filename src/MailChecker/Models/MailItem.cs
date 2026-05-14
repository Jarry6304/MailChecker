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
    string ParentFolderId)
{
    public string DisplaySender =>
        string.IsNullOrWhiteSpace(SenderName)
            ? SenderAddress
            : $"{SenderName} <{SenderAddress}>";
}
