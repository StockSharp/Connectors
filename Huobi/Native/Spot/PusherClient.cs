namespace StockSharp.Huobi.Native.Spot;

using Ecng.IO.Compression;

using StockSharp.Huobi.Native.Spot.Model;

class PusherClient : BaseLogReceiver
{
	private class CommandInfo(long id, bool? isSubscribe, SecurityId securityId, MessageTypes type, object arg = null)
	{
		public long Id { get; } = id;
		public bool? IsSubscribe { get; } = isSubscribe;
		public SecurityId SecurityId { get; } = securityId;
		public MessageTypes Type { get; } = type;
		public object Arg { get; } = arg;
	}

	private static class Commands
	{
		public const string Subscribe = "sub";
		public const string Unsubscribe = "unsub";
		public const string Request = "req";
		public const string Operation = "op";
		public const string Ping = "ping";
		public const string Pong = "pong";
		public const string Push = "push";
	}

	private abstract class BaseClient : BaseLogReceiver
	{
		private readonly string _address;

		private readonly PusherClient _parent;
		private readonly WebSocketClient _client;

		private readonly SynchronizedDictionary<long, CommandInfo> _commands = [];

		private bool _isConnectionControl;

		// to get readable name after obfuscation
		public override string Name => _parent.Name;

		protected WebSocketClient Client => _client;
		protected readonly int Type;

		protected BaseClient(int type, string address, PusherClient parent, WorkingTime workingTime)
		{
			Type = type;
			_address = address;

			Parent = _parent = parent;

			_client = new(
				_address,
				(state, token) =>
				{
					if (_parent.StateChanged is { } handler)
						return handler(state, token);
					return default;
				},
				(error, token) =>
				{
					this.AddErrorLog(error);
					if (_parent.Error is { } handler)
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

		private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
		{
			var obj = msg.AsObject();

			if (!await OnProcessImplAsync(_parent, obj, cancellationToken))
				this.AddErrorLog(LocalizedStrings.UnknownEvent, (string)obj.ToString());
		}

		protected abstract ValueTask<bool> OnProcessImplAsync(PusherClient parent, dynamic obj, CancellationToken cancellationToken);

		protected override void DisposeManaged()
		{
			_client.Dispose();
			base.DisposeManaged();
		}

		protected CommandInfo GetInfo(long id)
			=> TryGetInfo(id) ?? throw new ArgumentException($"Command {id} not exist.");

		protected CommandInfo TryGetInfo(long id) => _commands.TryGetValue(id);

		public ValueTask ConnectAsync(bool isConnectionControl, CancellationToken cancellationToken)
		{
			_isConnectionControl = isConnectionControl;
			_commands.Clear();

			this.AddInfoLog(LocalizedStrings.Connecting);
			return _client.ConnectAsync(cancellationToken);
		}

		public void Disconnect()
		{
			if (!_client.IsConnected)
				return;

			this.AddInfoLog(LocalizedStrings.Disconnecting);
			_client.Disconnect();
		}

		protected ValueTask RaisePingAsync(long id, CancellationToken cancellationToken)
		{
			if (_parent.Ping is { } handler)
				return handler($"{Type}={id}", cancellationToken);
			return default;
		}

		public abstract ValueTask Pong(long id, CancellationToken cancellationToken);

		protected ValueTask Request(string topic, CommandInfo info, long? from, long? to, CancellationToken cancellationToken)
		{
			if (info is null)
				throw new ArgumentNullException(nameof(info));

			dynamic body = new ExpandoObject();

			body.req = topic;
			body.id = info.Id;

			if (from != null)
				body.from = from.Value;

			if (to != null)
				body.to = to.Value;

			return Send(body, info, cancellationToken);
		}

		protected ValueTask Send(object body, CommandInfo info, CancellationToken cancellationToken)
		{
			if (info is not null)
				_commands.Add(info.Id, info);

			return _client.SendAsync(body, cancellationToken);
		}
	}

	private class AccountClient(Authenticator authenticator, string address, PusherClient parent, WorkingTime workingTime) : BaseClient(2, address, parent, workingTime)
	{
		private readonly Authenticator _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

		// to get readable name after obfuscation
		public override string Name => base.Name + "_Account";

        public ValueTask Sign(CancellationToken cancellationToken)
		{
			var timestamp = Authenticator.GetTimestamp();

			var accessKey = _authenticator.Key.UnSecure();
			var signature = _authenticator.Sign(RestSharp.Method.Get, "api.huobi.pro", "/ws/v2", $"accessKey={accessKey}&signatureMethod={Authenticator.Method}&signatureVersion={Authenticator.Version21}&timestamp={timestamp.EncodeUrl().UrlEncodeToUpperCase()}");

			return Send(new
			{
				action = Commands.Request,
				ch = Channels.Auth,
				@params = new
				{
					authType = "api",
					accessKey,
					signatureMethod = Authenticator.Method,
					signatureVersion = Authenticator.Version21,
					timestamp,
					signature
				}
			}, null, cancellationToken);
		}

		private ValueTask Subscribe(string topic, CommandInfo info, CancellationToken cancellationToken)
		{
			return Send(new { action = Commands.Subscribe, ch = topic }, info, cancellationToken);
		}

		private ValueTask UnSubscribe(string topic, CommandInfo info, CancellationToken cancellationToken)
		{
			return Send(new { action = Commands.Unsubscribe, ch = topic }, info, cancellationToken);
		}

		public override ValueTask Pong(long id, CancellationToken cancellationToken)
		{
			return Send(new
			{
				action = Commands.Pong,
				data = new
				{
					ts = id,
				}
			}, null, cancellationToken);
		}

		protected override async ValueTask<bool> OnProcessImplAsync(PusherClient parent, dynamic obj, CancellationToken cancellationToken)
		{
			var channel = (string)obj.ch;
			var data = (JToken)obj.data;

			switch ((string)obj.action)
			{
				case Commands.Ping:
				{
					await RaisePingAsync((long)obj.data.ts, cancellationToken);
					return true;
				}

				case Commands.Push:
				{
					if (channel.StartsWithIgnoreCase("order"))
					{
						if (parent.OrderChanged is { } handler)
							await handler(data.DeserializeObject<SocketOrder>(), cancellationToken);
					}
					else if (channel.StartsWithIgnoreCase("accounts"))
					{
						if (parent.BalanceChanged is { } handler)
							await handler(data.DeserializeObject<SocketBalance>(), cancellationToken);
					}
					else
						return false;

					return true;
				}

				case Commands.Subscribe:
				case Commands.Unsubscribe:
					return true;

				case Commands.Request:
				{
					switch (channel)
					{
						case Channels.Auth:
						{
							if ((int)obj.code != 200)
							{
								if (parent.Error is { } handler)
									await handler(new InvalidOperationException((string)obj.message.ToString()), cancellationToken);
							}

							return true;
						}
					}

					return false;
				}

				default:
					return false;
			}
		}

		private static class Channels
		{
			public const string Accounts = "accounts.update#0";
			public const string Orders = "orders#*";
			public const string Auth = "auth";
		}

		public ValueTask SubscribeAccounts(CommandInfo info, CancellationToken cancellationToken)
		{
			return Subscribe(Channels.Accounts, info, cancellationToken);
		}

		public ValueTask UnSubscribeAccounts(CommandInfo info, CancellationToken cancellationToken)
		{
			return UnSubscribe(Channels.Accounts, info, cancellationToken);
		}

		public ValueTask SubscribeOrders(CommandInfo info, CancellationToken cancellationToken)
		{
			return Subscribe(Channels.Orders, info, cancellationToken);
		}

		public ValueTask UnSubscribeOrders(CommandInfo info, CancellationToken cancellationToken)
		{
			return UnSubscribe(Channels.Orders, info, cancellationToken);
		}
	}

	private class MarketDataClient : BaseClient
	{
		private static readonly HashSet<string> _md520SupportedSymbols;

		static MarketDataClient()
		{
			// 5/20 depth supported only by limited number of symbols
			// list copied from docs: https://huobiapi.github.io/docs/spot/v1/en/#market-by-price-incremental-update
			_md520SupportedSymbols = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
			{
				"btcusdt",
				"ethusdt",
				"xrpusdt",
				"eosusdt",
				"ltcusdt",
				"etcusdt",
				"adausdt",
				"dashusdt",
				"bsvusdt",
			};
		}

		private readonly SynchronizedDictionary<string, long> _subscriptionChannels = new(StringComparer.InvariantCultureIgnoreCase);

		// to get readable name after obfuscation
		public override string Name => base.Name + "_MarketData_" + Type;

		public MarketDataClient(int type, string address, PusherClient parent, WorkingTime workingTime)
			: base(type, address, parent, workingTime)
		{
			Client.PreProcess2 += OnPreProcess;
		}

		protected override void DisposeManaged()
		{
			Client.PreProcess2 -= OnPreProcess;
			base.DisposeManaged();
		}

		private int OnPreProcess(ReadOnlyMemory<byte> source, Memory<byte> dest)
			=> source.Span.UnGZip(dest.Span);

		private static class Channels
		{
			public const string Ticker = "market.{0}.detail";
			public const string Best = "market.{0}.bbo";
			public const string Deals = "market.{0}.trade.detail";
			public const string OrderBook = "market.{0}.mbp.{1}";
			public const string Candles = "market.{0}.kline.{1}";
		}

		private ValueTask Subscribe(string topic, CommandInfo info, CancellationToken cancellationToken)
		{
			return Send(new { sub = topic, id = info.Id }, info, cancellationToken);
		}

		private ValueTask UnSubscribe(string topic, CommandInfo info, CancellationToken cancellationToken)
		{
			return Send(new { unsub = topic, id = info.Id }, info, cancellationToken);
		}

		public ValueTask SubscribeTicker(string symbol, CommandInfo info, CancellationToken cancellationToken)
		{
			return Subscribe(Channels.Ticker.Put(symbol), info, cancellationToken);
		}

		public ValueTask UnSubscribeTicker(string symbol, CommandInfo info, CancellationToken cancellationToken)
		{
			return UnSubscribe(Channels.Ticker.Put(symbol), info, cancellationToken);
		}

		public ValueTask SubscribeBest(string symbol, CommandInfo info, CancellationToken cancellationToken)
		{
			return Subscribe(Channels.Best.Put(symbol), info, cancellationToken);
		}

		public ValueTask UnSubscribeBest(string symbol, CommandInfo info, CancellationToken cancellationToken)
		{
			return UnSubscribe(Channels.Best.Put(symbol), info, cancellationToken);
		}

		public ValueTask SubscribeTrades(string symbol, CommandInfo info, CancellationToken cancellationToken)
		{
			return Subscribe(Channels.Deals.Put(symbol), info, cancellationToken);
		}

		public ValueTask UnSubscribeTrades(string symbol, CommandInfo info, CancellationToken cancellationToken)
		{
			return UnSubscribe(Channels.Deals.Put(symbol), info, cancellationToken);
		}

		private int CoerceBookDepth(string symbol, int requestedDepth)
		{
			var actualDepth = !_md520SupportedSymbols.Contains(symbol)
				? 150
				: requestedDepth switch
				{
					< 20   => 5,
					< 150  => 20,
					_      => 150
				};

			if (requestedDepth != actualDepth)
				this.AddWarningLog("Market depth MBP({0}) is not supported for symbol {1}. Will use depth={2} instead.", requestedDepth, symbol, actualDepth);

			return actualDepth;
		}

		public ValueTask RequestOrderBook(string symbol, int depth, CommandInfo info, CancellationToken cancellationToken)
		{
			depth = CoerceBookDepth(symbol, depth);
			return Request(Channels.OrderBook.Put(symbol, depth), info, null, null, cancellationToken);
		}

		public ValueTask SubscribeOrderBook(string symbol, int depth, CommandInfo info, CancellationToken cancellationToken)
		{
			depth = CoerceBookDepth(symbol, depth);
			return Subscribe(Channels.OrderBook.Put(symbol, depth), info, cancellationToken);
		}

		public ValueTask UnSubscribeOrderBook(string symbol, int depth, CommandInfo info, CancellationToken cancellationToken)
		{
			depth = CoerceBookDepth(symbol, depth);
			return UnSubscribe(Channels.OrderBook.Put(symbol, depth), info, cancellationToken);
		}

		public ValueTask RequestCandles(string symbol, TimeSpan timeFrame, long? from, long? to, CommandInfo info, CancellationToken cancellationToken)
		{
			return Request(Channels.Candles.Put(symbol, timeFrame.ToNative()), info, from, to, cancellationToken);
		}

		public ValueTask SubscribeCandles(string symbol, TimeSpan timeFrame, CommandInfo info, CancellationToken cancellationToken)
		{
			return Subscribe(Channels.Candles.Put(symbol, timeFrame.ToNative()), info, cancellationToken);
		}

		public ValueTask UnSubscribeCandles(string symbol, TimeSpan timeFrame, CommandInfo info, CancellationToken cancellationToken)
		{
			return UnSubscribe(Channels.Candles.Put(symbol, timeFrame.ToNative()), info, cancellationToken);
		}

		public override ValueTask Pong(long id, CancellationToken cancellationToken)
		{
			return Send(new { pong = id }, null, cancellationToken);
		}

		protected override async ValueTask<bool> OnProcessImplAsync(PusherClient parent, dynamic obj, CancellationToken cancellationToken)
		{
			if (obj.ping != null)
			{
				await RaisePingAsync((long)obj.ping, cancellationToken);
				return true;
			}
			else if (obj.status != null)
			{
				if (obj.status == "error")
				{
					if (parent.Error is { } errorHandler)
						await errorHandler(new InvalidOperationException((string)((JObject)obj).Property("err-msg").Value), cancellationToken);
				}
				else
				{
					var id = (long)obj.id;

					if (obj.subbed != null)
					{
						_subscriptionChannels.TryAdd((string)obj.subbed, id);
					}
					else if (obj.unsubbed != null)
					{
						_subscriptionChannels.Remove((string)obj.unsubbed);
					}

					if (parent.SubscriptionResponse is { } subHandler)
						await subHandler(id, cancellationToken);

					var subscription = TryGetInfo(id);

					if (subscription != null && subscription.IsSubscribe == null)
					{
						var c = subscription.SecurityId;

						if (subscription.Type == MessageTypes.CandleTimeFrame)
						{
							if (parent.CandlesReceived is { } handler)
								await handler(subscription.SecurityId, (TimeSpan)subscription.Arg, id, ((JToken)obj.data).DeserializeObject<Ohlc[]>(), cancellationToken);
						}
						else if (subscription.Type == MessageTypes.QuoteChange)
						{
							if (parent.OrderBookChanged is { } handler)
								await handler(((long)obj.ts).FromUnix(false), subscription.SecurityId, (int)subscription.Arg, true, ((JToken)obj.data).DeserializeObject<OrderBook>(), cancellationToken);
						}
						else
						{

						}
					}
				}

				return true;
			}
			else if (obj.ch != null)
			{
				if (!_subscriptionChannels.TryGetValue((string)obj.ch, out var id))
					return false;

				var subscription = GetInfo(id);

				var secId = subscription.SecurityId;

				switch (subscription.Type)
				{
					case MessageTypes.QuoteChange:
						if (parent.OrderBookChanged is { } obHandler)
							await obHandler(((long)obj.ts).FromUnix(false), secId, (int)subscription.Arg, false, ((JToken)obj.tick).DeserializeObject<OrderBook>(), cancellationToken);
						return true;

					case MessageTypes.Execution:
						if (parent.NewTrades is { } tradesHandler)
							await tradesHandler(((long)obj.ts).FromUnix(false), secId, ((JToken)obj.tick.data).DeserializeObject<SocketTrade[]>(), cancellationToken);
						return true;

					case MessageTypes.Level1Change:
					{
						if (subscription.Arg is null)
						{
							if (parent.TickerChanged is { } tickerHandler)
								await tickerHandler(((long)obj.ts).FromUnix(false), secId, ((JToken)obj.tick).DeserializeObject<Ticker>(), cancellationToken);
						}
						else
						{
							if (parent.BestChanged is { } bestHandler)
								await bestHandler(((long)obj.ts).FromUnix(false), secId, ((JToken)obj.tick).DeserializeObject<Best>(), cancellationToken);
						}

						return true;
					}

					case MessageTypes.CandleTimeFrame:
						if (parent.NewCandle is { } candleHandler)
							await candleHandler(((long)obj.ts).FromUnix(false), secId, (TimeSpan)subscription.Arg, id, ((JToken)obj.tick).DeserializeObject<Ohlc>(), cancellationToken);
						return true;

					default:
						return false;
				}
			}
			else
			{
				return false;
			}
		}
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Huobi) + "_" + nameof(Spot) + "_" + nameof(PusherClient);

	public event Func<DateTime, SecurityId, Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<DateTime, SecurityId, SocketTrade[], CancellationToken, ValueTask> NewTrades;
	public event Func<DateTime, SecurityId, int, bool, OrderBook, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<DateTime, SecurityId, TimeSpan, long, Ohlc, CancellationToken, ValueTask> NewCandle;
	public event Func<SecurityId, TimeSpan, long, Ohlc[], CancellationToken, ValueTask> CandlesReceived;
	public event Func<DateTime, SecurityId, Best, CancellationToken, ValueTask> BestChanged;
	public event Func<SocketOrder, CancellationToken, ValueTask> OrderChanged;
	public event Func<SocketBalance, CancellationToken, ValueTask> BalanceChanged;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;
	public event Func<string, CancellationToken, ValueTask> Ping;
	public event Func<long, CancellationToken, ValueTask> SubscriptionResponse;

	private readonly string _domain;

	private readonly MarketDataClient _feedClient;
	private readonly MarketDataClient _mbpClient;
	private readonly AccountClient _accountClient;

	public PusherClient(Authenticator authenticator, string domain, WorkingTime workingTime)
	{
		if (domain.IsEmpty())
			throw new ArgumentNullException(nameof(domain));

		_domain = domain;

		_feedClient = new MarketDataClient(0, $"wss://{_domain}/ws", this, workingTime);
		_mbpClient = new MarketDataClient(1, $"wss://{_domain}/feed", this, workingTime);
		_accountClient = new AccountClient(authenticator, $"wss://{_domain}/ws/v2", this, workingTime);
	}

	protected override void DisposeManaged()
	{
		_feedClient.Dispose();
		_mbpClient.Dispose();
		_accountClient.Dispose();

		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(bool marketData, bool transactions, CancellationToken cancellationToken)
	{
		if (marketData)
		{
			await _feedClient.ConnectAsync(!transactions, cancellationToken);
			await _mbpClient.ConnectAsync(false, cancellationToken);
		}

		if (transactions)
		{
			await _accountClient.ConnectAsync(true, cancellationToken);
			await _accountClient.Sign(cancellationToken);
		}
	}

	public void Disconnect()
	{
		_feedClient.Disconnect();
		_mbpClient.Disconnect();
		_accountClient.Disconnect();
	}

	public ValueTask SubscribeTicker(SecurityId securityId, long id, CancellationToken cancellationToken)
	{
		return _feedClient.SubscribeTicker(securityId.ToSymbol(), new CommandInfo(id, true, securityId, MessageTypes.Level1Change), cancellationToken);
	}

	public ValueTask UnSubscribeTicker(SecurityId securityId, long id, CancellationToken cancellationToken)
	{
		return _feedClient.UnSubscribeTicker(securityId.ToSymbol(), new CommandInfo(id, false, securityId, MessageTypes.Level1Change), cancellationToken);
	}

	public ValueTask SubscribeBest(SecurityId securityId, long id, CancellationToken cancellationToken)
	{
		return _feedClient.SubscribeBest(securityId.ToSymbol(), new CommandInfo(id, true, securityId, MessageTypes.Level1Change, Level1Fields.BestBidPrice), cancellationToken);
	}

	public ValueTask UnSubscribeBest(SecurityId securityId, long id, CancellationToken cancellationToken)
	{
		return _feedClient.UnSubscribeBest(securityId.ToSymbol(), new CommandInfo(id, false, securityId, MessageTypes.Level1Change, Level1Fields.BestBidPrice), cancellationToken);
	}

	public ValueTask SubscribeTrades(SecurityId securityId, long id, CancellationToken cancellationToken)
	{
		return _feedClient.SubscribeTrades(securityId.ToSymbol(), new CommandInfo(id, true, securityId, MessageTypes.Execution, DataType.Ticks), cancellationToken);
	}

	public ValueTask UnSubscribeTrades(SecurityId securityId, long id, CancellationToken cancellationToken)
	{
		return _feedClient.UnSubscribeTrades(securityId.ToSymbol(), new CommandInfo(id, false, securityId, MessageTypes.Execution, DataType.Ticks), cancellationToken);
	}

	public ValueTask RequestOrderBook(SecurityId securityId, int depth, long id, CancellationToken cancellationToken)
	{
		return _mbpClient.RequestOrderBook(securityId.ToSymbol(), depth, new CommandInfo(id, null, securityId, MessageTypes.QuoteChange, depth), cancellationToken);
	}

	public ValueTask SubscribeOrderBook(SecurityId securityId, int depth, long id, CancellationToken cancellationToken)
	{
		return _mbpClient.SubscribeOrderBook(securityId.ToSymbol(), depth, new CommandInfo(id, true, securityId, MessageTypes.QuoteChange, depth), cancellationToken);
	}

	public ValueTask UnSubscribeOrderBook(SecurityId securityId, int depth, long id, CancellationToken cancellationToken)
	{
		return _mbpClient.UnSubscribeOrderBook(securityId.ToSymbol(), depth, new CommandInfo(id, false, securityId, MessageTypes.QuoteChange, depth), cancellationToken);
	}

	public ValueTask RequestCandles(SecurityId securityId, TimeSpan timeFrame, long id, long? from, long? to, CancellationToken cancellationToken)
	{
		return _feedClient.RequestCandles(securityId.ToSymbol(), timeFrame, from, to, new CommandInfo(id, null, securityId, MessageTypes.CandleTimeFrame, timeFrame), cancellationToken);
	}

	public ValueTask SubscribeCandles(SecurityId securityId, TimeSpan timeFrame, long id, CancellationToken cancellationToken)
	{
		return _feedClient.SubscribeCandles(securityId.ToSymbol(), timeFrame, new CommandInfo(id, true, securityId, MessageTypes.CandleTimeFrame, timeFrame), cancellationToken);
	}

	public ValueTask UnSubscribeCandles(SecurityId securityId, TimeSpan timeFrame, long id, CancellationToken cancellationToken)
	{
		return _feedClient.UnSubscribeCandles(securityId.ToSymbol(), timeFrame, new CommandInfo(id, false, securityId, MessageTypes.CandleTimeFrame, timeFrame), cancellationToken);
	}

	public ValueTask SubscribeAccounts(long id, CancellationToken cancellationToken)
	{
		return _accountClient.SubscribeAccounts(new CommandInfo(id, true, default, MessageTypes.PortfolioLookup), cancellationToken);
	}

	public ValueTask UnSubscribeAccounts(long id, CancellationToken cancellationToken)
	{
		return _accountClient.UnSubscribeAccounts(new CommandInfo(id, false, default, MessageTypes.PortfolioLookup), cancellationToken);
	}

	public ValueTask SubscribeOrders(long id, CancellationToken cancellationToken)
	{
		return _accountClient.SubscribeOrders(new CommandInfo(id, true, default, MessageTypes.OrderStatus), cancellationToken);
	}

	public ValueTask UnSubscribeOrders(long id, CancellationToken cancellationToken)
	{
		return _accountClient.UnSubscribeOrders(new CommandInfo(id, false, default, MessageTypes.OrderStatus), cancellationToken);
	}

	public async ValueTask Pong(string id, CancellationToken cancellationToken)
	{
		var parts = id.SplitByEqual(false);
		var feedId = parts[0].To<int>();
		var pingId = parts[1].To<long>();

		switch (feedId)
		{
			case 0:
				await _feedClient.Pong(pingId, cancellationToken);
				break;
			case 1:
				await _mbpClient.Pong(pingId, cancellationToken);
				break;
			case 2:
				await _accountClient.Pong(pingId, cancellationToken);
				break;
			default:
				this.AddErrorLog("Unk ping feed {0}.", feedId);
				break;
		}
	}
}