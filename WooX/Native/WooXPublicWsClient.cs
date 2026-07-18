namespace StockSharp.WooX.Native;

readonly record struct WooXWsSubscriptionKey(WooXWsTopics Topic, string Symbol,
	TimeSpan TimeFrame);

sealed class WooXPublicWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<WooXWsSubscriptionKey, string> _subscriptions = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private long _localId;

	public WooXPublicWsClient(string endpoint, string applicationId, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/" +
			Uri.EscapeDataString(applicationId.ThrowIfEmpty(nameof(applicationId)));
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(WooX) + "_PublicWs";

	public event Func<WooXWsTicker, long, CancellationToken, ValueTask> TickerReceived;
	public event Func<WooXWsBestBidOffer, long, CancellationToken, ValueTask> BestBidOfferReceived;
	public event Func<WooXWsBook, long, CancellationToken, ValueTask> BookReceived;
	public event Func<WooXWsTrade, long, CancellationToken, ValueTask> TradeReceived;
	public event Func<WooXWsCandle, long, CancellationToken, ValueTask> CandleReceived;
	public event Func<WooXWsTopics, WooXWsReferencePrice, long, CancellationToken, ValueTask> ReferencePriceReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException("WOO X public WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	public ValueTask SubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.Ticker, symbol, default), true,
			cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.Ticker, symbol, default), false,
			cancellationToken);

	public ValueTask SubscribeBestBidOfferAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.BestBidOffer, symbol, default), true,
			cancellationToken);

	public ValueTask UnsubscribeBestBidOfferAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.BestBidOffer, symbol, default), false,
			cancellationToken);

	public ValueTask SubscribeBookAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.OrderBook, symbol, default), true,
			cancellationToken);

	public ValueTask UnsubscribeBookAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.OrderBook, symbol, default), false,
			cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.Trade, symbol, default), true,
			cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.Trade, symbol, default), false,
			cancellationToken);

	public ValueTask SubscribeCandlesAsync(string symbol, TimeSpan timeFrame,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.Candle, symbol, timeFrame), true,
			cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string symbol, TimeSpan timeFrame,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.Candle, symbol, timeFrame), false,
			cancellationToken);

	public ValueTask SubscribeIndexPriceAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.IndexPrice, symbol, default), true,
			cancellationToken);

	public ValueTask UnsubscribeIndexPriceAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.IndexPrice, symbol, default), false,
			cancellationToken);

	public ValueTask SubscribeMarkPriceAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.MarkPrice, symbol, default), true,
			cancellationToken);

	public ValueTask UnsubscribeMarkPriceAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.MarkPrice, symbol, default), false,
			cancellationToken);

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> _client is { IsConnected: true } client
			? SendAsync(client, new WooXWsHeartbeat { Event = "ping" }, cancellationToken)
			: default;

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(client, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(socket, message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _jsonSettings,
		};
		client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-WooX-Connector/1.0");
		return client;
	}

	private async ValueTask DisposeClientAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
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

	private async ValueTask OnStateChangedAsync(WebSocketClient client, ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			KeyValuePair<WooXWsSubscriptionKey, string>[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var subscription in subscriptions)
				await SendSubscriptionAsync(client, subscription.Key, subscription.Value, true,
					cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(WooXWsSubscriptionKey subscription,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		string id;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				if (_subscriptions.ContainsKey(subscription))
					return;
				id = Interlocked.Increment(ref _localId).ToString(CultureInfo.InvariantCulture);
				_subscriptions.Add(subscription, id);
			}
			else if (!_subscriptions.Remove(subscription, out id))
				return;
		}
		if (_client?.IsConnected == true)
			await SendSubscriptionAsync(_client, subscription, id, isSubscribe,
				cancellationToken);
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		WooXWsSubscriptionKey subscription, string id, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendAsync(client, new WooXWsCommand
		{
			Id = id,
			Topic = GetTopic(subscription),
			Event = isSubscribe ? "subscribe" : "unsubscribe",
		}, cancellationToken);

	private static string GetTopic(WooXWsSubscriptionKey subscription)
		=> subscription.Topic switch
		{
			WooXWsTopics.Ticker => subscription.Symbol + "@ticker",
			WooXWsTopics.BestBidOffer => subscription.Symbol + "@bbo",
			WooXWsTopics.OrderBook => subscription.Symbol + "@orderbook",
			WooXWsTopics.Trade => subscription.Symbol + "@trade",
			WooXWsTopics.Candle => subscription.Symbol + "@kline_" +
				subscription.TimeFrame.ToWooXWsInterval(),
			WooXWsTopics.IndexPrice => subscription.Symbol + "@indexprice",
			WooXWsTopics.MarkPrice => subscription.Symbol + "@markprice",
			_ => throw new ArgumentOutOfRangeException(nameof(subscription), subscription, null),
		};

	private async ValueTask SendAsync<TPayload>(WebSocketClient client, TPayload payload,
		CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(payload, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client, WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<WooXWsHeader>(payload);
			if (header.Event.EqualsIgnoreCase("ping"))
			{
				await SendAsync(client, new WooXWsHeartbeat { Event = "pong" }, cancellationToken);
				return;
			}
			if (!header.Event.IsEmpty())
			{
				if (header.IsSuccess == false)
					throw new InvalidOperationException(
						$"WOO X WebSocket request failed: {header.Message}".Trim());
				return;
			}
			if (header.Topic.EndsWith("@ticker", StringComparison.OrdinalIgnoreCase))
			{
				var envelope = Deserialize<WooXWsEnvelope<WooXWsTicker>>(payload);
				if (envelope.Data is not null && TickerReceived is { } tickerHandler)
					await tickerHandler(envelope.Data, envelope.Timestamp, cancellationToken);
			}
			else if (header.Topic.EndsWith("@bbo", StringComparison.OrdinalIgnoreCase))
			{
				var envelope = Deserialize<WooXWsEnvelope<WooXWsBestBidOffer>>(payload);
				if (envelope.Data is not null && BestBidOfferReceived is { } bboHandler)
					await bboHandler(envelope.Data, envelope.Timestamp, cancellationToken);
			}
			else if (header.Topic.EndsWith("@orderbook", StringComparison.OrdinalIgnoreCase) ||
				header.Topic.EndsWith("@orderbook100", StringComparison.OrdinalIgnoreCase))
			{
				var envelope = Deserialize<WooXWsEnvelope<WooXWsBook>>(payload);
				if (envelope.Data is not null && BookReceived is { } bookHandler)
					await bookHandler(envelope.Data, envelope.Timestamp, cancellationToken);
			}
			else if (header.Topic.EndsWith("@trade", StringComparison.OrdinalIgnoreCase))
			{
				var envelope = Deserialize<WooXWsEnvelope<WooXWsTrade>>(payload);
				if (envelope.Data is not null && TradeReceived is { } tradeHandler)
					await tradeHandler(envelope.Data, envelope.Timestamp, cancellationToken);
			}
			else if (header.Topic.Contains("@kline_", StringComparison.OrdinalIgnoreCase))
			{
				var envelope = Deserialize<WooXWsEnvelope<WooXWsCandle>>(payload);
				if (envelope.Data is not null && CandleReceived is { } candleHandler)
					await candleHandler(envelope.Data, envelope.Timestamp, cancellationToken);
			}
			else if (header.Topic.EndsWith("@indexprice", StringComparison.OrdinalIgnoreCase) ||
				header.Topic.EndsWith("@markprice", StringComparison.OrdinalIgnoreCase))
			{
				var envelope = Deserialize<WooXWsEnvelope<WooXWsReferencePrice>>(payload);
				if (envelope.Data is not null && ReferencePriceReceived is { } priceHandler)
					await priceHandler(header.Topic.EndsWith("@indexprice",
						StringComparison.OrdinalIgnoreCase)
						? WooXWsTopics.IndexPrice
						: WooXWsTopics.MarkPrice,
						envelope.Data, envelope.Timestamp, cancellationToken);
			}
			else
				throw new InvalidDataException($"Unknown WOO X WebSocket topic '{header.Topic}'.");
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private TData Deserialize<TData>(string payload)
		=> JsonConvert.DeserializeObject<TData>(payload, _jsonSettings)
			?? throw new InvalidDataException("WOO X WebSocket returned an empty message.");

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;
}
