namespace StockSharp.XOpenHub.Native;

internal sealed class XApiException : InvalidOperationException
{
	public XApiException(string code, string message)
		: base($"X Open Hub xAPI error {code.IsEmpty("UNKNOWN")}: {message.IsEmpty("Unknown error")}.")
	{
		Code = code;
	}

	public string Code { get; }
}

internal sealed class XApiCommandClient : BaseLogReceiver
{
	private const int _maxMessageSize = 16 * 1024 * 1024;
	private static readonly TimeSpan _requestInterval = TimeSpan.FromMilliseconds(200);

	private readonly Uri _uri;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private ClientWebSocket _socket;
	private DateTime _lastRequest;
	private long _requestId;

	public XApiCommandClient(bool isDemo)
	{
		_uri = new(isDemo ? "wss://ws.xapi.pro/demo" : "wss://ws.xapi.pro/real");
	}

	public override string Name => nameof(XOpenHub) + "_Command";

	public string StreamSessionId { get; private set; }

	public async Task Connect(string userId, string password, string applicationName,
		CancellationToken cancellationToken)
	{
		if (_socket != null)
			throw new InvalidOperationException("X Open Hub command socket is already connected.");

		var socket = new ClientWebSocket();
		socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		try
		{
			await socket.ConnectAsync(_uri, cancellationToken);
			_socket = socket;
			var response = await Send<XApiLoginArguments, XApiEmptyResult>("login", new()
			{
				UserId = userId.ThrowIfEmpty(nameof(userId)),
				Password = password.ThrowIfEmpty(nameof(password)),
				ApplicationName = applicationName,
			}, cancellationToken);
			StreamSessionId = response.StreamSessionId.ThrowIfEmpty(nameof(response.StreamSessionId));
		}
		catch
		{
			_socket = null;
			socket.Dispose();
			throw;
		}
	}

	public Task<XApiSymbol[]> GetAllSymbols(CancellationToken cancellationToken)
		=> SendData<XApiEmptyArguments, XApiSymbol[]>("getAllSymbols", null, cancellationToken);

	public Task<XApiChartData> GetChartRange(string symbol, int period, DateTimeOffset from,
		DateTimeOffset to, CancellationToken cancellationToken)
		=> SendData<XApiChartArguments, XApiChartData>("getChartRangeRequest", new()
		{
			Info = new()
			{
				Symbol = symbol.ThrowIfEmpty(nameof(symbol)),
				Period = period,
				Start = from.ToUnixTimeMilliseconds(),
				End = to.ToUnixTimeMilliseconds(),
				Ticks = 0,
			},
		}, cancellationToken);

	public Task<XApiMarginLevel> GetMarginLevel(CancellationToken cancellationToken)
		=> SendData<XApiEmptyArguments, XApiMarginLevel>("getMarginLevel", null,
			cancellationToken);

	public Task<XApiTrade[]> GetTrades(bool openedOnly, CancellationToken cancellationToken)
		=> SendData<XApiTradesArguments, XApiTrade[]>("getTrades",
			new() { OpenedOnly = openedOnly }, cancellationToken);

	public Task<XApiTrade[]> GetTradesHistory(DateTimeOffset from, DateTimeOffset to,
		CancellationToken cancellationToken)
		=> SendData<XApiTradesHistoryArguments, XApiTrade[]>("getTradesHistory", new()
		{
			Start = from.ToUnixTimeMilliseconds(),
			End = to.ToUnixTimeMilliseconds(),
		}, cancellationToken);

	public Task<XApiTradeTransactionResult> TradeTransaction(XApiTradeTransactionInfo trade,
		CancellationToken cancellationToken)
		=> SendData<XApiTradeTransactionArguments, XApiTradeTransactionResult>("tradeTransaction",
			new() { Trade = trade ?? throw new ArgumentNullException(nameof(trade)) }, cancellationToken);

	public Task<XApiTradeStatus> GetTradeTransactionStatus(long order,
		CancellationToken cancellationToken)
		=> SendData<XApiTradeStatusArguments, XApiTradeStatus>("tradeTransactionStatus",
			new() { Order = order }, cancellationToken);

	public async Task<XApiTradeStatus> WaitTradeTransaction(long order,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; attempt < 30; attempt++)
		{
			var status = await GetTradeTransactionStatus(order, cancellationToken);
			if (status.RequestStatus == 3)
				return status;
			if (status.RequestStatus is 0 or 4)
				throw new InvalidOperationException(
					$"X Open Hub rejected trade transaction {order}: {status.Message.IsEmpty("No reason supplied")}.");
		}
		throw new TimeoutException($"X Open Hub trade transaction {order} remained pending.");
	}

	public async Task Ping(CancellationToken cancellationToken)
		=> await Send<XApiEmptyArguments, XApiEmptyResult>("ping", null, cancellationToken);

	public async Task Disconnect()
	{
		var socket = _socket;
		if (socket == null)
			return;

		try
		{
			if (socket.State == WebSocketState.Open)
				await Send<XApiEmptyArguments, XApiEmptyResult>("logout", null, CancellationToken.None);
		}
		catch (Exception error)
		{
			this.AddWarningLog("X Open Hub logout failed: {0}", error.Message);
		}

		_socket = null;
		StreamSessionId = null;
		try
		{
			if (socket.State == WebSocketState.Open)
				await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect",
					CancellationToken.None);
		}
		catch (WebSocketException)
		{
		}
		socket.Dispose();
	}

	private async Task<TResponse> SendData<TArguments, TResponse>(string command,
		TArguments arguments, CancellationToken cancellationToken)
	{
		var response = await Send<TArguments, TResponse>(command, arguments, cancellationToken);
		return response.Data;
	}

	private async Task<XApiResponse<TResponse>> Send<TArguments, TResponse>(string command,
		TArguments arguments, CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var socket = _socket;
			if (socket?.State != WebSocketState.Open)
				throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

			var wait = _requestInterval - (DateTime.UtcNow - _lastRequest);
			if (wait > TimeSpan.Zero)
				await Task.Delay(wait, cancellationToken);

			var tag = Interlocked.Increment(ref _requestId).ToString(CultureInfo.InvariantCulture);
			var request = new XApiCommand<TArguments>
			{
				Command = command,
				Arguments = arguments,
				CustomTag = tag,
			};
			var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request,
				new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
			if (bytes.Length > 1024)
				throw new InvalidOperationException(
					$"X Open Hub command '{command}' exceeds the protocol's 1 KiB limit.");

			await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
			_lastRequest = DateTime.UtcNow;
			var response = JsonConvert.DeserializeObject<XApiResponse<TResponse>>(
				await ReceiveText(socket, cancellationToken))
				?? throw new InvalidDataException(
					$"X Open Hub returned an invalid response to '{command}'.");

			if (!response.Status)
				throw new XApiException(response.ErrorCode, response.ErrorDescription);
			if (!response.CustomTag.IsEmpty() && !response.CustomTag.Equals(tag, StringComparison.Ordinal))
				throw new InvalidDataException(
					$"X Open Hub response tag '{response.CustomTag}' does not match request '{tag}'.");
			return response;
		}
		finally
		{
			_gate.Release();
		}
	}

	private static async Task<string> ReceiveText(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[16 * 1024];
		using var stream = new MemoryStream();
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException(
					$"X Open Hub command socket closed: {result.CloseStatus} {result.CloseStatusDescription}");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException(
					$"Unexpected X Open Hub command message type {result.MessageType}.");
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("X Open Hub command response exceeds 16 MiB.");
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
		}
	}

	protected override void DisposeManaged()
	{
		Disconnect().GetAwaiter().GetResult();
		_gate.Dispose();
		base.DisposeManaged();
	}
}
