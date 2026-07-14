namespace StockSharp.Poloniex.Native;

class PusherClient : BaseLogReceiver
{
	private class ErrorSubscription
	{
		[JsonProperty("error")]
		public string Error { get; set; }
	}

	public event Func<int, Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<int, OrderBook, CancellationToken, ValueTask> OrderBookSnapshot;
	public event Func<int, Trade, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<int, Trade, CancellationToken, ValueTask> NewTrade;
	//public event Action<ulong, string, string, uint?> TrollboxMessage;

	public event Func<SocketBalance, CancellationToken, ValueTask> BalanceChanged;
	public event Func<SocketOrderPending, CancellationToken, ValueTask> OrderPending;
	public event Func<SocketOrderLimit, CancellationToken, ValueTask> NewOrder;
	public event Func<SocketOrderUpdate, CancellationToken, ValueTask> OrderChanged;
	public event Func<SocketOrderKill, CancellationToken, ValueTask> OrderKilled;
	public event Func<SocketOwnTrade, CancellationToken, ValueTask> NewOwnTrade;

	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly WebSocketClient _client;
	private readonly Authenticator _authenticator;

	public PusherClient(Authenticator authenticator, WorkingTime workingTime)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

		_client = new(
			"wss://api2.poloniex.com",
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
			OnProcess,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
		};
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Poloniex) + "_" + nameof(PusherClient);

	protected override void DisposeManaged()
	{
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);
		return _client.ConnectAsync(cancellationToken);
	}

	public void Disconnect()
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		_client.Disconnect();
	}

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var obj = msg.AsObject<JContainer>();

		if (obj is not JArray)
		{
			var error = obj.ToObject<ErrorSubscription>();
			if (Error is { } errorHandler)
				await errorHandler(new InvalidOperationException(error.Error), cancellationToken);
			return;
		}

		var arr = (JArray)obj;

		// heartbeat
		if (arr.Count == 2)
			return;

		var channelId = (int)arr[0];

		switch (channelId)
		{
			case (int)Channels.Heartbeat:
			case (int)Channels.Trollbox:
			case (int)Channels.Footer:
				break;

			case (int)Channels.Ticker:
			{
				var data = (JArray)arr[2];

				var id = (int)data[0];
				if (TickerChanged is { } tickerHandler)
					await tickerHandler(id, new Ticker
					{
						TickerId = (int)data[0],
						Last = (double)data[1],
						LowestAsk = (double)data[2],
						HighestBid = (double)data[3],
						PercentChange = (double)data[4],
						BaseVolume = (double)data[5],
						QuoteVolume = (double)data[6],
						IsFrozen = (int)data[7] != 0,
						//24hrHigh = (double)data[8],
						//24hrLow = (double)data[9],
					}, cancellationToken);

				break;
			}

			case (int)Channels.Account:
			{
				foreach (var data in (JArray)arr[2])
				{
					switch ((string)data[0])
					{
						case "p":
							if (OrderPending is { } pendingHandler)
								await pendingHandler(data.DeserializeObject<SocketOrderPending>(), cancellationToken);
							break;
						case "b":
							if (BalanceChanged is { } balanceHandler)
								await balanceHandler(data.DeserializeObject<SocketBalance>(), cancellationToken);
							break;
						case "n":
							if (NewOrder is { } newOrderHandler)
								await newOrderHandler(data.DeserializeObject<SocketOrderLimit>(), cancellationToken);
							break;
						case "o":
							if (OrderChanged is { } orderChangedHandler)
								await orderChangedHandler(data.DeserializeObject<SocketOrderUpdate>(), cancellationToken);
							break;
						case "t":
							if (NewOwnTrade is { } ownTradeHandler)
								await ownTradeHandler(data.DeserializeObject<SocketOwnTrade>(), cancellationToken);
							break;
						case "k":
							if (OrderKilled is { } killedHandler)
								await killedHandler(data.DeserializeObject<SocketOrderKill>(), cancellationToken);
							break;
						default:
							this.AddWarningLog(LocalizedStrings.UnknownEvent, (string)data[0]);
							break;
					}
				}

				break;
			}

			default:
			{
				var tickerId = channelId;

				foreach (var data in (JArray)arr[2])
				{
					switch ((string)data[0])
					{
						case "i":
							//var pair = (JEnumerable<>)data[1][1];
							var arr2 = (JArray)((dynamic)data[1]).orderBook;
							var asks = arr2[0];
							var bids = arr2[1];
							if (OrderBookSnapshot is { } snapshotHandler)
								await snapshotHandler(tickerId, new OrderBook
								{
									Bids = [.. bids.Cast<JProperty>().Select(p => new OrderEntry { PricePerCoin = p.Name.To<double>(), AmountQuote = (double)p.Value })],
									Asks = [.. asks.Cast<JProperty>().Select(p => new OrderEntry { PricePerCoin = p.Name.To<double>(), AmountQuote = (double)p.Value })],
								}, cancellationToken);
							break;

						case "o":
							if (OrderBookChanged is { } bookChangedHandler)
								await bookChangedHandler(tickerId, new Trade
								{
									Type = (int)data[1],
									Rate = (double)data[2],
									Amount = (double)data[3],
								}, cancellationToken);
							break;

						case "t":
							if (NewTrade is { } tradeHandler)
								await tradeHandler(tickerId, new Trade
								{
									Id = (long)data[1],
									Type = (int)data[2],
									Rate = (double)data[3],
									Amount = (double)data[4],
									Date = (double)data[5],
								}, cancellationToken);
							break;

						default:
							this.AddWarningLog(LocalizedStrings.UnknownEvent, (string)data[0]);
							break;
					}
				}

				break;
			}
		}
	}

	private enum Channels
	{
		Account = 1000,
		Trollbox = 1001,
		Ticker = 1002,
		Footer = 1003,
		Volume24H = 1003,
		Heartbeat = 1010,
	}

	public ValueTask SubscribeToTicker(CancellationToken cancellationToken)
	{
		return Subscribe((int)Channels.Ticker, cancellationToken);
	}

	public ValueTask UnSubscribeFromTicker(CancellationToken cancellationToken)
	{
		return UnSubscribe((int)Channels.Ticker, cancellationToken);
	}

	public ValueTask SubscribeTicker(int currencyPairId, CancellationToken cancellationToken)
	{
		return Subscribe(currencyPairId, cancellationToken);
	}

	public ValueTask UnSubscribeTicker(int currencyPairId, CancellationToken cancellationToken)
	{
		return UnSubscribe(currencyPairId, cancellationToken);
	}

	public ValueTask SubscribeAccount(CancellationToken cancellationToken)
	{
		var payload = $"nonce={_authenticator.GetNonce()}";

		return _client.SendAsync(new
		{
			command = "subscribe",
			channel = (int)Channels.Account,
			key = _authenticator.Key.UnSecure(),
			payload,
			sign = _authenticator.Sign(payload)
		}, cancellationToken);
	}

	public ValueTask UnSubscribeAccount(CancellationToken cancellationToken)
	{
		return UnSubscribe((int)Channels.Account, cancellationToken);
	}

	private ValueTask Subscribe(int channelId, CancellationToken cancellationToken)
	{
		return _client.SendAsync(new
		{
			command = "subscribe",
			channel = channelId
		}, cancellationToken);
	}

	private ValueTask UnSubscribe(int channelId, CancellationToken cancellationToken)
	{
		return _client.SendAsync(new
		{
			command = "unsubscribe",
			channel = channelId
		}, cancellationToken);
	}
}