namespace StockSharp.Osmosis.Native;

sealed class OsmosisSocketClient : BaseLogReceiver
{
	private const int _maximumMessageLength = 16 * 1024 * 1024;
	private const string _swapQuery =
		"tm.event='Tx' AND token_swapped.module='gamm'";

	private readonly Uri _endpoint;
	private readonly Func<OsmosisSwapEvent, ValueTask> _swapHandler;
	private readonly Func<Exception, ValueTask> _errorHandler;
	private readonly Lock _sync = new();
	private ClientWebSocket _socket;
	private CancellationTokenSource _lifetime;
	private Task _receiveTask;
	private TaskCompletionSource<bool> _subscriptionCompletion;
	private long _requestId;
	private bool _isDisposed;

	public OsmosisSocketClient(string endpoint,
		Func<OsmosisSwapEvent, ValueTask> swapHandler,
		Func<Exception, ValueTask> errorHandler)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"wss://{endpoint.TrimStart('/')}";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("ws" or "wss") ||
			(_endpoint.Scheme == "ws" && !_endpoint.IsLoopback))
			throw new ArgumentException(
				"Osmosis streaming endpoint must use WSS, except for a local node.",
				nameof(endpoint));
		_swapHandler = swapHandler ?? throw new ArgumentNullException(
			nameof(swapHandler));
		_errorHandler = errorHandler ?? throw new ArgumentNullException(
			nameof(errorHandler));
	}

	public override string Name => "Osmosis_WebSocket";

	public bool IsConnected => _socket?.State == WebSocketState.Open;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		if (_socket is not null)
			throw new InvalidOperationException(
				"The Osmosis WebSocket is already initialized.");
		_socket = new();
		_socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		_lifetime = new();
		await _socket.ConnectAsync(_endpoint, cancellationToken);
		_subscriptionCompletion = new(
			TaskCreationOptions.RunContinuationsAsynchronously);
		_receiveTask = ReceiveLoopAsync(_lifetime.Token);
		var id = Interlocked.Increment(ref _requestId);
		await SendAsync(new()
		{
			Id = id,
			Method = "subscribe",
			Parameters = new() { Query = _swapQuery },
		}, cancellationToken);
		await _subscriptionCompletion.Task.WaitAsync(cancellationToken);
	}

	private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
	{
		var buffer = new byte[64 * 1024];
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				using var stream = new MemoryStream();
				WebSocketReceiveResult result;
				do
				{
					result = await _socket.ReceiveAsync(
						new ArraySegment<byte>(buffer), cancellationToken);
					if (result.MessageType == WebSocketMessageType.Close)
						throw new WebSocketException(
							"Osmosis streaming endpoint closed the connection.");
					if (stream.Length + result.Count > _maximumMessageLength)
						throw new InvalidDataException(
							"Osmosis streaming message exceeds the safety limit.");
					stream.Write(buffer, 0, result.Count);
				}
				while (!result.EndOfMessage);
				if (result.MessageType != WebSocketMessageType.Text)
					continue;
				var message = JsonConvert.DeserializeObject<OsmosisSocketMessage>(
					Encoding.UTF8.GetString(stream.GetBuffer(), 0,
						checked((int)stream.Length)));
				if (message is not null)
					await HandleAsync(message);
			}
		}
		catch (Exception error) when (error is not OperationCanceledException &&
			!cancellationToken.IsCancellationRequested && !_isDisposed)
		{
			_subscriptionCompletion?.TrySetException(error);
			await _errorHandler(error);
		}
	}

	private async ValueTask HandleAsync(OsmosisSocketMessage message)
	{
		if (message.Id is not null)
		{
			if (message.Error is not null)
				_subscriptionCompletion?.TrySetException(
					new InvalidOperationException(
						$"Osmosis subscribe failed ({message.Error.Code}): " +
						message.Error.Message));
			else
				_subscriptionCompletion?.TrySetResult(true);
		}

		var transaction = message.Result?.Data?.Value?.TransactionResult;
		if (transaction?.Result is not { Code: 0 } execution ||
			transaction.Height.IsEmpty() || transaction.TransactionBytes.IsEmpty())
			return;
		if (!long.TryParse(transaction.Height, NumberStyles.None,
			CultureInfo.InvariantCulture, out var height) || height <= 0)
			throw new InvalidDataException(
				$"Osmosis returned invalid event height '{transaction.Height}'.");
		byte[] transactionBytes;
		try
		{
			transactionBytes = Convert.FromBase64String(
				transaction.TransactionBytes);
		}
		catch (FormatException error)
		{
			throw new InvalidDataException(
				"Osmosis returned invalid transaction bytes.", error);
		}
		var hash = Convert.ToHexString(SHA256.HashData(transactionBytes));
		foreach (var item in execution.Events ?? [])
		{
			if (item?.Type != "token_swapped")
				continue;
			var module = FindAttribute(item, "module");
			if (module != "gamm")
				continue;
			var poolText = FindAttribute(item, "pool_id");
			var inputText = FindAttribute(item, "tokens_in");
			var outputText = FindAttribute(item, "tokens_out");
			if (!ulong.TryParse(poolText, NumberStyles.None,
				CultureInfo.InvariantCulture, out var poolId) || poolId == 0 ||
				inputText.IsEmpty() || outputText.IsEmpty())
				continue;
			var messageIndexText = FindAttribute(item, "msg_index");
			var messageIndex = 0;
			if (!messageIndexText.IsEmpty() &&
				(!int.TryParse(messageIndexText, NumberStyles.None,
					CultureInfo.InvariantCulture, out messageIndex) ||
					messageIndex < 0))
				continue;
			await _swapHandler(new()
			{
				TransactionHash = hash,
				Height = height,
				PoolId = poolId,
				MessageIndex = messageIndex,
				Input = inputText.ParseCoin("swap input"),
				Output = outputText.ParseCoin("swap output"),
			});
		}
	}

	private static string FindAttribute(OsmosisSocketEvent item, string key)
		=> (item.Attributes ?? []).FirstOrDefault(attribute =>
			attribute?.Key == key)?.Value;

	private async ValueTask SendAsync(OsmosisSocketRequest request,
		CancellationToken cancellationToken)
	{
		var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request,
			new JsonSerializerSettings
			{
				NullValueHandling = NullValueHandling.Ignore,
			}));
		await _socket.SendAsync(new ArraySegment<byte>(data),
			WebSocketMessageType.Text, true, cancellationToken);
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_lifetime?.Cancel();
		_socket?.Abort();
		_socket?.Dispose();
		_lifetime?.Dispose();
		_subscriptionCompletion?.TrySetCanceled();
		base.DisposeManaged();
	}
}
