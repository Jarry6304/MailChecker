using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using MailChecker.Configuration;
using GoogleGmailService = Google.Apis.Gmail.v1.GmailService;

namespace MailChecker.Services;

public sealed class GmailAuthProvider
{
    private readonly GmailConfig _config;

    public GmailAuthProvider(GmailConfig config)
    {
        _config = config;
    }

    public async Task<GoogleGmailService> CreateClientAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.ClientId) ||
            string.IsNullOrWhiteSpace(_config.ClientSecret))
        {
            throw new InvalidOperationException(
                "Gmail:ClientId / Gmail:ClientSecret is not configured. " +
                "Create an OAuth client (type: Desktop app) in Google Cloud Console " +
                "and set both values in appsettings.json.");
        }

        var storePath = ResolveDir(_config.TokenCacheDirectory);
        Directory.CreateDirectory(storePath);

        var clientSecrets = new ClientSecrets
        {
            ClientId = _config.ClientId,
            ClientSecret = _config.ClientSecret,
        };

        Console.WriteLine();
        Console.WriteLine("=== Google 帳號登入 ===");
        Console.WriteLine("如果尚未授權，瀏覽器會自動開啟 Google 同意畫面，請依指示登入並授權。");
        Console.WriteLine();

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets,
            _config.Scopes,
            string.IsNullOrWhiteSpace(_config.UserAccount) ? "user" : _config.UserAccount,
            ct,
            new FileDataStore(storePath, fullPath: true));

        return new GoogleGmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "MailChecker"
        });
    }

    private static string ResolveDir(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
