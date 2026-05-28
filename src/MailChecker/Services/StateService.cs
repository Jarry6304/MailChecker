using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailChecker.Services;

public sealed class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly StateFile _state;

    private StateStore(string path, StateFile state)
    {
        _path = path;
        _state = state;
    }

    public ProviderStateView ForProvider(string providerKey)
    {
        if (!_state.Providers.TryGetValue(providerKey, out var ps))
        {
            ps = new ProviderState();
            _state.Providers[providerKey] = ps;
        }
        return new ProviderStateView(ps);
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        _state.LastRunUtc = DateTimeOffset.UtcNow;

        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, _state, JsonOptions, ct);
    }

    public static StateStore Load(string path)
    {
        if (!File.Exists(path))
        {
            return new StateStore(path, new StateFile());
        }

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        StateFile loaded;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("providers", out _))
        {
            loaded = JsonSerializer.Deserialize<StateFile>(root.GetRawText(), JsonOptions)
                     ?? new StateFile();
        }
        else
        {
            // Legacy single-provider format — migrate as the graph provider's state.
            var legacy = JsonSerializer.Deserialize<ProviderState>(root.GetRawText(), JsonOptions)
                         ?? new ProviderState();
            loaded = new StateFile
            {
                LastRunUtc = legacy.LastRunUtc,
                Providers = new Dictionary<string, ProviderState>
                {
                    ["graph"] = legacy
                }
            };
        }

        return new StateStore(path, loaded);
    }

    private sealed class StateFile
    {
        public DateTimeOffset? LastRunUtc { get; set; }
        public Dictionary<string, ProviderState> Providers { get; set; } = new();
    }

    internal sealed class ProviderState
    {
        public bool FirstRunCompleted { get; set; }
        public DateTimeOffset? LastReceivedDateTime { get; set; }
        public DateTimeOffset? LastRunUtc { get; set; }
        public HashSet<string> ProcessedMessageIds { get; set; } = new();
    }
}

public sealed class ProviderStateView
{
    private readonly StateStore.ProviderState _state;

    internal ProviderStateView(StateStore.ProviderState state)
    {
        _state = state;
    }

    public bool IsFirstRun => !_state.FirstRunCompleted;

    public DateTimeOffset? LastReceivedDateTime => _state.LastReceivedDateTime;

    public bool IsAlreadyProcessed(string messageId) =>
        _state.ProcessedMessageIds.Contains(messageId);

    public void MarkProcessed(string messageId, DateTimeOffset? receivedDateTime)
    {
        _state.ProcessedMessageIds.Add(messageId);
        if (receivedDateTime is { } received &&
            (_state.LastReceivedDateTime is null || received > _state.LastReceivedDateTime))
        {
            _state.LastReceivedDateTime = received;
        }
    }

    public void CompleteFirstRun()
    {
        _state.FirstRunCompleted = true;
        _state.LastRunUtc = DateTimeOffset.UtcNow;
    }
}
