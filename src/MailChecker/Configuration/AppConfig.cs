namespace MailChecker.Configuration;

public sealed class AppConfig
{
    public GraphConfig Graph { get; set; } = new();
    public OutlookConfig Outlook { get; set; } = new();
    public GmailConfig Gmail { get; set; } = new();
    public LineConfig Line { get; set; } = new();
    public ClassifierConfig Classifier { get; set; } = new();
    public StateConfig State { get; set; } = new();
}

public sealed class GraphConfig
{
    public string ClientId { get; set; } = "";
    public string TenantId { get; set; } = "common";
    public string[] Scopes { get; set; } = new[] { "Mail.ReadWrite", "User.Read" };
    public string TokenCacheFile { get; set; } = "data/graph-token-cache.bin";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(ClientId);
}

public sealed class OutlookConfig
{
    public string BillFolderName { get; set; } = "帳單";
    public int PageSize { get; set; } = 50;
}

public sealed class GmailConfig
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string[] Scopes { get; set; } = new[] { "https://www.googleapis.com/auth/gmail.modify" };
    public string TokenCacheDirectory { get; set; } = "data/gmail-token-cache";
    public string UserAccount { get; set; } = "user";
    public string BillLabelName { get; set; } = "帳單";
    public int PageSize { get; set; } = 50;

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

public sealed class LineConfig
{
    public string ChannelAccessToken { get; set; } = "";
    public string UserId { get; set; } = "";
    public int BodyPreviewMaxChars { get; set; } = 400;
}

public sealed class ClassifierConfig
{
    public string KeywordsFile { get; set; } = "keywords.json";
}

public sealed class StateConfig
{
    public string StateFile { get; set; } = "data/state.json";
}
