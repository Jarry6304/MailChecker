using MailChecker.Configuration;
using MailChecker.Models;
using MailChecker.Services;
using Microsoft.Extensions.Configuration;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await RunAsync(cts.Token);
    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("Cancelled.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal: {ex.Message}");
    Console.Error.WriteLine(ex);
    return 1;
}

static async Task RunAsync(CancellationToken ct)
{
    var config = LoadConfig();
    var stateStore = StateStore.Load(ResolvePath(config.State.StateFile));
    var classifier = ClassifierService.Load(ResolvePath(config.Classifier.KeywordsFile));

    using var line = new LineMessagingService(config.Line);
    if (!line.IsConfigured)
    {
        Console.WriteLine("注意：LINE 尚未設定，這次只會分類/搬信，不會推播。");
    }

    var providers = BuildProviders(config);
    if (providers.Count == 0)
    {
        Console.Error.WriteLine(
            "未啟用任何信箱：請至少在 appsettings.json 設定 Graph (Microsoft) 或 Gmail (Google) 其中一個。");
        return;
    }

    Console.WriteLine($"=== MailChecker — 啟用信箱：{string.Join(", ", providers.Select(p => p.DisplayName))} ===");

    // Auth phase runs sequentially so device-code / browser prompts don't fight for the console.
    var ready = new List<IMailProvider>();
    foreach (var provider in providers)
    {
        try
        {
            var who = await provider.InitializeAsync(ct);
            Console.WriteLine($"[{provider.DisplayName}] 已登入：{who}");
            ready.Add(provider);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{provider.DisplayName}] 初始化失敗：{ex.Message}");
        }
    }

    if (ready.Count == 0)
    {
        Console.Error.WriteLine("所有信箱都初始化失敗，結束。");
        return;
    }

    var tasks = ready
        .Select(p => ProcessProviderAsync(p, classifier, line, stateStore.ForProvider(p.ProviderKey), ct))
        .ToList();
    var summaries = await Task.WhenAll(tasks);

    await stateStore.SaveAsync(ct);

    foreach (var provider in ready)
    {
        await provider.DisposeAsync();
    }

    Console.WriteLine();
    foreach (var s in summaries)
    {
        Console.WriteLine($"[{s.Provider}] 完成。帳單：{s.BillCount}，非帳單：{s.SkippedCount}，失敗：{s.ErrorCount}");
    }
}

static List<IMailProvider> BuildProviders(AppConfig config)
{
    var list = new List<IMailProvider>();
    if (config.Graph.IsEnabled)
    {
        list.Add(new GraphMailProvider(config.Graph, config.Outlook));
    }
    if (config.Gmail.IsEnabled)
    {
        list.Add(new GmailService(config.Gmail));
    }
    return list;
}

static async Task<ProviderSummary> ProcessProviderAsync(
    IMailProvider provider,
    ClassifierService classifier,
    LineMessagingService line,
    ProviderStateView state,
    CancellationToken ct)
{
    var label = provider.DisplayName;
    void Log(string msg) => Console.WriteLine($"[{label}] {msg}");

    Log(state.IsFirstRun ? "首次掃描。" : "增量掃描。");

    var receivedAfter = state.IsFirstRun ? (DateTimeOffset?)null : state.LastReceivedDateTime;
    if (receivedAfter is { } cutoff)
    {
        Log($"只處理 {cutoff.ToLocalTime():yyyy-MM-dd HH:mm} 之後的信件。");
    }
    else
    {
        Log("掃描收件匣全部信件（這可能需要一點時間）。");
    }

    var collected = new List<MailItem>();
    try
    {
        await foreach (var mail in provider.EnumerateInboxAsync(receivedAfter, ct))
        {
            if (!state.IsAlreadyProcessed(mail.Id))
            {
                collected.Add(mail);
            }
        }
    }
    catch (Exception ex)
    {
        Log($"列舉信件失敗：{ex.Message}");
        return new ProviderSummary(label, 0, 0, 1);
    }

    Log($"待處理信件數：{collected.Count}");

    var billCount = 0;
    var skippedCount = 0;
    var errorCount = 0;

    foreach (var mail in collected)
    {
        ct.ThrowIfCancellationRequested();

        var result = classifier.Classify(mail);
        if (!result.IsBill)
        {
            state.MarkProcessed(mail.Id, mail.ReceivedDateTime);
            skippedCount++;
            continue;
        }

        ConsoleSink.WriteBlock(() =>
        {
            Console.WriteLine();
            Console.WriteLine($"[{label}] 📬 帳單命中：{mail.Subject}");
            Console.WriteLine($"[{label}]    寄件人：{mail.DisplaySender}");
            Console.WriteLine($"[{label}]    理由：{result.Reason}");
        });

        try
        {
            await provider.ProcessBillAsync(mail, ct);
            await line.NotifyBillAsync(mail, ct);
            state.MarkProcessed(mail.Id, mail.ReceivedDateTime);
            billCount++;
            Log("   ✔ 已搬移、標為已讀、LINE 已通知");
        }
        catch (Exception ex)
        {
            errorCount++;
            Console.Error.WriteLine($"[{label}]    ✘ 處理失敗：{ex.Message}");
        }
    }

    if (state.IsFirstRun)
    {
        state.CompleteFirstRun();
    }

    return new ProviderSummary(label, billCount, skippedCount, errorCount);
}

static AppConfig LoadConfig()
{
    var builder = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
        .AddUserSecrets(typeof(Program).Assembly, optional: true)
        .AddEnvironmentVariables(prefix: "MAILCHECKER_");

    var raw = builder.Build();
    var config = new AppConfig();
    raw.Bind(config);
    return config;
}

static string ResolvePath(string path)
{
    return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}

internal sealed record ProviderSummary(string Provider, int BillCount, int SkippedCount, int ErrorCount);

internal static class ConsoleSink
{
    private static readonly object Lock = new();

    public static void WriteBlock(Action writer)
    {
        lock (Lock)
        {
            writer();
        }
    }
}

public partial class Program;
