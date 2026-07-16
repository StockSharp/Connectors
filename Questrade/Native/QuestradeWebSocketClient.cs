namespace StockSharp.Questrade.Native;

sealed class QuestradeWebSocketClient : BaseLogReceiver
{
	private const int _maxMessageSize = 4 * 1024 * 1024;
	private readonly Uri _uri;
	private readonly Func<string> _accessToken;
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.DateTimeOffset,
	};

	public QuestradeWebSocketClient(string apiServer, int port, Func<string> accessToken)
	{
		var apiUri = new Uri(apiServer, UriKind.Absolute);
		_uri = new UriBuilder(apiUri)
		{
			Scheme = "wss",
			Port = port,
			Path = "/",
			Query = string.Empty,
		}.Uri;
		_accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
	}

	public async Task Run<T>(Func<T, CancellationToken, ValueTask> handler, CancellationToken cancellationToken)
	{
		using var socket = new ClientWebSocket();
		socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		await socket.ConnectAsync(_uri, cancellationToken);
		await SendText(socket, _accessToken().ThrowIfEmpty("Questrade access token"), cancellationToken);
		var authentication = JsonConvert.DeserializeObject<QuestradeSocketAuthentication>(
			await ReceiveText(socket, cancellationToken), _jsonSettings);
		if (authentication?.Success != true)
			throw new InvalidOperationException("Questrade WebSocket authentication failed.");

		while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
		{
			var text = await ReceiveText(socket, cancellationToken);
			var message = JsonConvert.DeserializeObject<T>(text, _jsonSettings);
			if (message != null)
				await handler(message, cancellationToken);
		}
	}

	private static Task SendText(ClientWebSocket socket, string text, CancellationToken cancellationToken)
	{
		var bytes = Encoding.UTF8.GetBytes(text);
		return socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
	}

	private static async Task<string> ReceiveText(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		var buffer = new byte[16 * 1024];
		using var stream = new MemoryStream();
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException($"Questrade WebSocket closed: {result.CloseStatus} {result.CloseStatusDescription}");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException($"Unexpected Questrade WebSocket message type {result.MessageType}.");
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("Questrade WebSocket message exceeds 4 MiB.");
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
		}
	}
}
