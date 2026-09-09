using System.Security.Cryptography;
using System.Text.Json;
using BetterGI.RemoteLite.Agent.Storage;
using BetterGI.RemoteLite.BetterGi;
using BetterGI.RemoteLite.Protocol;
using BetterGI.RemoteLite.Security;

namespace BetterGI.RemoteLite.Agent.Runtime;

internal sealed class AgentRuntime : IAsyncDisposable
{
    private readonly AgentSettingsStore _settingsStore;
    private readonly Func<PairRequest, Task<bool>> _confirmPairing;
    private readonly BetterGiController _controller;
    private readonly RequestReplayGuard _replayGuard = new();
    private readonly CancellationTokenSource _lifetime = new();
    private PairingKeyMaterial? _material;
    private EncryptedEnvelopeCodec? _incoming;
    private EncryptedEnvelopeCodec? _outgoing;
    private RelayClient? _relay;
    private Task? _statusTask;
    private DateTimeOffset _pairingAllowedUntil;
    private string? _lastStatusJson;

    public AgentRuntime(AgentSettingsStore settingsStore, Func<PairRequest, Task<bool>> confirmPairing)
    {
        _settingsStore = settingsStore;
        _confirmPairing = confirmPairing;
        _controller = new BetterGiController(settingsStore);
        _controller.ProgressChanged += (_, progress) => _ = SendPushSafeAsync(PushEvents.RunProgress, progress);
        _controller.RunCompleted += (_, report) => _ = SendPushSafeAsync(PushEvents.RunCompleted, report);
    }

    public bool PeerOnline => _relay?.PeerOnline ?? false;

    public void Start()
    {
        var settings = _settingsStore.Current;
        var secret = SecretProtector.UnprotectBytes(settings.ProtectedPairingSecret);
        if (!settings.IsConfigured || secret is null || secret.Length != 32)
        {
            return;
        }
        try
        {
            _material = PairingKeyMaterial.Derive(secret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
        _incoming = new EncryptedEnvelopeCodec(_material.PhoneToPcKey, "pc-to-phone", "phone-to-pc");
        _outgoing = new EncryptedEnvelopeCodec(_material.PcToPhoneKey, "pc-to-phone", "phone-to-pc");
        _relay = new RelayClient(settings, _material);
        _relay.BinaryReceived += HandleBinaryAsync;
        _relay.PeerOnlineChanged += (_, _) => _ = SendStatusIfChangedAsync();
        _relay.Start();
        _statusTask = Task.Run(StatusLoopAsync);
    }

    public void AllowPairing(TimeSpan duration) => _pairingAllowedUntil = DateTimeOffset.UtcNow.Add(duration);

    public AgentStatusDto GetStatus() => _controller.GetStatus(PeerOnline);

    private async Task HandleBinaryAsync(byte[] payload)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(payload, RemoteJson.Options)
                ?? throw new JsonException("Encrypted envelope is empty.");
            if (!_replayGuard.TryAccept(envelope.RequestId))
            {
                return;
            }
            var plaintext = _incoming!.Decrypt<JsonElement>(envelope);
            var request = plaintext.Deserialize<RpcRequestMessage>(RemoteJson.Options)
                ?? throw new JsonException("RPC request is empty.");
            if (request.Type != "rpc.request" || request.RequestId != envelope.RequestId || !RpcMethods.Allowed.Contains(request.Method))
            {
                throw new InvalidDataException("RPC request is invalid.");
            }
            var response = await DispatchAsync(request, _lifetime.Token).ConfigureAwait(false);
            await SendResponseAsync(response, _lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or CryptographicException or InvalidDataException or ArgumentException)
        {
            // Authentication and contract failures do not receive an oracle response.
        }
    }

    private async Task<RpcResponseMessage> DispatchAsync(RpcRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Method == RpcMethods.PairRequest)
            {
                return Success(request.RequestId, await HandlePairRequestAsync(request).ConfigureAwait(false));
            }

            EnsureBoundPhone(request.SenderDeviceId);
            return request.Method switch
            {
                RpcMethods.StatusGet => Success(request.RequestId, GetStatus()),
                RpcMethods.ConfigGet => Success(request.RequestId, await _controller.CreateConfigStore().LoadAsync(cancellationToken).ConfigureAwait(false)),
                RpcMethods.ConfigUpdate => Success(request.RequestId, await UpdateConfigAsync(request, cancellationToken).ConfigureAwait(false)),
                RpcMethods.TaskStart => Success(request.RequestId, await StartTaskAsync(request, cancellationToken).ConfigureAwait(false)),
                RpcMethods.TaskStop => Success(request.RequestId, await StopTaskAsync(request, cancellationToken).ConfigureAwait(false)),
                RpcMethods.ReportLatest => Success(request.RequestId, await _controller.Reports.LatestAsync(cancellationToken).ConfigureAwait(false)),
                _ => Failure(request.RequestId, "method_not_allowed", "操作不受支持。"),
            };
        }
        catch (ConfigConflictException exception)
        {
            return Failure(request.RequestId, "config_conflict", exception.Message, true, new { latestRevision = exception.LatestRevision });
        }
        catch (FileNotFoundException exception)
        {
            return Failure(request.RequestId, "not_configured", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(request.RequestId, "operation_rejected", exception.Message, true);
        }
        catch (InvalidDataException exception)
        {
            return Failure(request.RequestId, "invalid_request", exception.Message);
        }
    }

    private async Task<PairResponse> HandlePairRequestAsync(RpcRequestMessage request)
    {
        var pair = request.Params.Deserialize<PairRequest>(RemoteJson.Options)
            ?? throw new InvalidDataException("绑定请求无效。");
        if (!string.Equals(pair.DeviceId, request.SenderDeviceId, StringComparison.Ordinal) || pair.DeviceId.Length is < 16 or > 128)
        {
            throw new InvalidDataException("手机设备 ID 无效。");
        }
        var settings = _settingsStore.Current;
        if (!string.IsNullOrEmpty(settings.BoundPhoneDeviceId))
        {
            if (!string.Equals(settings.BoundPhoneDeviceId, pair.DeviceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("此电脑已经绑定另一台手机。请在电脑托盘中执行重新绑定。");
            }
            return new PairResponse(settings.PcDeviceId, Environment.MachineName, true);
        }
        if (DateTimeOffset.UtcNow > _pairingAllowedUntil)
        {
            throw new InvalidOperationException("电脑端绑定窗口已过期，请重新打开二维码。");
        }
        if (!await _confirmPairing(pair).ConfigureAwait(false))
        {
            throw new InvalidOperationException("电脑端拒绝了绑定请求。");
        }
        _settingsStore.Update(value => value.BoundPhoneDeviceId = pair.DeviceId);
        return new PairResponse(settings.PcDeviceId, Environment.MachineName, true);
    }

    private async Task<RemoteConfigDto> UpdateConfigAsync(RpcRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_controller.CanMutate(out var reason))
        {
            throw new InvalidOperationException(reason);
        }
        var update = request.Params.Deserialize<ConfigUpdateRequest>(RemoteJson.Options)
            ?? throw new InvalidDataException("配置提交无效。");
        return await _controller.CreateConfigStore().UpdateAsync(update, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TaskAccepted> StartTaskAsync(RpcRequestMessage request, CancellationToken cancellationToken)
    {
        var start = request.Params.Deserialize<TaskStartRequest>(RemoteJson.Options)
            ?? throw new InvalidDataException("启动请求无效。");
        if (!start.Confirmed)
        {
            throw new InvalidOperationException("手机端必须明确确认本次启动。");
        }
        return await _controller.StartAsync(start.ExpectedRevision, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TaskStopResult> StopTaskAsync(RpcRequestMessage request, CancellationToken cancellationToken)
    {
        var stop = request.Params.Deserialize<TaskStopRequest>(RemoteJson.Options)
            ?? new TaskStopRequest(null);
        return await _controller.StopAsync(stop.RunId, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureBoundPhone(string deviceId)
    {
        var bound = _settingsStore.Current.BoundPhoneDeviceId;
        if (string.IsNullOrEmpty(bound) || !string.Equals(bound, deviceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("手机尚未绑定此电脑。");
        }
    }

    private async Task SendResponseAsync(RpcResponseMessage response, CancellationToken cancellationToken)
    {
        var envelope = _outgoing!.Encrypt(response, response.RequestId);
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, RemoteJson.Options);
        await _relay!.SendBinaryAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendPushSafeAsync(string eventName, object data)
    {
        try
        {
            if (!PeerOnline)
            {
                return;
            }
            var requestId = Guid.NewGuid().ToString("N");
            var message = new PushMessage("push", eventName, data);
            var envelope = _outgoing!.Encrypt(message, requestId);
            await _relay!.SendBinaryAsync(JsonSerializer.SerializeToUtf8Bytes(envelope, RemoteJson.Options), _lifetime.Token).ConfigureAwait(false);
        }
        catch
        {
            // Pushes are best effort; the phone can resync through RPC.
        }
    }

    private async Task StatusLoopAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            await SendStatusIfChangedAsync().ConfigureAwait(false);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), _lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task SendStatusIfChangedAsync()
    {
        var status = GetStatus();
        var json = JsonSerializer.Serialize(status, RemoteJson.Options);
        if (string.Equals(json, _lastStatusJson, StringComparison.Ordinal))
        {
            return;
        }
        _lastStatusJson = json;
        await SendPushSafeAsync(PushEvents.StatusChanged, status).ConfigureAwait(false);
    }

    private static RpcResponseMessage Success(string requestId, object? result)
        => new("rpc.response", requestId, true, result);

    private static RpcResponseMessage Failure(string requestId, string code, string message, bool retryable = false, object? data = null)
        => new("rpc.response", requestId, false, null, new RpcError(code, message, retryable, data));

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_relay is not null)
        {
            await _relay.DisposeAsync().ConfigureAwait(false);
        }
        if (_statusTask is not null)
        {
            try
            {
                await _statusTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        _material?.Dispose();
        _lifetime.Dispose();
    }
}

