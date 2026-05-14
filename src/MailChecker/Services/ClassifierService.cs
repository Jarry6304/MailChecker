using System.Text.Json;
using MailChecker.Configuration;
using MailChecker.Models;

namespace MailChecker.Services;

public sealed class ClassifierService
{
    private readonly KeywordRules _rules;

    private ClassifierService(KeywordRules rules)
    {
        _rules = rules;
    }

    public static ClassifierService Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Keywords file not found: {path}. Copy keywords.json into your output directory.", path);
        }

        using var stream = File.OpenRead(path);
        var rules = JsonSerializer.Deserialize<KeywordRules>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? new KeywordRules();

        return new ClassifierService(rules);
    }

    public ClassificationResult Classify(MailItem mail)
    {
        var address = mail.SenderAddress ?? "";
        var addressLower = address.ToLowerInvariant();
        var nameLower = (mail.SenderName ?? "").ToLowerInvariant();

        foreach (var domain in _rules.SenderDomains)
        {
            if (!string.IsNullOrWhiteSpace(domain) &&
                addressLower.EndsWith(domain.ToLowerInvariant(), StringComparison.Ordinal))
            {
                return new ClassificationResult(
                    true,
                    $"sender domain matches '{domain}'",
                    Array.Empty<string>(),
                    domain);
            }
        }

        foreach (var fragment in _rules.SenderAddressContains)
        {
            if (!string.IsNullOrWhiteSpace(fragment) &&
                addressLower.Contains(fragment.ToLowerInvariant(), StringComparison.Ordinal))
            {
                return new ClassificationResult(
                    true,
                    $"sender address contains '{fragment}'",
                    Array.Empty<string>(),
                    fragment);
            }
        }

        foreach (var fragment in _rules.SenderNameContains)
        {
            if (!string.IsNullOrWhiteSpace(fragment) &&
                nameLower.Contains(fragment.ToLowerInvariant(), StringComparison.Ordinal))
            {
                return new ClassificationResult(
                    true,
                    $"sender name contains '{fragment}'",
                    Array.Empty<string>(),
                    fragment);
            }
        }

        var haystack = $"{mail.Subject}\n{mail.BodyPreview}\n{mail.PlainBody}".ToLowerInvariant();
        var matched = new List<string>();
        foreach (var keyword in _rules.SubjectAndBodyKeywords)
        {
            if (!string.IsNullOrWhiteSpace(keyword) &&
                haystack.Contains(keyword.ToLowerInvariant(), StringComparison.Ordinal))
            {
                matched.Add(keyword);
            }
        }

        if (matched.Count > 0)
        {
            return new ClassificationResult(
                true,
                $"keyword match: {string.Join(", ", matched)}",
                matched,
                null);
        }

        return ClassificationResult.NotBill();
    }
}
