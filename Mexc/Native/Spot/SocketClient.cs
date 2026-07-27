namespace StockSharp.Mexc.Native.Spot;

using System.Globalization;
using System.IO;

using Google.Protobuf;

using Newtonsoft.Json.Linq;

using StockSharp.Mexc.Native.Protobuf;
using StockSharp.Mexc.Native.Spot.Model;

class SocketClient : BaseLogReceiver
{
	public override string Name => nameof(Mexc) + "_" + nameof(Spot) + nameof(SocketClient);

	public event Func<Ticker, CancellationToken, ValueTask> TickerReceived;
	public event Func<OrderBookUpdate, CancellationToken, ValueTask> OrderBookReceived;
	public event Func<TradeStream, CancellationToken, ValueTask> TradeReceived;
	public event Func<CandleStream, CancellationToken, ValueTask> CandleReceived;
	public event Func<PrivateAccountUpdate, CancellationToken, ValueTask> AccountReceived;
	public event Func<PrivateDealUpdate, CancellationToken, ValueTask> UserTradeReceived;
	public event Func<PrivateOrderUpdate, CancellationToken, ValueTask> OrderReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly WebSocketClient _publicClient;
	private WebSocketClient _privateClient;
	private readonly string _publicUrl;
	private readonly SynchronizedDictionary<int, long> _subscriptions = new();
	private int _subscriptionId;
	private long _tradeIdSeed;
	private string _listenKey;
	private CancellationTokenSource _pingCts;
	private Task _pingTask;
	private readonly int _reconnectAttempts;

	public SocketClient(MexcMessageAdapter adapter, Authenticator authenticator, WorkingTime workingTime)
	{
		if (adapter is null)
			throw new ArgumentNullException(nameof(adapter));

		_publicUrl = $"wss://{adapter.SpotWsDomain}";
		_ = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
		_reconnectAttempts = adapter.ReConnectionSettings.ReAttemptCount;

		_publicClient = CreateClient(_publicUrl, false, OnPublicProcess, workingTime);
	}

	protected override void DisposeManaged()
	{
		StopPingLoop();
		DisposePrivate();
		_publicClient.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask Connect(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);
		await _publicClient.ConnectAsync(cancellationToken);
		StartPingLoop();
	}

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		StopPingLoop();
		await DisconnectPrivateAsync(cancellationToken);
		await _publicClient.DisconnectAsync(cancellationToken);
	}

	public async ValueTask EnsurePrivateConnected(string listenKey, CancellationToken cancellationToken)
	{
		if (listenKey.IsEmpty())
			throw new ArgumentNullException(nameof(listenKey));

		if (_privateClient != null && _listenKey == listenKey)
			return;

		await DisconnectPrivateAsync(cancellationToken);

		_listenKey = listenKey;
		var url = $"{_publicUrl}?listenKey={listenKey}";
		_privateClient = CreateClient(url, true, OnPrivateProcess, _publicClient.WorkingTime);
		await _privateClient.ConnectAsync(cancellationToken);
		StartPingLoop();
	}

	public async ValueTask DisconnectPrivateAsync(CancellationToken cancellationToken)
	{
		var privateClient = TakePrivate();

		if (privateClient is null)
			return;

		try
		{
			await privateClient.DisconnectAsync(cancellationToken);
		}
		catch
		{
		}

		privateClient.Dispose();
	}

	// Drops the private client without a graceful close - used from disposal, which cannot await.
	private void DisposePrivate()
	{
		var privateClient = TakePrivate();

		if (privateClient is null)
			return;

		privateClient.Dispose();
	}

	private WebSocketClient TakePrivate()
	{
		var privateClient = _privateClient;
		_privateClient = null;
		_listenKey = null;
		return privateClient;
	}

	private WebSocketClient CreateClient(string url, bool isPrivate, Func<WebSocketMessage, CancellationToken, ValueTask> onProcess, WorkingTime workingTime)
	{
		return new WebSocketClient(
			url,
			(state, token) =>
			{
				if (!isPrivate && StateChanged is { } handler)
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
			onProcess,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
			SendSettings = Native.Extensions.CreateJsonSettings(),
		};
	}

	private ValueTask OnPublicProcess(WebSocketMessage msg, CancellationToken cancellationToken)
		=> OnProcess(msg, false, cancellationToken);

	private ValueTask OnPrivateProcess(WebSocketMessage msg, CancellationToken cancellationToken)
		=> OnProcess(msg, true, cancellationToken);

	private async ValueTask OnProcess(WebSocketMessage msg, bool isPrivate, CancellationToken cancellationToken)
	{
		var raw = msg.AsString();

		if (raw.EqualsIgnoreCase("PONG"))
			return;

		if (!IsJson(msg.Memory.Span))
		{
			await OnProtobufProcess(msg.Memory, isPrivate, cancellationToken);
			return;
		}

		var obj = msg.AsObject() as JObject;
		if (obj is null)
			return;

		if (((string)obj["msg"]).EqualsIgnoreCase("PONG"))
			return;

		var channel = (string)obj["channel"];

		if (channel.IsEmpty())
		{
			var code = obj["code"]?.To<int?>();
			if (code is int c && c != 0)
			{
				var error = new InvalidOperationException($"WebSocket error: code={c}, msg={(string)obj["msg"]}");
				if (Error is { } handler)
					await handler(error, cancellationToken);
				return;
			}

			var msgText = (string)obj["msg"];
			if (!msgText.IsEmpty())
				this.AddDebugLog("WS({0}) message: {1}", isPrivate ? "private" : "public", msgText);

			return;
		}

		if (channel.Contains("public.aggre.bookTicker", StringComparison.OrdinalIgnoreCase))
		{
			var payload = obj["publicbookticker"] as JObject;
			var symbol = WsHelpers.ResolveSymbol(obj, channel);

			if (payload is not null && !symbol.IsEmpty() && TickerReceived is { } handler)
			{
				await handler(new Ticker
				{
					Symbol = symbol,
					BidPrice = WsHelpers.ToDouble(payload["bidprice"]),
					BidQty = WsHelpers.ToDouble(payload["bidquantity"]),
					AskPrice = WsHelpers.ToDouble(payload["askprice"]),
					AskQty = WsHelpers.ToDouble(payload["askquantity"]),
				}, cancellationToken);
			}

			return;
		}

		if (channel.Contains("public.aggre.deals", StringComparison.OrdinalIgnoreCase))
		{
			var payload = obj["publicdeals"] as JObject;
			var deals = payload?["dealsList"] as JArray;
			var symbol = WsHelpers.ResolveSymbol(obj, channel);

			if (deals is not null && !symbol.IsEmpty() && TradeReceived is { } handler)
			{
				foreach (var tradeObj in deals.OfType<JObject>())
				{
					await handler(new TradeStream
					{
						Symbol = symbol,
						Id = Interlocked.Increment(ref _tradeIdSeed),
						Price = WsHelpers.ToDouble(tradeObj["price"]),
						Quantity = WsHelpers.ToDouble(tradeObj["quantity"]),
						Time = WsHelpers.ToDateTime(tradeObj["time"]),
						IsBuyerMaker = tradeObj["tradetype"]?.To<int?>() == 2,
					}, cancellationToken);
				}
			}

			return;
		}

		if (channel.Contains("public.kline", StringComparison.OrdinalIgnoreCase))
		{
			var payload = obj["publicspotkline"] as JObject;
			var symbol = WsHelpers.ResolveSymbol(obj, channel);

			if (payload is not null && !symbol.IsEmpty() && CandleReceived is { } handler)
			{
				var interval = WsHelpers.FromSpotWsInterval((string)payload["interval"]);
				var openTime = WsHelpers.ToDateTime(payload["windowstart"]);
				var closeTime = WsHelpers.ToDateTime(payload["windowend"]);

				await handler(new CandleStream
				{
					Symbol = symbol,
					Kline = new CandleData
					{
						Symbol = symbol,
						Interval = interval,
						OpenTime = openTime,
						CloseTime = closeTime,
						Open = WsHelpers.ToDouble(payload["openingprice"]) ?? 0,
						Close = WsHelpers.ToDouble(payload["closingprice"]) ?? 0,
						High = WsHelpers.ToDouble(payload["highestprice"]) ?? 0,
						Low = WsHelpers.ToDouble(payload["lowestprice"]) ?? 0,
						Volume = WsHelpers.ToDouble(payload["volume"]) ?? 0,
						QuoteVolume = WsHelpers.ToDouble(payload["amount"]) ?? 0,
						IsClosed = DateTime.UtcNow >= closeTime,
					}
				}, cancellationToken);
			}

			return;
		}

		if (channel.Contains("public.aggre.depth", StringComparison.OrdinalIgnoreCase))
		{
			var payload = obj["publicincreasedepths"] as JObject;
			var symbol = WsHelpers.ResolveSymbol(obj, channel);

			if (payload is not null && !symbol.IsEmpty() && OrderBookReceived is { } handler)
			{
				var firstUpdateId = payload["fromVersion"]?.To<long?>() ?? 0;
				var finalUpdateId = payload["toVersion"]?.To<long?>() ?? 0;

				await handler(new OrderBookUpdate
				{
					Symbol = symbol,
					FirstUpdateId = firstUpdateId,
					FinalUpdateId = finalUpdateId,
					Bids = WsHelpers.ToOrderBookEntries(payload["bidsList"]),
					Asks = WsHelpers.ToOrderBookEntries(payload["asksList"]),
				}, cancellationToken);
			}

			return;
		}

		if (channel.Contains("private.account", StringComparison.OrdinalIgnoreCase))
		{
			var payload = obj["privateAccount"] as JObject;

			if (payload is not null && AccountReceived is { } handler)
			{
				await handler(new PrivateAccountUpdate
				{
					Asset = (string)payload["vcoinName"],
					Balance = WsHelpers.ToDouble(payload["balanceAmount"]),
					Frozen = WsHelpers.ToDouble(payload["frozenAmount"]),
					ChangeType = (string)payload["type"],
					Time = WsHelpers.ToDateTime(payload["time"]),
				}, cancellationToken);
			}

			return;
		}

		if (channel.Contains("private.deals", StringComparison.OrdinalIgnoreCase))
		{
			var payload = obj["privateDeals"] as JObject;
			var symbol = WsHelpers.ResolveSymbol(obj, channel);

			if (payload is not null && !symbol.IsEmpty() && UserTradeReceived is { } handler)
			{
				await handler(new PrivateDealUpdate
				{
					Symbol = symbol,
					Price = WsHelpers.ToDouble(payload["price"]),
					Quantity = WsHelpers.ToDouble(payload["quantity"]),
					TradeType = payload["tradeType"]?.To<int?>(),
					TradeId = (string)payload["tradeId"],
					OrderId = (string)payload["orderId"],
					FeeAmount = WsHelpers.ToDouble(payload["feeAmount"]),
					FeeCurrency = (string)payload["feeCurrency"],
					Time = WsHelpers.ToDateTime(payload["time"]),
				}, cancellationToken);
			}

			return;
		}

		if (channel.Contains("private.orders", StringComparison.OrdinalIgnoreCase))
		{
			var payload = obj["privateOrders"] as JObject;
			var symbol = WsHelpers.ResolveSymbol(obj, channel);

			if (payload is not null && !symbol.IsEmpty() && OrderReceived is { } handler)
			{
				await handler(new PrivateOrderUpdate
				{
					Symbol = symbol,
					Id = (string)payload["id"],
					Price = WsHelpers.ToDouble(payload["price"]),
					Quantity = WsHelpers.ToDouble(payload["quantity"]),
					AvgPrice = WsHelpers.ToDouble(payload["avgPrice"]),
					OrderType = payload["orderType"]?.To<int?>(),
					TradeType = payload["tradeType"]?.To<int?>(),
					RemainQuantity = WsHelpers.ToDouble(payload["remainQuantity"]),
					LastDealQuantity = WsHelpers.ToDouble(payload["lastDealQuantity"]),
					CumulativeQuantity = WsHelpers.ToDouble(payload["cumulativeQuantity"]),
					CumulativeAmount = WsHelpers.ToDouble(payload["cumulativeAmount"]),
					Status = payload["status"]?.To<int?>(),
					CreateTime = WsHelpers.ToDateTime(payload["createTime"]),
				}, cancellationToken);
			}
		}
	}

	private async ValueTask OnProtobufProcess(ReadOnlyMemory<byte> data, bool isPrivate,
		CancellationToken cancellationToken)
	{
		PushDataV3ApiWrapper message;

		try
		{
			message = PushDataV3ApiWrapper.Parser.ParseFrom(data.ToArray());
		}
		catch (InvalidProtocolBufferException error)
		{
			if (Error is { } errorHandler)
				await errorHandler(new InvalidDataException(
					"MEXC returned malformed Protobuf market data.", error),
					cancellationToken);
			return;
		}

		var symbol = ResolveSymbol(message);

		if (message.PublicAggreBookTicker is { } ticker)
		{
			if (!symbol.IsEmpty() && TickerReceived is { } handler)
				await handler(new Ticker
				{
					Symbol = symbol,
					BidPrice = WsHelpers.ToDouble(ticker.BidPrice),
					BidQty = WsHelpers.ToDouble(ticker.BidQuantity),
					AskPrice = WsHelpers.ToDouble(ticker.AskPrice),
					AskQty = WsHelpers.ToDouble(ticker.AskQuantity),
				}, cancellationToken);
			return;
		}

		if (message.PublicAggreDeals is { } deals)
		{
			if (!symbol.IsEmpty() && TradeReceived is { } handler)
				foreach (var trade in deals.Deals)
					await handler(new TradeStream
					{
						Symbol = symbol,
						Id = ParseId(trade.TradeId),
						Price = WsHelpers.ToDouble(trade.Price),
						Quantity = WsHelpers.ToDouble(trade.Quantity),
						Time = WsHelpers.ToDateTime(trade.Time),
						IsBuyerMaker = trade.TradeType == 2,
					}, cancellationToken);
			return;
		}

		if (message.PublicAggreDepths is { } depth)
		{
			if (!symbol.IsEmpty() && OrderBookReceived is { } handler)
				await handler(new OrderBookUpdate
				{
					Symbol = symbol,
					FirstUpdateId = ParseVersion(depth.FromVersion),
					FinalUpdateId = ParseVersion(depth.ToVersion),
					Bids = [.. depth.Bids.Select(static level => new OrderBookEntry
					{
						Price = WsHelpers.ToDouble(level.Price),
						Quantity = WsHelpers.ToDouble(level.Quantity),
					})],
					Asks = [.. depth.Asks.Select(static level => new OrderBookEntry
					{
						Price = WsHelpers.ToDouble(level.Price),
						Quantity = WsHelpers.ToDouble(level.Quantity),
					})],
				}, cancellationToken);
			return;
		}

		if (message.PublicSpotKline is { } kline)
		{
			if (!symbol.IsEmpty() && CandleReceived is { } handler)
			{
				var closeTime = WsHelpers.ToDateTime(kline.WindowEnd);
				await handler(new CandleStream
				{
					Symbol = symbol,
					Kline = new CandleData
					{
						Symbol = symbol,
						Interval = WsHelpers.FromSpotWsInterval(kline.Interval),
						OpenTime = WsHelpers.ToDateTime(kline.WindowStart),
						CloseTime = closeTime,
						Open = WsHelpers.ToDouble(kline.OpeningPrice) ?? 0,
						Close = WsHelpers.ToDouble(kline.ClosingPrice) ?? 0,
						High = WsHelpers.ToDouble(kline.HighestPrice) ?? 0,
						Low = WsHelpers.ToDouble(kline.LowestPrice) ?? 0,
						Volume = WsHelpers.ToDouble(kline.Volume) ?? 0,
						QuoteVolume = WsHelpers.ToDouble(kline.Amount) ?? 0,
						IsClosed = DateTime.UtcNow >= closeTime,
					},
				}, cancellationToken);
			}
			return;
		}

		if (message.PrivateAccount is { } account)
		{
			if (isPrivate && AccountReceived is { } handler)
				await handler(new PrivateAccountUpdate
				{
					Asset = account.VcoinName,
					Balance = WsHelpers.ToDouble(account.BalanceAmount),
					Frozen = WsHelpers.ToDouble(account.FrozenAmount),
					ChangeType = account.Type,
					Time = WsHelpers.ToDateTime(account.Time),
				}, cancellationToken);
			return;
		}

		if (message.PrivateDeals is { } deal)
		{
			if (isPrivate && !symbol.IsEmpty() && UserTradeReceived is { } handler)
				await handler(new PrivateDealUpdate
				{
					Symbol = symbol,
					TradeId = deal.TradeId,
					OrderId = deal.OrderId,
					Price = WsHelpers.ToDouble(deal.Price),
					Quantity = WsHelpers.ToDouble(deal.Quantity),
					TradeType = deal.TradeType,
					FeeAmount = WsHelpers.ToDouble(deal.FeeAmount),
					FeeCurrency = deal.FeeCurrency,
					Time = WsHelpers.ToDateTime(deal.Time),
				}, cancellationToken);
			return;
		}

		if (message.PrivateOrders is { } order)
		{
			if (isPrivate && !symbol.IsEmpty() && OrderReceived is { } handler)
				await handler(new PrivateOrderUpdate
				{
					Symbol = symbol,
					Id = order.Id,
					Price = WsHelpers.ToDouble(order.Price),
					Quantity = WsHelpers.ToDouble(order.Quantity),
					AvgPrice = WsHelpers.ToDouble(order.AvgPrice),
					OrderType = order.OrderType,
					TradeType = order.TradeType,
					RemainQuantity = WsHelpers.ToDouble(order.RemainQuantity),
					LastDealQuantity = WsHelpers.ToDouble(order.LastDealQuantity),
					CumulativeQuantity = WsHelpers.ToDouble(order.CumulativeQuantity),
					CumulativeAmount = WsHelpers.ToDouble(order.CumulativeAmount),
					Status = order.Status,
					CreateTime = WsHelpers.ToDateTime(order.CreateTime),
				}, cancellationToken);
			return;
		}

		this.AddDebugLog("Ignore MEXC Protobuf channel {0} ({1}).",
			message.Channel, message.BodyCase);
	}

	private long ParseId(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var result)
			? result
			: Interlocked.Increment(ref _tradeIdSeed);

	private static long ParseVersion(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var result)
			? result
			: 0;

	private static string ResolveSymbol(PushDataV3ApiWrapper message)
	{
		if (!message.Symbol.IsEmpty())
			return message.Symbol.ToUpperInvariant();

		var parts = message.Channel.SplitByAt(false);
		if (parts.Length >= 2 && parts[^1].All(char.IsLetterOrDigit))
			return parts[^1].ToUpperInvariant();

		if (parts.Length >= 3 && parts[^2].All(char.IsLetterOrDigit))
			return parts[^2].ToUpperInvariant();

		return null;
	}

	private static bool IsJson(ReadOnlySpan<byte> data)
	{
		foreach (var value in data)
		{
			if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
				continue;

			return value is (byte)'{' or (byte)'[';
		}

		return true;
	}

	public ValueTask SubscribeTicker(long transId, string symbol, CancellationToken cancellationToken)
		=> Subscribe(transId, $"spot@public.aggre.bookTicker.v3.api.pb@100ms@{symbol.ToUpperInvariant()}", _publicClient, cancellationToken);

	public ValueTask UnsubscribeTicker(long originTransId, string symbol, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, $"spot@public.aggre.bookTicker.v3.api.pb@100ms@{symbol.ToUpperInvariant()}", _publicClient, cancellationToken);

	public ValueTask SubscribeOrderBook(long transId, string symbol, int levels, CancellationToken cancellationToken)
		=> Subscribe(transId, $"spot@public.aggre.depth.v3.api.pb@100ms@{symbol.ToUpperInvariant()}", _publicClient, cancellationToken);

	public ValueTask UnsubscribeOrderBook(long originTransId, string symbol, int levels, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, $"spot@public.aggre.depth.v3.api.pb@100ms@{symbol.ToUpperInvariant()}", _publicClient, cancellationToken);

	public ValueTask SubscribeTrades(long transId, string symbol, CancellationToken cancellationToken)
		=> Subscribe(transId, $"spot@public.aggre.deals.v3.api.pb@100ms@{symbol.ToUpperInvariant()}", _publicClient, cancellationToken);

	public ValueTask UnsubscribeTrades(long originTransId, string symbol, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, $"spot@public.aggre.deals.v3.api.pb@100ms@{symbol.ToUpperInvariant()}", _publicClient, cancellationToken);

	public ValueTask SubscribeCandles(long transId, string symbol, string interval, CancellationToken cancellationToken)
		=> Subscribe(transId, $"spot@public.kline.v3.api.pb@{symbol.ToUpperInvariant()}@{WsHelpers.ToSpotWsInterval(interval)}", _publicClient, cancellationToken);

	public ValueTask UnsubscribeCandles(long originTransId, string symbol, string interval, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, $"spot@public.kline.v3.api.pb@{symbol.ToUpperInvariant()}@{WsHelpers.ToSpotWsInterval(interval)}", _publicClient, cancellationToken);

	public ValueTask SubscribePrivateAccount(long transId, CancellationToken cancellationToken)
		=> Subscribe(transId, "spot@private.account.v3.api.pb", _privateClient, cancellationToken);

	public ValueTask UnsubscribePrivateAccount(long originTransId, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, "spot@private.account.v3.api.pb", _privateClient, cancellationToken);

	public ValueTask SubscribePrivateDeals(long transId, CancellationToken cancellationToken)
		=> Subscribe(transId, "spot@private.deals.v3.api.pb", _privateClient, cancellationToken);

	public ValueTask UnsubscribePrivateDeals(long originTransId, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, "spot@private.deals.v3.api.pb", _privateClient, cancellationToken);

	public ValueTask SubscribePrivateOrders(long transId, CancellationToken cancellationToken)
		=> Subscribe(transId, "spot@private.orders.v3.api.pb", _privateClient, cancellationToken);

	public ValueTask UnsubscribePrivateOrders(long originTransId, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, "spot@private.orders.v3.api.pb", _privateClient, cancellationToken);

	private ValueTask Subscribe(long transId, string channel, WebSocketClient client, CancellationToken cancellationToken)
	{
		if (client is null)
			throw new InvalidOperationException("Private WS is not connected.");

		var id = ++_subscriptionId;
		_subscriptions[id] = transId;

		return Send(client, new
		{
			method = "SUBSCRIPTION",
			@params = new[] { channel },
			id
		}, cancellationToken);
	}

	private ValueTask Unsubscribe(long originTransId, string channel, WebSocketClient client, CancellationToken cancellationToken)
	{
		if (client is null)
			return default;

		var id = ++_subscriptionId;

		return Send(client, new
		{
			method = "UNSUBSCRIPTION",
			@params = new[] { channel },
			id
		}, cancellationToken);
	}

	private static ValueTask Send(WebSocketClient client, object message, CancellationToken cancellationToken)
		=> client.SendAsync(message, cancellationToken);

	private void StartPingLoop()
	{
		if (_pingCts != null)
			return;

		_pingCts = new CancellationTokenSource();
		_pingTask = PingLoopAsync(_pingCts.Token);
	}

	private void StopPingLoop()
	{
		var cts = _pingCts;
		_pingCts = null;
		_pingTask = null;

		if (cts is null)
			return;

		try
		{
			cts.Cancel();
		}
		finally
		{
			cts.Dispose();
		}
	}

	private async Task PingLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);

				await Send(_publicClient, new { method = "PING" }, cancellationToken);

				if (_privateClient is { } privateClient)
					await Send(privateClient, new { method = "PING" }, cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			this.AddErrorLog(ex);
		}
	}
}
