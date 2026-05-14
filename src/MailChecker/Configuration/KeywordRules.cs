namespace MailChecker.Configuration;

public sealed class KeywordRules
{
    public List<string> SubjectAndBodyKeywords { get; set; } = new();
    public List<string> SenderAddressContains { get; set; } = new();
    public List<string> SenderNameContains { get; set; } = new();
    public List<string> SenderDomains { get; set; } = new();
}
