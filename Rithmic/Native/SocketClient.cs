namespace StockSharp.Rithmic.Native;

using System.Net.WebSockets;

using Ecng.ComponentModel;

internal class SocketClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;

	public SocketClient(string uri, int reconnectAttempts, WorkingTime workingTime)
	{
		_client = new WebSocketClient(
			uri,
			(state, token) =>
			{
				if (StateChanged is { } handler)
					return handler(state, token);
				return default;
			},
			(error, token) =>
			{
				this.AddErrorLog(error);

				if (Error is { } handler)
					return handler(error, token);
				return default;
			},
			OnProcessAsync,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime,
		};

		_client.PostConnect += OnPostConnectAsync;
	}

	public event Action<int, byte[]> MessageReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;
	public event Func<bool, CancellationToken, ValueTask> PostConnect;

	public bool IsConnected => _client.IsConnected;

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
		=> _client.ConnectAsync(cancellationToken);

	public void Disconnect()
		=> _client.Disconnect();

	public ValueTask SendAsync(Google.Protobuf.IMessage message, CancellationToken cancellationToken)
		=> _client.SendAsync(message.ToByteArray(), WebSocketMessageType.Binary, cancellationToken);

	private ValueTask OnProcessAsync(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var data = message.Memory.ToArray();
		var templateId = MessageType.Parser.ParseFrom(data).TemplateId;
		MessageReceived?.Invoke(templateId, data);
		return default;
	}

	private ValueTask OnPostConnectAsync(bool isReconnect, CancellationToken cancellationToken)
		=> PostConnect is { } handler ? handler(isReconnect, cancellationToken) : default;

	protected override void DisposeManaged()
	{
		_client.Dispose();
		base.DisposeManaged();
	}
}
