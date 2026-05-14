namespace MailChecker.Models;

public sealed record ClassificationResult(
    bool IsBill,
    string Reason,
    IReadOnlyList<string> MatchedKeywords,
    string? MatchedSenderRule)
{
    public static ClassificationResult NotBill() =>
        new(false, "no match", Array.Empty<string>(), null);
}
