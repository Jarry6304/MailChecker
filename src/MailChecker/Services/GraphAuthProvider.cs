using Azure.Identity;
using MailChecker.Configuration;
using Microsoft.Graph;

namespace MailChecker.Services;

public sealed class GraphAuthProvider
{
    private readonly GraphConfig _config;

    public GraphAuthProvider(GraphConfig config)
    {
        _config = config;
    }

    public GraphServiceClient CreateClient()
    {
        if (string.IsNullOrWhiteSpace(_config.ClientId))
        {
            throw new InvalidOperationException(
                "Graph:ClientId is not configured. Register an Azure AD application " +
                "and set its Client ID in appsettings.json.");
        }

        var options = new DeviceCodeCredentialOptions
        {
            ClientId = _config.ClientId,
            TenantId = string.IsNullOrWhiteSpace(_config.TenantId) ? "common" : _config.TenantId,
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions
            {
                Name = "MailChecker",
                UnsafeAllowUnencryptedStorage = true
            },
            DeviceCodeCallback = (code, _) =>
            {
                Console.WriteLine();
                Console.WriteLine("=== Microsoft 帳號登入 ===");
                Console.WriteLine(code.Message);
                Console.WriteLine();
                return Task.CompletedTask;
            }
        };

        var credential = new DeviceCodeCredential(options);
        return new GraphServiceClient(credential, _config.Scopes);
    }
}
