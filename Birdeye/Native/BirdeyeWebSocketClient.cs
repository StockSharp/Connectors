namespace StockSharp.Birdeye.Native;

sealed class BirdeyeWebSocketClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly string _origin;
	private readonly string _apiKey;
	private readonly string _chain;
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private ClientWebSocket _socket;
	private CancellationTokenSource _receiveCancellation;
	private Task _receiveTask;

	public BirdeyeWebSocketClient(
		string endpoint,
		string origin,
		SecureString apiKey,
		string chain)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint))
			.Trim()
			.TrimEnd('/');
		if (!Uri.TryCreate(
			_endpoint, UriKind.Absolute, out var endpointUri) ||
			endpointUri.Scheme is not ("ws" or "wss"))
			throw new ArgumentException(
				"Birdeye WebSocket endpoint must be an absolute " +
					"WebSocket URL.",
				nameof(endpoint));
		_origin = origin.ThrowIfEmpty(nameof(origin)).Trim();
		if (!Uri.TryCreate(
			_origin, UriKind.Absolute, out var originUri) ||
			originUri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Birdeye WebSocket origin must be an absolute " +
					"HTTP URL.",
				nameof(origin));
		_apiKey = apiKey.UnSecure();
		if (_apiKey.IsEmpty())
			throw new ArgumentException(
				"Birdeye API key is required.",
				nameof(apiKey));
		_chain = BirdeyeExtensions.NormalizeChain(chain);
	}

	public override string Name => "Birdeye_WS";

	public event Func<
		BirdeyeCandle,
		CancellationToken,
		ValueTask> CandleReceived;

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		if (_socket is not null)
			throw new InvalidOperationException(
				"Birdeye WebSocket is already connected.");
		var socket = new ClientWebSocket();
		socket.Options.AddSubProtocol("echo-protocol");
		socket.Options.SetRequestHeader("Origin", _origin);
		var uri = new Uri(
			$"{_endpoint}/{Uri.EscapeDataString(_chain)}" +
				$"?x-api-key={Uri.EscapeDataString(_apiKey)}");
		try
		{
			await socket.ConnectAsync(uri, cancellationToken);
		}
		catch
		{
			socket.Dispose();
			throw;
		}
		_socket = socket;
		_receiveCancellation = new();
		_receiveTask = ReceiveLoopAsync(
			socket, _receiveCancellation.Token);
	}

	public async ValueTask SubscribeAsync(
		IEnumerable<(string Address, string Interval)> subscriptions,
		bool priceInUsd,
		CancellationToken cancellationToken)
	{
		var values = (subscriptions ?? [])
			.Distinct()
			.ToArray();
		if (values.Length > 100)
			throw new InvalidOperationException(
				"Birdeye allows at most 100 price subscriptions " +
					"per WebSocket connection.");
		var payload = values.Length == 0
			? "{\"type\":\"UNSUBSCRIBE_PRICE\"}"
			: BuildSubscriptionPayload(values, priceInUsd);
		await SendAsync(payload, cancellationToken);
	}

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		var socket = _socket;
		if (socket is null)
			return;
		_receiveCancellation?.Cancel();
		try
		{
			if (socket.State is
				WebSocketState.Open or
				WebSocketState.CloseReceived)
				await socket.CloseOutputAsync(
					WebSocketCloseStatus.NormalClosure,
					"disconnect",
					cancellationToken);
		}
		catch (WebSocketException)
		{
		}
		if (_receiveTask is not null)
			try
			{
				await _receiveTask;
			}
			catch (OperationCanceledException)
			{
			}
		socket.Dispose();
		_receiveCancellation?.Dispose();
		_socket = null;
		_receiveCancellation = null;
		_receiveTask = null;
	}

	internal static string BuildSubscriptionPayload(
		IEnumerable<(string Address, string Interval)> subscriptions,
		bool priceInUsd)
	{
		var values = (subscriptions ?? [])
			.Distinct()
			.ToArray();
		if (values.Length == 0)
			throw new ArgumentException(
				"At least one Birdeye subscription is required.",
				nameof(subscriptions));
		if (values.Length > 100)
			throw new ArgumentOutOfRangeException(
				nameof(subscriptions),
				"Birdeye allows at most 100 addresses.");
		foreach (var value in values)
		{
			if (!BirdeyeExtensions.IsSafeAddress(value.Address))
				throw new ArgumentException(
					$"Invalid Birdeye address " +
						$"'{value.Address}'.",
					nameof(subscriptions));
			if (value.Interval.ToTimeFrame() is null)
				throw new ArgumentException(
					$"Invalid Birdeye interval " +
						$"'{value.Interval}'.",
					nameof(subscriptions));
		}
		var currency = priceInUsd ? "usd" : "native";
		var query = string.Join(
			" OR ",
			values.Select(value =>
				$"(address = {value.Address} AND " +
					$"chartType = {value.Interval} AND " +
					$"currency = {currency})"));
		return new JObject
		{
			["type"] = "SUBSCRIBE_PRICE",
			["data"] = new JObject
			{
				["queryType"] = "complex",
				["query"] = query,
				["mode"] = "scaled",
			},
		}.ToString(Formatting.None);
	}

	internal static BirdeyeCandle DeserializePrice(string json)
	{
		JToken root;
		try
		{
			root = JToken.Parse(
				json.ThrowIfEmpty(nameof(json)));
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Birdeye WebSocket returned invalid JSON.", error);
		}
		if (root is not JObject message ||
			!message.Value<string>("type")
				.EqualsIgnoreCase("PRICE_DATA") ||
			message["data"] is not JObject data)
			return null;
		var address = data.Value<string>("address");
		var interval = data.Value<string>("type");
		var timeFrame = interval.ToTimeFrame();
		var timestamp = Long(data["unixTime"]);
		if (address.IsEmpty() ||
			timeFrame is null ||
			timestamp is null)
			return null;
		DateTime time;
		try
		{
			time = DateTimeOffset.FromUnixTimeSeconds(
				timestamp.Value).UtcDateTime;
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
		}
		var useScaled =
			data["scaledO"] is not null &&
			data["scaledH"] is not null &&
			data["scaledL"] is not null &&
			data["scaledC"] is not null;
		return new()
		{
			Address = address,
			TimeFrame = timeFrame,
			OpenTime = time,
			Open = Decimal(
				useScaled ? data["scaledO"] : data["o"]) ?? 0,
			High = Decimal(
				useScaled ? data["scaledH"] : data["h"]) ?? 0,
			Low = Decimal(
				useScaled ? data["scaledL"] : data["l"]) ?? 0,
			Close = Decimal(
				useScaled ? data["scaledC"] : data["c"]) ?? 0,
			Volume = Decimal(
				useScaled ? data["scaledV"] : data["v"]) ?? 0,
			VolumeUsd = Decimal(data["vUsd"]),
		};
	}

	private async ValueTask SendAsync(
		string payload,
		CancellationToken cancellationToken)
	{
		var socket = _socket;
		if (socket?.State != WebSocketState.Open)
			throw new InvalidOperationException(
				"Birdeye WebSocket is not connected.");
		var bytes = Encoding.UTF8.GetBytes(payload);
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await socket.SendAsync(
				bytes,
				WebSocketMessageType.Text,
				true,
				cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async Task ReceiveLoopAsync(
		ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[16384];
		try
		{
			while (!cancellationToken.IsCancellationRequested &&
				socket.State is
					WebSocketState.Open or
					WebSocketState.CloseSent)
			{
				using var stream = new MemoryStream();
				WebSocketReceiveResult result;
				do
				{
					result = await socket.ReceiveAsync(
						buffer, cancellationToken);
					if (result.MessageType ==
						WebSocketMessageType.Close)
						return;
					stream.Write(buffer, 0, result.Count);
				}
				while (!result.EndOfMessage);
				if (result.MessageType !=
					WebSocketMessageType.Text)
					continue;
				var message = Encoding.UTF8.GetString(
					stream.GetBuffer(),
					0,
					checked((int)stream.Length));
				BirdeyeCandle candle;
				try
				{
					candle = DeserializePrice(message);
				}
				catch (Exception error) when (
					!cancellationToken.IsCancellationRequested)
				{
					this.AddErrorLog(error);
					continue;
				}
				if (candle is not null &&
					CandleReceived is not null)
					await CandleReceived.InvokeAsync(
						candle, cancellationToken);
			}
		}
		catch (OperationCanceledException) when (
			cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			this.AddErrorLog(error);
		}
	}

	private static decimal? Decimal(JToken value)
		=> decimal.TryParse(
			value?.ToString(),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: null;

	private static long? Long(JToken value)
		=> long.TryParse(
			value?.ToString(),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: null;

	protected override void DisposeManaged()
	{
		if (_socket is not null)
			DisconnectAsync(CancellationToken.None)
				.AsTask()
				.GetAwaiter()
				.GetResult();
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
