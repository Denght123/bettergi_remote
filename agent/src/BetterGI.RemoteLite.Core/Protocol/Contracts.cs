using System.Text.Json;

namespace BetterGI.RemoteLite.Protocol;

public static class ProtocolConstants
{
    public const int Version = 1;
    public const int MaximumPlaintextBytes = 48 * 1024;
    public static readonly TimeSpan MaximumMessageLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);
}

public static class RpcMethods
{
    public const string PairRequest = "pair.request";
    public const string StatusGet = "status.get";
    public const string ConfigGet = "config.get";
    public const string ConfigSync = "config.sync";
    public const string ConfigUpdate = "config.update";
    public const string TaskStart = "task.start";
    public const string TaskStop = "task.stop";
    public const string ReportLatest = "report.latest";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        PairRequest,
        StatusGet,
        ConfigGet,
        ConfigSync,
        ConfigUpdate,
        TaskStart,
        TaskStop,
        ReportLatest,
    };
}

public static class PushEvents
{
    public const string StatusChanged = "status.changed";
    public const string RunProgress = "run.progress";
    public const string RunCompleted = "run.completed";
}

public sealed record EncryptedEnvelope(
    int ProtocolVersion,
    string Direction,
    string RequestId,
    long IssuedAtUnixSeconds,
    long ExpiresAtUnixSeconds,
    string Nonce,
    string Ciphertext);

public sealed record RpcRequestMessage(
    string Type,
    string RequestId,
    string SenderDeviceId,
    string Method,
    JsonElement Params);

public sealed record RpcResponseMessage(
    string Type,
    string RequestId,
    bool Ok,
    object? Result = null,
    RpcError? Error = null);

public sealed record PushMessage(string Type, string Event, object Data);

public sealed record RpcError(string Code, string Message, bool Retryable = false, object? Data = null);

public sealed record PairRequest(string DeviceId, string DeviceLabel);

public sealed record PairResponse(string PcDeviceId, string PcName, bool Bound, DateTimeOffset BindingExpiresAt);

public sealed record ConfigUpdateRequest(
    string BaseRevision,
    IReadOnlyList<TaskItemDto> Tasks,
    IReadOnlyDictionary<string, JsonElement> Values);

public sealed record TaskStartRequest(string ExpectedRevision, bool Confirmed);

public sealed record TaskStopRequest(string? RunId);

public sealed record TaskAccepted(string RunId, DateTimeOffset AcceptedAt);

public sealed record TaskStopResult(string? RunId, string State, bool ShortcutSent);

public sealed record AgentStatusDto(
    string PcName,
    string PcDeviceId,
    bool PhonePeerOnline,
    bool WindowsUnlocked,
    bool BetterGiConfigured,
    bool BetterGiVersionSupported,
    string? BetterGiVersion,
    bool BetterGiRunning,
    bool GameRunning,
    string State,
    string? ActiveRunId,
    string? ActiveTask,
    DateTimeOffset ObservedAt,
    string? Message = null,
    DateTimeOffset? BindingExpiresAt = null,
    int? BindingDaysRemaining = null,
    string AgentVersion = "0.3.2");

public sealed record TaskItemDto(string Id, string Name, bool Enabled, bool IsCustom, int Order);

public enum EditableFieldType
{
    Text,
    Number,
    Toggle,
    Select,
    MultiSelect,
}

public sealed record EditableFieldDto(
    string Path,
    string Scope,
    string Group,
    string Label,
    EditableFieldType Type,
    JsonElement Value,
    IReadOnlyList<string>? Options = null,
    double? Minimum = null,
    double? Maximum = null,
    string? Description = null);

public sealed record RemoteConfigDto(
    string Name,
    string Revision,
    IReadOnlyList<TaskItemDto> Tasks,
    IReadOnlyList<EditableFieldDto> Fields,
    string CompletionAction,
    DateTimeOffset ReadAt);

public sealed record RunProgressDto(
    string RunId,
    string State,
    string? CurrentTask,
    int CompletedTasks,
    int TotalTasks,
    string? Message,
    DateTimeOffset ObservedAt);

public sealed record RunTaskResult(string Name, string State, string? Message = null);

public sealed record RunReportDto(
    string RunId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    double DurationSeconds,
    IReadOnlyList<RunTaskResult> Tasks,
    IReadOnlyDictionary<string, int> Rewards,
    string? DailyRewardStatus,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> LogExcerpt,
    string ParserVersion = "0.64.x-v1");
