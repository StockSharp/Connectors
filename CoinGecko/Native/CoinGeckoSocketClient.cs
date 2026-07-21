namespace StockSharp.CoinGecko.Native;

readonly record struct CoinGeckoStreamKey(
	CoinGeckoSocketChannels Channel,
	string CoinId,
	string QuoteCurrency,
	string Network,
	string PoolAddress,
	string TokenAddress,
	CoinGeckoSocketIntervals? Interval)
{
	public static CoinGeckoStreamKey CoinPrice(string coinId, string quoteCurrency)
		=> new(CoinGeckoSocketChannels.CoinPrice, coinId, quoteCurrency,
			null, null, null, null);

	public static CoinGeckoStreamKey TokenPrice(string network, string tokenAddress)
		=> new(CoinGeckoSocketChannels.OnchainTokenPrice, null, null,
			network, null, tokenAddress, null);

	public static CoinGeckoStreamKey PoolTrades(string network, string poolAddress)
		=> new(CoinGeckoSocketChannels.OnchainTrade, null, null,
			network, poolAddress, null, null);

	public static CoinGeckoStreamKey PoolOhlcv(string network, string poolAddress,
		CoinGeckoSocketIntervals interval)
		=> new(CoinGeckoSocketChannels.OnchainOhlcv, null, null,
			network, poolAddress, null, interval);
}

sealed class CoinGeckoSocketClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly string _apiKey;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _connectionGate = new(1, 1);
	private readonly SemaphoreSlim _subscriptionGate = new(1, 1);
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly HashSet<CoinGeckoStreamKey> _subscriptions = [];
	private readonly JsonSerializerSettings _settings = new()
	{
		Culture = CultureInfo.InvariantCulture,
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		Formatting = Formatting.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private WebSocketClient _client;
	private bool _isDisposed;

	public CoinGeckoSocketClient(string endpoint, SecureString apiKey,
		WorkingTime workingTime, int reconnectAttempts)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/'), UriKind.Absolute,
			out var uri) || uri.Scheme != "wss" || uri.Host.IsEmpty() ||
			!uri.Query.IsEmpty() || !uri.Fragment.IsEmpty())
			throw new ArgumentException(
				"A valid secure CoinGecko WebSocket endpoint is required.",
				nameof(endpoint));
		if (apiKey.IsEmpty())
			throw new ArgumentNullException(nameof(apiKey));
		_endpoint = uri;
		_apiKey = apiKey.UnSecure().Trim();
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "CoinGecko_WS";

	public event Func<CoinGeckoCoinPriceUpdate, CancellationToken, ValueTask>
		CoinPriceReceived;
	public event Func<CoinGeckoOnchainPriceUpdate, CancellationToken, ValueTask>
		OnchainPriceReceived;
	public event Func<CoinGeckoOnchainTradeUpdate, CancellationToken, ValueTask>
		TradeReceived;
	public event Func<CoinGeckoOnchainOhlcvUpdate, CancellationToken, ValueTask>
		OhlcvReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async ValueTask SubscribeAsync(CoinGeckoStreamKey key,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		ValidateKey(key);
		await _subscriptionGate.WaitAsync(cancellationToken);
		try
		{
			bool isFirstChannel;
			using (_sync.EnterScope())
			{
				if (!_subscriptions.Add(key))
					return;
				isFirstChannel = !_subscriptions.Any(item => item.Channel ==
					key.Channel && item != key);
			}
			try
			{
				await EnsureConnectedAsync(cancellationToken);
				if (isFirstChannel)
					await SendChannelAsync(key.Channel,
						CoinGeckoSocketCommands.Subscribe, cancellationToken);
				await SendDataAsync(key, true, cancellationToken);
			}
			catch
			{
				using (_sync.EnterScope())
					_subscriptions.Remove(key);
				throw;
			}
		}
		finally
		{
			_subscriptionGate.Release();
		}
	}

	public async ValueTask UnsubscribeAsync(CoinGeckoStreamKey key,
		CancellationToken cancellationToken)
	{
		await _subscriptionGate.WaitAsync(cancellationToken);
		try
		{
			bool isLastChannel;
			using (_sync.EnterScope())
			{
				if (!_subscriptions.Remove(key))
					return;
				isLastChannel = !_subscriptions.Any(item => item.Channel == key.Channel);
			}
			var client = _client;
			if (client?.IsConnected != true)
				return;
			await SendDataAsync(key, false, cancellationToken);
			if (isLastChannel)
				await SendChannelAsync(key.Channel,
					CoinGeckoSocketCommands.Unsubscribe, cancellationToken);
		}
		finally
		{
			_subscriptionGate.Release();
		}
	}

	private async ValueTask EnsureConnectedAsync(
		CancellationToken cancellationToken)
	{
		await _connectionGate.WaitAsync(cancellationToken);
		try
		{
			EnsureClient();
			if (!_client.IsConnected)
				await _client.ConnectAsync(cancellationToken);
		}
		finally
		{
			_connectionGate.Release();
		}
	}

	private void EnsureClient()
	{
		if (_client is not null)
			return;
		var client = new WebSocketClient(_endpoint.AbsoluteUri,
			OnStateChangedAsync, RaiseErrorAsync, OnProcessAsync,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _settings,
		};
		client.Init += socket =>
		{
			socket.Options.SetRequestHeader("x-cg-pro-api-key", _apiKey);
			socket.Options.SetRequestHeader("User-Agent",
				"StockSharp-CoinGecko/1.0");
		};
		_client = client;
	}

	private async ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		this.AddInfoLog("CoinGecko WebSocket state: {0}.", state);
		if (state != ConnectionStates.Restored)
			return;
		await _subscriptionGate.WaitAsync(cancellationToken);
		try
		{
			CoinGeckoStreamKey[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var channel in subscriptions.Select(item => item.Channel).Distinct())
				await SendChannelAsync(channel, CoinGeckoSocketCommands.Subscribe,
					cancellationToken);
			foreach (var subscription in subscriptions)
				await SendDataAsync(subscription, true, cancellationToken);
		}
		finally
		{
			_subscriptionGate.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		_ = client;
		var json = message.AsString();
		if (json.IsEmpty())
			return;
		try
		{
			var route = JsonConvert.DeserializeObject<CoinGeckoSocketRoute>(json,
				_settings) ?? throw new InvalidDataException(
					"CoinGecko WebSocket returned an empty JSON message.");
			if (route.Code is >= 4000 ||
				route.MessageType == CoinGeckoSocketMessageTypes.RejectSubscription)
				throw new InvalidOperationException(
					$"CoinGecko WebSocket error{FormatCode(route.Code)}: " +
					route.Message.IsEmpty("subscription rejected"));
			if (route.MessageType is not null || route.Code is not null)
				return;

			if (route.OhlcvChannel == CoinGeckoSocketChannelCodes.OnchainOhlcv)
			{
				var update = Deserialize<CoinGeckoOnchainOhlcvUpdate>(json);
				if (OhlcvReceived is { } handler)
					await handler(update, cancellationToken);
				return;
			}
			switch (route.Channel)
			{
				case CoinGeckoSocketChannelCodes.CoinPrice:
					if (CoinPriceReceived is { } coinHandler)
						await coinHandler(Deserialize<CoinGeckoCoinPriceUpdate>(json),
							cancellationToken);
					break;
				case CoinGeckoSocketChannelCodes.OnchainTokenPrice:
					if (OnchainPriceReceived is { } priceHandler)
						await priceHandler(Deserialize<CoinGeckoOnchainPriceUpdate>(json),
							cancellationToken);
					break;
				case CoinGeckoSocketChannelCodes.OnchainTrade:
					if (TradeReceived is { } tradeHandler)
						await tradeHandler(Deserialize<CoinGeckoOnchainTradeUpdate>(json),
							cancellationToken);
					break;
				default:
					throw new InvalidDataException(
						"CoinGecko WebSocket returned an unsupported data message.");
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or FormatException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private T Deserialize<T>(string json)
		=> JsonConvert.DeserializeObject<T>(json, _settings) ??
			throw new InvalidDataException(
				$"CoinGecko returned an empty {typeof(T).Name} message.");

	private ValueTask SendChannelAsync(CoinGeckoSocketChannels channel,
		CoinGeckoSocketCommands command, CancellationToken cancellationToken)
		=> SendAsync(new CoinGeckoSocketRequest
		{
			Command = command,
			Identifier = Serialize(new CoinGeckoSocketIdentifier
			{
				Channel = channel,
			}),
		}, cancellationToken);

	private ValueTask SendDataAsync(CoinGeckoStreamKey key, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var identifier = Serialize(new CoinGeckoSocketIdentifier
		{
			Channel = key.Channel,
		});
		string data = key.Channel switch
		{
			CoinGeckoSocketChannels.CoinPrice => Serialize(
				new CoinGeckoCoinStreamRequest
				{
					CoinIds = [key.CoinId],
					QuoteCurrencies = isSubscribe ? [key.QuoteCurrency] : null,
					Action = isSubscribe ? CoinGeckoSocketActions.SetTokens :
						CoinGeckoSocketActions.UnsetTokens,
				}),
			CoinGeckoSocketChannels.OnchainTokenPrice => Serialize(
				new CoinGeckoOnchainTokenStreamRequest
				{
					Tokens = [key.Network + ":" + key.TokenAddress],
					Action = isSubscribe ? CoinGeckoSocketActions.SetTokens :
						CoinGeckoSocketActions.UnsetTokens,
				}),
			CoinGeckoSocketChannels.OnchainTrade => Serialize(
				new CoinGeckoOnchainPoolStreamRequest
				{
					Pools = [key.Network + ":" + key.PoolAddress],
					Action = isSubscribe ? CoinGeckoSocketActions.SetPools :
						CoinGeckoSocketActions.UnsetPools,
				}),
			CoinGeckoSocketChannels.OnchainOhlcv => Serialize(
				new CoinGeckoOnchainOhlcvStreamRequest
				{
					Pools = [key.Network + ":" + key.PoolAddress],
					Interval = key.Interval ?? throw new InvalidOperationException(
						"CoinGecko OHLCV interval is missing."),
					Token = CoinGeckoOnchainTokens.Base,
					Action = isSubscribe ? CoinGeckoSocketActions.SetPools :
						CoinGeckoSocketActions.UnsetPools,
				}),
			_ => throw new ArgumentOutOfRangeException(nameof(key), key.Channel, null),
		};
		return SendAsync(new CoinGeckoSocketRequest
		{
			Command = CoinGeckoSocketCommands.Message,
			Identifier = identifier,
			Data = data,
		}, cancellationToken);
	}

	private string Serialize<T>(T value)
		=> JsonConvert.SerializeObject(value, _settings);

	private async ValueTask SendAsync(CoinGeckoSocketRequest request,
		CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true)
			throw new InvalidOperationException(
				"CoinGecko WebSocket is not connected.");
		await _sendGate.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(request, cancellationToken);
		}
		finally
		{
			_sendGate.Release();
		}
	}

	private static void ValidateKey(CoinGeckoStreamKey key)
	{
		switch (key.Channel)
		{
			case CoinGeckoSocketChannels.CoinPrice:
				key.CoinId.ThrowIfEmpty(nameof(key.CoinId));
				key.QuoteCurrency.ThrowIfEmpty(nameof(key.QuoteCurrency));
				break;
			case CoinGeckoSocketChannels.OnchainTokenPrice:
				key.Network.ThrowIfEmpty(nameof(key.Network));
				key.TokenAddress.ThrowIfEmpty(nameof(key.TokenAddress));
				break;
			case CoinGeckoSocketChannels.OnchainTrade:
				key.Network.ThrowIfEmpty(nameof(key.Network));
				key.PoolAddress.ThrowIfEmpty(nameof(key.PoolAddress));
				break;
			case CoinGeckoSocketChannels.OnchainOhlcv:
				key.Network.ThrowIfEmpty(nameof(key.Network));
				key.PoolAddress.ThrowIfEmpty(nameof(key.PoolAddress));
				if (key.Interval is null)
					throw new ArgumentException(
						"CoinGecko OHLCV interval is required.", nameof(key));
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(key), key.Channel, null);
		}
	}

	private static string FormatCode(int? code)
		=> code is int value ? $" ({value})" : string.Empty;

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		using (_sync.EnterScope())
			_subscriptions.Clear();
		if (client is null)
			return;
		try
		{
			if (client.IsConnected)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client.Dispose();
		}
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		DisconnectAsync(default).AsTask().GetAwaiter().GetResult();
		_connectionGate.Dispose();
		_subscriptionGate.Dispose();
		_sendGate.Dispose();
		base.DisposeManaged();
	}
}
