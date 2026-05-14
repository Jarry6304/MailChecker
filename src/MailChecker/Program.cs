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
    var state = StateService.Load(ResolvePath(config.State.StateFile));
    var classifier = ClassifierService.Load(ResolvePath(config.Classifier.KeywordsFile));

    Console.WriteLine($"=== MailChecker — {(state.IsFirstRun ? "首次掃描" : "增量掃描")} ===");

    var graphClient = new GraphAuthProvider(config.Graph).CreateClient();
    var graph = new GraphService(graphClient, config.Outlook);

    var who = await graph.GetSignedInUserAsync(ct);
    Console.WriteLine($"已登入：{who}");

    var folderId = await graph.EnsureBillFolderAsync(ct);
    Console.WriteLine($"帳單資料夾就緒：{config.Outlook.BillFolderName} ({folderId})");

    using var line = new LineMessagingService(config.Line);
    if (!line.IsConfigured)
    {
        Console.WriteLine("注意：LINE 尚未設定，這次只會分類/搬信，不會推播。");
    }

    var receivedAfter = state.IsFirstRun ? (DateTimeOffset?)null : state.LastReceivedDateTime;
    if (receivedAfter is { } cutoff)
    {
        Console.WriteLine($"只處理 {cutoff.ToLocalTime():yyyy-MM-dd HH:mm} 之後的信件。");
    }
    else
    {
        Console.WriteLine("掃描收件匣全部信件（這可能需要一點時間）。");
    }

    var collected = new List<MailItem>();
    await foreach (var mail in graph.EnumerateInboxAsync(receivedAfter, ct))
    {
        if (!state.IsAlreadyProcessed(mail.Id))
        {
            collected.Add(mail);
        }
    }

    Console.WriteLine($"待處理信件數：{collected.Count}");

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

        Console.WriteLine();
        Console.WriteLine($"📬 帳單命中：{mail.Subject}");
        Console.WriteLine($"   寄件人：{mail.DisplaySender}");
        Console.WriteLine($"   理由：{result.Reason}");

        try
        {
            if (!mail.IsRead)
            {
                await graph.MarkAsReadAsync(mail.Id, ct);
            }

            if (!string.Equals(mail.ParentFolderId, folderId, StringComparison.Ordinal))
            {
                await graph.MoveToFolderAsync(mail.Id, folderId, ct);
            }

            await line.NotifyBillAsync(mail, ct);

            state.MarkProcessed(mail.Id, mail.ReceivedDateTime);
            billCount++;
            Console.WriteLine("   ✔ 已搬移、標為已讀、LINE 已通知");
        }
        catch (Exception ex)
        {
            errorCount++;
            Console.Error.WriteLine($"   ✘ 處理失敗：{ex.Message}");
        }
    }

    if (state.IsFirstRun)
    {
        state.CompleteFirstRun();
    }

    await state.SaveAsync(ct);

    Console.WriteLine();
    Console.WriteLine($"完成。帳單：{billCount}，非帳單：{skippedCount}，失敗：{errorCount}");
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

public partial class Program;
