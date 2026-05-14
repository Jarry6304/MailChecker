using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailChecker.Services;

public sealed class StateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private State _state;

    private StateService(string path, State state)
    {
        _path = path;
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

    public static StateService Load(string path)
    {
        if (!File.Exists(path))
        {
            return new StateService(path, new State());
        }

        using var stream = File.OpenRead(path);
        var state = JsonSerializer.Deserialize<State>(stream, JsonOptions) ?? new State();
        return new StateService(path, state);
    }

    private sealed class State
    {
        public bool FirstRunCompleted { get; set; }
        public DateTimeOffset? LastReceivedDateTime { get; set; }
        public DateTimeOffset? LastRunUtc { get; set; }
        public HashSet<string> ProcessedMessageIds { get; set; } = new();
    }
}
