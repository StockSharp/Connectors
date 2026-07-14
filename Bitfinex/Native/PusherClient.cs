namespace StockSharp.Bitfinex.Native;

using System.Dynamic;
using System.Security.Cryptography;

class PusherClient : BaseLogReceiver
{
	private class MarketDataWebSocketClient : WebSocketClient
	{
		public MarketDataWebSocketClient(PusherClient parent, string url)
			: base(url,
				(state, token) =>
				{
					if (parent.StateChanged is { } handler)
						return handler(state, token);
					return default;
				},
				(error, token) =>
				{
					parent.AddErrorLog(error);
					if (parent.Error is { } handler)
						return handler(error, token);
					return default;
				},
				parent.OnProcess,
				(s, a) => parent.AddInfoLog(s, a),
				(s, a) => parent.AddErrorLog(s, a),
				(s, a) => parent.AddVerboseLog(s, a))
		{
		}

		public int Counter { get; set; }
	}

	public event Func<string, Trade, CancellationToken, ValueTask> NewTrade;
	public event Func<string, IEnumerable<Tuple<decimal, int, decimal>>, CancellationToken, ValueTask> OrderBookSnaphot;
	public event Func<string, decimal, int, decimal, CancellationToken, ValueTask> OrderBookIncrement;
	public event Func<string, OrderLog, CancellationToken, ValueTask> NewOrderLog;
	public event Func<string, Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<string, string, Ohlc, CancellationToken, ValueTask> NewCandle;
	public event Func<string, string, decimal, decimal, decimal, int?, decimal?, decimal?, decimal?, decimal?, CancellationToken, ValueTask> NewPosition;
	public event Func<string, string, decimal, decimal, CancellationToken, ValueTask> NewWallet;
	public event Func<string, long, long?, long?, string, long, long, decimal, decimal, string, string, string, Tuple<decimal?, decimal?, decimal?, decimal?>, long?, int?, CancellationToken, ValueTask> OrderChanged;
	public event Func<long, string, long, long, decimal, decimal, string, decimal?, int, decimal?, string, CancellationToken, ValueTask> NewOwnTrade;
	public event Func<bool, long, long?, long?, string, CancellationToken, ValueTask> OrderError;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;
	public event Action<string> TradesSubscribed;
	public event Action<string> OrderBookSubscribed;
	public event Action<string> OrderLogSubscribed;
	public event Action<string> TickerSubscribed;
	public event Action<string> CandlesSubscribed;
	public event Action<string> TradesUnSubscribed;
	public event Action<string> OrderBookUnSubscribed;
	public event Action<string> OrderLogUnSubscribed;
	public event Action<string> TickerUnSubscribed;
	public event Action<string> CandlesUnSubscribed;

	private readonly WebSocketClient _client;

	private readonly SecureString _key;
	private readonly HashAlgorithm _hasher;
	private readonly int _attemptsCount;
	private readonly WorkingTime _workingTime;
	private readonly UTCIncrementalIdGenerator _nonceGen;

	// https://www.bitfinex.com/posts/267
	private const int _maxSubscriptions = 45;
	private MarketDataWebSocketClient _currentMarketDataClient;
	private readonly Dictionary<string, MarketDataWebSocketClient> _marketDataClients = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedPairSet<(Channels, string, string), int> _channelIds = [];
	private readonly SynchronizedDictionary<int, MarketDataWebSocketClient> _clientsByChannelId = [];

	public PusherClient(SecureString key, SecureString secret, int attemptsCount, WorkingTime workingTime)
	{
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_key = key;
		_hasher = secret.IsEmpty() ? null : new HMACSHA384(secret.UnSecure().ASCII());
		_attemptsCount = attemptsCount;

		_nonceGen = new UTCIncrementalIdGenerator();

		_client = new(
			"wss://api.bitfinex.com/ws/2",
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
			ReconnectAttempts = attemptsCount,
			WorkingTime = workingTime,
		};
	}

	protected override void DisposeManaged()
	{
		_client.Dispose();
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Bitfinex) + "_" + nameof(PusherClient);

	public ValueTask Connect(CancellationToken cancellationToken)
	{
		ClearMarketDataClients();

		this.AddInfoLog(LocalizedStrings.Connecting);
		return _client.ConnectAsync(cancellationToken);
	}

	public void Disconnect()
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		_client.Disconnect();

		_marketDataClients.Values.Distinct().ForEach(c => c.Disconnect());

		ClearMarketDataClients();
	}

	private void ClearMarketDataClients()
	{
		_channelIds.Clear();
		_clientsByChannelId.Clear();
		_marketDataClients.Clear();
		_currentMarketDataClient = null;
	}

	private async ValueTask OnProcess(WebSocketClient client, WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var d = msg.AsObject();

		var needReconnect = false;

		switch (d)
		{
			case JObject _:
			{
				//var obj = o.ToObject<ChannelData>();

				switch ((string)d.@event)
				{
					case "info":
					{
						if (!((string)d.code).IsEmpty())
						{
							needReconnect = true;
							if (Error is { } handler)
								await handler(new InvalidOperationException((string)d.message), cancellationToken);
						}
						else if (client is not MarketDataWebSocketClient)
						{
						}

						break;
					}

					case "subscribed":
					{
						var chan = (string)d.channel;
						var pair = (string)d.pair;

						string arg = null;

						Channels channel;

						if (chan.EqualsIgnoreCase(ChannelNames.OrderBook))
						{
							var prec = (string)d.prec;

							if (prec == null || prec.EqualsIgnoreCase("R0"))
							{
								channel = Channels.OrderLog;
								OrderLogSubscribed?.Invoke(pair);
							}
							else
							{
								channel = Channels.OrderBook;
								OrderBookSubscribed?.Invoke(pair);
							}
						}
						else if (chan.EqualsIgnoreCase(ChannelNames.Trades))
						{
							channel = Channels.Trades;
							TradesSubscribed?.Invoke(pair);
						}
						else if (chan.EqualsIgnoreCase(ChannelNames.Ticker))
						{
							channel = Channels.Ticker;
							TickerSubscribed?.Invoke(pair);
						}
						else if (chan.EqualsIgnoreCase(ChannelNames.Candles))
						{
							channel = Channels.Candles;
							pair = (string)d.key;
							var parts = pair.SplitByColon(false);
							arg = parts[1];
							pair = parts[2];
							CandlesSubscribed?.Invoke(pair);
						}
						else
						{
							this.AddErrorLog(LocalizedStrings.UnknownEvent, chan);
							break;
						}

						var chanId = (int)d.chanId;
						_channelIds[(channel, pair.ToLowerInvariant(), arg)] = chanId;
						_clientsByChannelId[chanId] = (MarketDataWebSocketClient)client;

						break;
					}

					case "unsubscribed":
					{
						var chanId = (int)d.chanId;

						if (!_channelIds.TryGetKey(chanId, out var chanInfo))
						{
							this.AddErrorLog(LocalizedStrings.UnknownEvent.Put(chanId));
							break;
						}

						var symbol = chanInfo.Item2;

						switch (chanInfo.Item1)
						{
							case Channels.Trades:
								TradesUnSubscribed?.Invoke(symbol);
								break;
							case Channels.OrderBook:
								OrderBookUnSubscribed?.Invoke(symbol);
								break;
							case Channels.OrderLog:
								OrderLogUnSubscribed?.Invoke(symbol);
								break;
							case Channels.Ticker:
								TickerUnSubscribed?.Invoke(symbol);
								break;
							case Channels.Candles:
								CandlesUnSubscribed?.Invoke(symbol);
								break;
							default:
								throw new ArgumentOutOfRangeException(LocalizedStrings.UnknownEvent.Put(chanInfo.Item1));
						}

						_channelIds.RemoveByValue(chanId);
						_clientsByChannelId.Remove(chanId);

						break;
					}

					case "pong":
						break;

					case "error":
						this.AddErrorLog((string)d.message);
						break;

					case "auth":
						if (!((string)d.status).EqualsIgnoreCase("OK"))
							this.AddErrorLog("Not authenticated {0}", (string)d.code);

						break;

					case "unauth":
						this.AddErrorLog("Not authenticated {0}", (int)d.chanId);
						break;

					default:
						this.AddErrorLog(LocalizedStrings.UnknownEvent, (string)d.@event);
						break;
				}

				break;
			}
			case JArray a:
			{
				var chanId = (int)a[0];
				var typeObj = a[1];

				if (chanId == 0)
				{
					var type = (string)typeObj;

					// Heartbeating
					if (type == "hb")
						break;

					var value = (JArray)a[2];

					if (value.Count == 0)
						break;

					switch (type)
					{
						case "ps": //position snapshot
						{
							foreach (var posItem in value)
							{
								if (NewPosition is { } handler)
									await handler((string)posItem[0], (string)posItem[1], (decimal)(double)posItem[2], (decimal)(double)posItem[3], (decimal)(double)posItem[4], (int?)posItem[5], (decimal?)(double?)posItem[6], (decimal?)(double?)posItem[7], (decimal?)(double?)posItem[8], (decimal?)(double?)posItem[9], cancellationToken);
							}

							break;
						}
						case "pn": //new position
						case "pu": //position update
						case "pc": //position close
						{
							if (NewPosition is { } handler)
								await handler((string)value[0], (string)value[1], (decimal)(double)value[2], (decimal)(double)value[3], (decimal)(double)value[4], (int?)value[5], (decimal?)(double?)value[6], (decimal?)(double?)value[7], (decimal?)(double?)value[8], (decimal?)(double?)value[9], cancellationToken);
							break;
						}
						case "ws": //wallet snapshot
						{
							foreach (var walItem in value)
							{
								if (NewWallet is { } handler)
									await handler((string)walItem[0], (string)walItem[1], (decimal)(double)walItem[2], (decimal)(double)walItem[3], cancellationToken);
							}

							break;
						}
						case "wu": //wallet update
						{
							if (NewWallet is { } handler)
								await handler((string)value[0], (string)value[1], (decimal)(double)value[2], (decimal)(double)value[3], cancellationToken);
							break;
						}
						case "os": //order snapshot
						{
							foreach (var ordItem in value)
							{
								if (OrderChanged is { } handler)
									await handler(type, (long)ordItem[0], (long?)ordItem[1], (long?)ordItem[2], (string)ordItem[3], (long)ordItem[4], (long)ordItem[5], (decimal)(double)ordItem[6],
										(decimal)(double)ordItem[7], (string)ordItem[8], (string)ordItem[9], (string)ordItem[13], Tuple.Create((decimal?)(double?)ordItem[16], (decimal?)(double?)ordItem[17], (decimal?)(double?)ordItem[18], (decimal?)(double?)ordItem[19]), (long?)ordItem[25], (int)ordItem[12], cancellationToken);
							}

							break;
						}
						case "on": //new order
						case "ou": //order update
						case "oc": //order cancel
						{
							if (OrderChanged is { } handler)
								await handler(type, (long)value[0], (long?)value[1], (long?)value[2], (string)value[3], (long)value[4], (long)value[5], (decimal)(double)value[6],
									(decimal)(double)value[7], (string)value[8], (string)value[9], (string)value[13], Tuple.Create((decimal?)(double?)value[16], (decimal?)(double?)value[17], (decimal?)(double?)value[18], (decimal?)(double?)value[19]), (long?)value[25], (int)value[12], cancellationToken);

							break;
						}
						case "ts": //trade snapshot
						{
							foreach (var trdItem in value)
							{
								if (NewOwnTrade is { } handler)
									await handler((long)trdItem[0], (string)trdItem[1], (long)trdItem[2], (long)trdItem[3], (decimal)(double)trdItem[4], (decimal)(double)trdItem[5], (string)trdItem[6], (decimal?)(double?)trdItem[7], (int)trdItem[8], (decimal?)(double?)trdItem[9], (string)trdItem[10], cancellationToken);
							}

							break;
						}
						case "te": //trade executed
						{
							if (NewOwnTrade is { } handler)
								await handler((long)value[0], (string)value[1], (long)value[2], (long)value[3], (decimal)(double)value[4], (decimal)(double)value[5], (string)value[6], (decimal?)(double?)value[7], (int)value[8], null, null, cancellationToken);
							break;
						}
						case "tu": //trade execution update
						{
							if (NewOwnTrade is { } handler)
								await handler((long)value[0], (string)value[1], (long)value[2], (long)value[3], (decimal)(double)value[4], (decimal)(double)value[5], (string)value[6], (decimal?)(double?)value[7], (int)value[8], (decimal?)(double?)value[9], (string)value[10], cancellationToken);
							break;
						}
						case "n":
						{
							switch ((string)value[1])
							{
								case "on-req":
									if ((string)value[6] == "ERROR")
									{
										if (OrderError is { } handler)
											await handler(true, (long)value[0], (long?)value[4][0], (long?)value[4][2], (string)value[7], cancellationToken);
									}

									break;

								case "oc-req":
									if ((string)value[6] == "ERROR")
									{
										if (OrderError is { } handler)
											await handler(false, (long)value[0], (long?)value[4][0], (long?)value[4][2], (string)value[7], cancellationToken);
									}

									break;

								case "ou-req":
									if ((string)value[6] == "ERROR")
									{
										if (OrderError is { } handler)
											await handler(false, (long)value[0], (long?)value[4][0], (long?)value[4][2], (string)value[7], cancellationToken);
									}

									break;

								default:
									this.AddErrorLog(LocalizedStrings.UnknownEvent, type);
									break;
							}

							break;
						}
						case "bu":
						{
							break;
						}
						default:
							this.AddErrorLog(LocalizedStrings.UnknownEvent, type);
							break;
					}
				}
				else
				{
					// Heartbeating
					if (typeObj.Type == JTokenType.String && (string)typeObj == "hb")
						break;

					if (!_channelIds.TryGetKey(chanId, out var chanInfo))
					{
						this.AddErrorLog(LocalizedStrings.UnknownEvent.Put(chanId));
						break;
					}

					switch (chanInfo.Item1)
					{
						case Channels.Trades:
						{
							var value = typeObj;

							if (value is JArray arr)
							{
								// snapshot

								foreach (var item in arr)
								{
									if (NewTrade is { } handler)
										await handler(chanInfo.Item2, item.DeserializeObject<Trade>(), cancellationToken);
								}
							}
							else
							{
								var type = (string)value;

								switch (type)
								{
									case "te": //trade executed
										if (NewTrade is { } handler)
											await handler(chanInfo.Item2, a[2].DeserializeObject<Trade>(), cancellationToken);
										break;

									case "tu": //trade execution update
										if (NewTrade is { } tuHandler)
											await tuHandler(chanInfo.Item2, a[2].DeserializeObject<Trade>(), cancellationToken);
										break;

									default:
										this.AddErrorLog(LocalizedStrings.UnknownEvent, type);
										break;
								}
							}

							break;
						}
						case Channels.OrderBook:
						{
							var value = typeObj;

							if (value is JArray arr && arr.Count > 0 && arr[0].Type == JTokenType.Array)
							{
								// snapshot

								var changes = new List<Tuple<decimal, int, decimal>>();

								foreach (var item in arr)
								{
									changes.Add(Tuple.Create((decimal)(double)item[0], (int)item[1], (decimal)(double)item[2]));
								}

								if (OrderBookSnaphot is { } handler)
									await handler(chanInfo.Item2, changes, cancellationToken);
							}
							else
							{
								if (OrderBookIncrement is { } handler)
									await handler(chanInfo.Item2, (decimal)(double)value[0], (int)value[1], (decimal)(double)value[2], cancellationToken);
							}

							break;
						}
						case Channels.OrderLog:
						{
							var value = typeObj;

							if (value is JArray arr && arr.Count > 0 && arr[0].Type == JTokenType.Array)
							{
								// snapshot

								foreach (var item in arr)
								{
									if (NewOrderLog is { } handler)
										await handler(chanInfo.Item2, item.DeserializeObject<OrderLog>(), cancellationToken);
								}
							}
							else
							{
								if (NewOrderLog is { } handler)
									await handler(chanInfo.Item2, value.DeserializeObject<OrderLog>(), cancellationToken);
							}

							break;
						}
						case Channels.Ticker:
							if (TickerChanged is { } tickerHandler)
								await tickerHandler(chanInfo.Item2, a[1].DeserializeObject<Ticker>(), cancellationToken);
							break;
						case Channels.Candles:
						{
							var value = typeObj;

							if (value is JArray arr && arr.Count > 0 && arr[0].Type == JTokenType.Array)
							{
								// snapshot

								foreach (var item in arr)
								{
									if (NewCandle is { } handler)
										await handler(chanInfo.Item2, chanInfo.Item3, item.DeserializeObject<Ohlc>(), cancellationToken);
								}
							}
							else
							{
								if (NewCandle is { } handler)
									await handler(chanInfo.Item2, chanInfo.Item3, value.DeserializeObject<Ohlc>(), cancellationToken);
							}

							break;
						}
						default:
							throw new ArgumentOutOfRangeException(LocalizedStrings.UnknownEvent.Put(chanInfo.Item1));
					}
				}

				break;
			}
		}

		if (needReconnect)
		{
			if (client is MarketDataWebSocketClient)
				this.AddInfoLog(LocalizedStrings.Disconnecting + "_MarketData");
			else
				this.AddInfoLog(LocalizedStrings.Disconnecting);

			if (StateChanged is { } stateHandler)
				await stateHandler(ConnectionStates.Failed, cancellationToken);
		}
	}

	public void SubscribeAccount(bool cancelOnDisconnect)
	{
		var authNonce = _nonceGen.GetNextId();
		var authPayload = "AUTH" + authNonce;

		var signature = _hasher
			.ComputeHash(authPayload.UTF8())
			.Digest()
			.ToLowerInvariant();

		dynamic body = new ExpandoObject();

		body.apiKey = _key.UnSecure();
		body.authSig = signature;
		body.authNonce = authNonce;
		body.authPayload = authPayload;
		body.@event = "auth";

		if (cancelOnDisconnect)
			body.dms = 4;

		_client.Send(body);
	}

	public static class ChannelNames
	{
		public const string Trades = "trades";
		public const string OrderBook = "book";
		//public const string OrderLog = "book";
		public const string Ticker = "ticker";
		public const string Candles = "candles";
	}

	private enum Channels
	{
		Trades,
		OrderBook,
		OrderLog,
		Ticker,
		Candles,
	}

	public ValueTask SubscribeTicker(string currency, long transId, CancellationToken cancellationToken)
	{
		return Subscribe(ChannelNames.Ticker, currency, transId, cancellationToken);
	}

	public ValueTask UnSubscribeTicker(string currency, long originTransId, CancellationToken cancellationToken)
	{
		return UnSubscribe(Channels.Ticker, currency, default, -originTransId, cancellationToken);
	}

	public ValueTask SubscribeTrades(string currency, long transId, CancellationToken cancellationToken)
	{
		return Subscribe(ChannelNames.Trades, currency, transId, cancellationToken);
	}

	public ValueTask UnSubscribeTrades(string currency, long originTransId, CancellationToken cancellationToken)
	{
		return UnSubscribe(Channels.Trades, currency, default, -originTransId, cancellationToken);
	}

	public async ValueTask SubscribeCandles(string currency, string timeFrame, long transId, CancellationToken cancellationToken)
	{
		await (await CreateAndSubscribe(cancellationToken, ChannelNames.Candles, currency, timeFrame)).SendAsync(new
		{
			@event = "subscribe",
			channel = ChannelNames.Candles,
			key = $"trade:{timeFrame}:{currency}",
		}, cancellationToken, transId);
	}

	public ValueTask UnSubscribeCandles(string currency, string timeFrame, long originTransId, CancellationToken cancellationToken)
	{
		return UnSubscribe(Channels.Candles, currency, timeFrame, -originTransId, cancellationToken);
	}

	public async ValueTask SubscribeOrderBook(string currency, int? maxDepth, long transId, CancellationToken cancellationToken)
	{
		if (currency.IsEmpty())
			throw new ArgumentNullException(nameof(currency));

		var request = (dynamic)new ExpandoObject();

		request.@event = "subscribe";
		request.channel = ChannelNames.OrderBook;
		request.pair = currency;

		if (maxDepth != null)
			request.length = maxDepth.Value;

		await (await CreateAndSubscribe(cancellationToken, ChannelNames.OrderBook, currency)).SendAsync(request, cancellationToken, transId);
	}

	public ValueTask UnSubscribeOrderBook(string currency, long originTransId, CancellationToken cancellationToken)
	{
		return UnSubscribe(Channels.OrderBook, currency, default, -originTransId, cancellationToken);
	}

	public async ValueTask SubscribeOrderLog(string currency, int? maxDepth, long transId, CancellationToken cancellationToken)
	{
		if (currency.IsEmpty())
			throw new ArgumentNullException(nameof(currency));

		var request = (dynamic)new ExpandoObject();

		request.@event = "subscribe";
		request.channel = ChannelNames.OrderBook;
		request.prec = "R0";
		request.pair = currency;

		if (maxDepth != null)
			request.length = maxDepth.Value;

		await (await CreateAndSubscribe(cancellationToken, ChannelNames.OrderBook, currency, "R0")).SendAsync(request, cancellationToken, transId);
	}

	public ValueTask UnSubscribeOrderLog(string currency, long originTransId, CancellationToken cancellationToken)
	{
		return UnSubscribe(Channels.OrderLog, currency, default, -originTransId, cancellationToken);
	}

	private async ValueTask Subscribe(string channel, string pair, long subId, CancellationToken cancellationToken)
	{
		if (channel.IsEmpty())
			throw new ArgumentNullException(nameof(channel));

		if (pair.IsEmpty())
			throw new ArgumentNullException(nameof(pair));

		await (await CreateAndSubscribe(cancellationToken, channel, pair)).SendAsync(new
		{
			@event = "subscribe",
			channel,
			pair,
		}, cancellationToken, subId);
	}

	private async ValueTask UnSubscribe(Channels channel, string pair, string arg, long subId, CancellationToken cancellationToken)
	{
		if (pair.IsEmpty())
			throw new ArgumentNullException(nameof(pair));

		if (!_channelIds.TryGetValue((channel, pair[1..].ToLowerInvariant(), arg), out var chanId) || !_clientsByChannelId.TryGetValue(chanId, out var client))
			return;

		await client.SendAsync(new
		{
			@event = "unsubscribe",
			chanId,
		}, cancellationToken, subId);

		client.Counter--;

		if (client.Counter <= 0)
		{
			client.Disconnect();

			_clientsByChannelId.Remove(chanId);

			foreach (var p in _marketDataClients.ToArray())
			{
				if (p.Value != client)
					continue;

				_marketDataClients.Remove(p.Key);
			}

			if (client == _currentMarketDataClient)
				_currentMarketDataClient = null;
		}
	}

	private readonly TimeSpan _sleepInterval = TimeSpan.FromSeconds(5);

	private async Task<MarketDataWebSocketClient> CreateAndSubscribe(CancellationToken cancellationToken, params string[] args)
	{
		if (_currentMarketDataClient != null && ++_currentMarketDataClient.Counter < _maxSubscriptions)
		{
			await TimeSpan.FromSeconds(0.1).Delay(cancellationToken);
			return _currentMarketDataClient;
		}

		await _sleepInterval.Delay(cancellationToken);

		_currentMarketDataClient = new MarketDataWebSocketClient(this, "wss://api-pub.bitfinex.com/ws/2")
		{
			Counter = 1,
			ReconnectAttempts = _attemptsCount,
			WorkingTime = _workingTime,
		};
		_marketDataClients.Add(args.Join("_"), _currentMarketDataClient);

		await _currentMarketDataClient.ConnectAsync(cancellationToken);

		return _currentMarketDataClient;
	}

	private static void FillOrderRequest(dynamic request, decimal? price, decimal amount, decimal? trailingPrice, decimal? auxLimitPrice, decimal? ocoPrice, short? flags, string tif, int? leverage)
	{
		if (price != null)
			request.price = price.Value.To<string>();

		request.amount = amount.To<string>();

		if (leverage != null)
			request.lev = leverage.Value;

		if (trailingPrice != null)
			request.price_trailing = trailingPrice.Value.To<string>();

		if (auxLimitPrice != null)
			request.price_aux_limit = auxLimitPrice.Value.To<string>();

		if (ocoPrice != null)
			request.price_oco_stop = ocoPrice.Value.To<string>();

		if (flags != null)
			request.flags = flags.Value;

		if (!tif.IsEmpty())
			request.tif = tif;
	}

	public ValueTask RegisterOrder(long transactionId, string symbol, string type, decimal? price, decimal amount, decimal? trailingPrice, decimal? auxLimitPrice, decimal? ocoPrice, short? flags, string tif, int? leverage, CancellationToken cancellationToken)
	{
		dynamic request = new ExpandoObject();

		request.cid = transactionId;
		request.type = type.ToUpperInvariant();
		request.symbol = symbol;

		FillOrderRequest(request, price, amount, trailingPrice, auxLimitPrice, ocoPrice, flags, tif, leverage);

		return _client.SendAsync(new object[] { 0, "on", null, request }, cancellationToken);
	}

	public ValueTask ReplaceOrder(long orderId, decimal amount, decimal? delta, decimal price, decimal? trailingPrice, decimal? auxLimitPrice, short? flags, string tif, int? leverage, CancellationToken cancellationToken)
	{
		dynamic request = new ExpandoObject();
		request.id = orderId;

		FillOrderRequest(request, price, amount, trailingPrice, auxLimitPrice, null, flags, tif, leverage);

		if (delta != null)
			request.delta = delta.Value.To<string>();

		return _client.SendAsync(new object[] { 0, "ou", null, request }, cancellationToken);
	}

	public ValueTask CancelOrder(long orderId, CancellationToken cancellationToken)
	{
		return _client.SendAsync(new object[]
		{
			0, "oc", null, new { id = orderId }
		}, cancellationToken);
	}

	public ValueTask CancelAllOrders(CancellationToken cancellationToken)
	{
		return _client.SendAsync(new object[]
		{
			0, "oc_multi", null, new { all = 1 }
		}, cancellationToken);
	}
}
