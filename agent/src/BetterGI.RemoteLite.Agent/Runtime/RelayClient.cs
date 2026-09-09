using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BetterGI.RemoteLite.Agent.Storage;
using BetterGI.RemoteLite.Protocol;
using BetterGI.RemoteLite.Security;

namespace BetterGI.RemoteLite.Agent.Runtime;

internal sealed class RelayClient(AgentSettings settings, PairingKeyMaterial material) : IAsyncDisposable
{
    private readonly AgentSettings _settings = settings;
    private readonly PairingKeyMaterial _material = material;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private ClientWebSocket? _socket;
    private Task? _runTask;

    public event Func<byte[], Task>? BinaryReceived;
    public event EventHandler<bool>? PeerOnlineChanged;

    public bool PeerOnline { get; private set; }

    public void Start() => _runTask = Task.Run(RunAsync);

    public async Task SendBinaryAsync(byte[] payload, CancellationToken cancellationToken = default)
    {
        var socket = _socket;
        if (socket?.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("中转连接当前不可用。");
        }
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(payload, WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task RunAsync()
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                _socket = socket;
                await socket.ConnectAsync(BuildWebSocketUri(_settings.RelayBaseUrl), _lifetime.Token).ConfigureAwait(false);
                await SendRegistrationAsync(socket, _lifetime.Token).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(1);
                await ReceiveLoopAsync(socket, _lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Reconnect below. The tray UI reports the current connection state.
            }
            finally
            {
                _socket = null;
                SetPeerOnline(false);
            }

            try
            {
                await Task.Delay(delay, _lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
        }
    }

    private async Task SendRegistrationAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var registration = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "register",
            protocolVersion = ProtocolConstants.Version,
            channelId = _material.ChannelId,
            role = "pc",
            relayToken = _material.RelayToken,
        }, RemoteJson.Options);
        await socket.SendAsync(registration, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
                if (message.Length + result.Count > 64 * 1024)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "frame too large", cancellationToken).ConfigureAwait(false);
                    return;
                }
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var payload = message.ToArray();
            if (result.MessageType == WebSocketMessageType.Text)
            {
                HandleControl(payload);
            }
            else if (result.MessageType == WebSocketMessageType.Binary && BinaryReceived is not null)
            {
                await BinaryReceived(payload).ConfigureAwait(false);
            }
        }
    }

    private void HandleControl(byte[] payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == "peer_status" && root.TryGetProperty("online", out var online))
            {
                SetPeerOnline(online.GetBoolean());
            }
        }
        catch (JsonException)
        {
            // Ignore malformed relay controls. Encrypted messages use the binary path.
        }
    }

    private void SetPeerOnline(bool value)
    {
        if (PeerOnline == value)
        {
            return;
        }
        PeerOnline = value;
        PeerOnlineChanged?.Invoke(this, value);
    }

    private static Uri BuildWebSocketUri(string baseUrl)
    {
        var baseUri = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var builder = new UriBuilder(new Uri(baseUri, "ws"))
        {
            Scheme = baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Port = baseUri.IsDefaultPort ? -1 : baseUri.Port,
        };
        return builder.Uri;
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        var socket = _socket;
        if (socket?.State == WebSocketState.Open)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "agent stopping", CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The relay may already be gone.
            }
        }
        if (_runTask is not null)
        {
            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        _lifetime.Dispose();
        _sendLock.Dispose();
    }
}

