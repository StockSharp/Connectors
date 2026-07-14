namespace StockSharp.Huobi.Native.Futures;

using Ecng.IO.Compression;

using StockSharp.Huobi.Native.Futures.Model;

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
		public const string Notify = "notify";
		public const string Close = "close";
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

			_client.PreProcess2 += OnPreProcess;
		}

		private int OnPreProcess(ReadOnlyMemory<byte> source, Memory<byte> dest)
			=> source.Span.UnGZip(dest.Span);

		private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
		{
			var obj = msg.AsObject();

			if (!await OnProcessImplAsync(_parent, obj, cancellationToken))
				this.AddErrorLog(LocalizedStrings.UnknownEvent, (string)obj.ToString());
		}

		protected abstract ValueTask<bool> OnProcessImplAsync(PusherClient parent, dynamic obj, CancellationToken cancellationToken);

		protected override void DisposeManaged()
		{
			_client.PreProcess2 -= OnPreProcess;
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
			body.id = info.Id.To<string>();

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
			var signature = _authenticator.Sign(RestSharp.Method.Get, "api.hbdm.com", "/notification", $"AccessKeyId={accessKey}&SignatureMethod={Authenticator.Method}&SignatureVersion={Authenticator.Version2}&Timestamp={timestamp.EncodeUrl().UrlEncodeToUpperCase()}");

			return Send(new
			{
				op = Channels.Auth,
				type = "api",
				AccessKeyId = accessKey,
				SignatureMethod = Authenticator.Method,
				SignatureVersion = Authenticator.Version2,
				Timestamp = timestamp,
				Signature = signature
			}, null, cancellationToken);
		}

		private ValueTask Subscribe(string topic, CommandInfo info, CancellationToken cancellationToken)
		{
			return Send(new { op = Commands.Subscribe, ch = topic, cid = info.Id }, info, cancellationToken);
		}

		private ValueTask UnSubscribe(string topic, CommandInfo info, CancellationToken cancellationToken)
		{
			return Send(new { op = Commands.Unsubscribe, ch = topic, cid = info.Id }, info, cancellationToken);
		}

		public override ValueTask Pong(long id, CancellationToken cancellationToken)
		{
			return Send(new
			{
				op = Commands.Pong,
				ts = id.To<string>(),
			}, null, cancellationToken);
		}

		protected override async ValueTask<bool> OnProcessImplAsync(PusherClient parent, dynamic obj, CancellationToken cancellationToken)
		{
			var channel = (string)obj.topic;
			var data = (JToken)obj.data;

			switch ((string)obj.op)
			{
				case Commands.Ping:
				{
					await RaisePingAsync((long)obj.ts, cancellationToken);
					return true;
				}

				case Commands.Close:
				{
					return true;
				}

				case Commands.Notify:
				{
					var dt = ((long)obj.ts).FromUnix(false);

					if (channel.StartsWithIgnoreCase("order"))
					{
						if (parent.OrderChanged is { } handler)
							await handler(dt, ((JToken)obj).DeserializeObject<SocketOrder>(), cancellationToken);
					}
					else if (channel.StartsWithIgnoreCase("matchOrders"))
					{
						if (parent.OrderChanged is { } handler)
							await handler(dt, ((JToken)obj).DeserializeObject<SocketOrder>(), cancellationToken);
					}
					else if (channel.StartsWithIgnoreCase("accounts"))
					{
						if (parent.BalancesChanged is { } handler)
							await handler(dt, data.DeserializeObject<IEnumerable<Balance>>(), cancellationToken);
					}
					else if (channel.StartsWithIgnoreCase("positions"))
					{
						if (parent.PositionsChanged is { } handler)
							await handler(dt, data.DeserializeObject<IEnumerable<Position>>(), cancellationToken);
					}
					else
						return false;

					return true;
				}

				case Commands.Subscribe:
				case Commands.Unsubscribe:
					return true;

				case Channels.Auth:
				{
					if ((int)obj.Property("err-code").Value != 0)
					{
						if (parent.Error is { } handler)
							await handler(new InvalidOperationException((string)obj.Property("err-msg").Value), cancellationToken);
					}

					return true;
				}

				default:
					return false;
			}
		}

		private static class Channels
		{
			public const string Accounts = "accounts.*";
			public const string Positions = "accounts.*";
			public const string Orders = "orders.*";
			public const string MatchOrders = "matchOrders.*";
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

		public ValueTask SubscribePositions(CommandInfo info, CancellationToken cancellationToken)
		{
			return Subscribe(Channels.Positions, info, cancellationToken);
		}

		public ValueTask UnSubscribePositions(CommandInfo info, CancellationToken cancellationToken)
		{
			return UnSubscribe(Channels.Positions, info, cancellationToken);
		}

		public ValueTask SubscribeOrders(CommandInfo info, CancellationToken cancellationToken)
		{
			return Subscribe(Channels.Orders, info, cancellationToken);
		}

		public ValueTask UnSubscribeOrders(CommandInfo info, CancellationToken cancellationToken)
		{
			return UnSubscribe(Channels.Orders, info, cancellationToken);
		}

		public ValueTask SubscribeMatchOrders(CommandInfo info, CancellationToken cancellationToken)
		{
			return Subscribe(Channels.Orders, info, cancellationToken);
		}

		public ValueTask UnSubscribeMatchOrders(CommandInfo info, CancellationToken cancellationToken)
		{
			return UnSubscribe(Channels.Orders, info, cancellationToken);
		}
	}

	private class MarketDataClient(int type, string address, PusherClient parent, WorkingTime workingTime) : BaseClient(type, address, parent, workingTime)
	{
		private readonly SynchronizedDictionary<string, CommandInfo> _subscriptionChannels = new(StringComparer.InvariantCultureIgnoreCase);

		// to get readable name after obfuscation
		public override string Name => base.Name + "_MarketData_" + Type;

        private static class Channels
		{
			public const string Ticker = "market.{0}.detail";
			public const string Best = "market.{0}.bbo";
			public const string Deals = "market.{0}.trade.detail";
			public const string OrderBook = "market.{0}.depth.size_{1}.high_freq";
			public const string Candles = "market.{0}.kline.{1}";
		}

		private ValueTask RaiseSubscriptionResponseAsync(CommandInfo info, CancellationToken cancellationToken)
		{
			if (((PusherClient)Parent).SubscriptionResponse is { } handler)
				return handler(info.Id, cancellationToken);
			return default;
		}

		private async ValueTask Subscribe(string topic, CommandInfo info, CancellationToken cancellationToken)
		{
			_subscriptionChannels[topic] = info;

			dynamic body = new ExpandoObject();

			body.sub = topic;
			body.id = info.Id.To<string>();

			if (topic.EndsWithIgnoreCase("high_freq"))
				body.data_type = "incremental";

			await Send((object)body, info, cancellationToken);
			await RaiseSubscriptionResponseAsync(info, cancellationToken);
		}

		private async ValueTask UnSubscribe(string topic, CommandInfo info, CancellationToken cancellationToken)
		{
			dynamic body = new ExpandoObject();

			body.unsub = topic;
			body.id = info.Id.To<string>();

			if (topic.EndsWithIgnoreCase("high_freq"))
				body.data_type = "incremental";

			await Send((object)body, info, cancellationToken);
			await RaiseSubscriptionResponseAsync(info, cancellationToken);

			// prevent warning logs
			//_subscriptionChannels.Remove(topic);
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

		public ValueTask SubscribeOrderBook(string symbol, int depth, CommandInfo info, CancellationToken cancellationToken)
		{
			return Subscribe(Channels.OrderBook.Put(symbol, depth), info, cancellationToken);
		}

		public ValueTask UnSubscribeOrderBook(string symbol, int depth, CommandInfo info, CancellationToken cancellationToken)
		{
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

					var subscription = GetInfo(id);

					if (subscription.Type == MessageTypes.CandleTimeFrame && subscription.IsSubscribe == null)
					{
						if (parent.CandlesReceived is { } handler)
							await handler(subscription.SecurityId, (TimeSpan)subscription.Arg, id, ((JToken)obj.data).DeserializeObject<Ohlc[]>(), cancellationToken);
					}
				}

				return true;
			}
			else if (obj.ch != null)
			{
				if (!_subscriptionChannels.TryGetValue((string)obj.ch, out var subscription))
					return false;

				var secId = subscription.SecurityId;

				switch (subscription.Type)
				{
					case MessageTypes.QuoteChange:
						if (parent.OrderBookChanged is { } obHandler)
							await obHandler(((long)obj.ts).FromUnix(false), secId, ((JToken)obj.tick).DeserializeObject<OrderBook>(), cancellationToken);
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
							await candleHandler(((long)obj.ts).FromUnix(false), secId, (TimeSpan)subscription.Arg, subscription.Id, ((JToken)obj.tick).DeserializeObject<Ohlc>(), cancellationToken);
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
	public override string Name => nameof(Huobi) + "_" + nameof(Futures) + "_" + nameof(PusherClient);

	public event Func<DateTime, SecurityId, Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<DateTime, SecurityId, SocketTrade[], CancellationToken, ValueTask> NewTrades;
	public event Func<DateTime, SecurityId, OrderBook, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<DateTime, SecurityId, TimeSpan, long, Ohlc, CancellationToken, ValueTask> NewCandle;
	public event Func<SecurityId, TimeSpan, long, Ohlc[], CancellationToken, ValueTask> CandlesReceived;
	public event Func<DateTime, SecurityId, Best, CancellationToken, ValueTask> BestChanged;
	public event Func<DateTime, SocketOrder, CancellationToken, ValueTask> OrderChanged;
	public event Func<DateTime, IEnumerable<Balance>, CancellationToken, ValueTask> BalancesChanged;
	public event Func<DateTime, IEnumerable<Position>, CancellationToken, ValueTask> PositionsChanged;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;
	public event Func<string, CancellationToken, ValueTask> Ping;
	public event Func<long, CancellationToken, ValueTask> SubscriptionResponse;

	private readonly string _domain;

	private readonly MarketDataClient _feedClient;
	//private readonly MarketDataClient _mbpClient;
	private readonly AccountClient _accountClient;

	public PusherClient(Authenticator authenticator, string domain, WorkingTime workingTime)
	{
		if (domain.IsEmpty())
			throw new ArgumentNullException(nameof(domain));

		_domain = domain;

		_feedClient = new MarketDataClient(0, $"wss://{_domain}/ws", this, workingTime);
		//_mbpClient = new MarketDataClient(1, $"wss://{_domain}/feed", this);
		_accountClient = new AccountClient(authenticator, $"wss://{_domain}/notification", this, workingTime);
	}

	protected override void DisposeManaged()
	{
		_feedClient.Dispose();
		//_mbpClient.Dispose();
		_accountClient.Dispose();

		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(bool marketData, bool transactions, CancellationToken cancellationToken)
	{
		if (marketData)
		{
			await _feedClient.ConnectAsync(!transactions, cancellationToken);
			//_mbpClient.Connect(false);
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
		//_mbpClient.Disconnect();
		_accountClient.Disconnect();
	}

	public ValueTask SubscribeTicker(string symbol, SecurityId securityId, long id, CancellationToken cancellationToken)
	{
		return _feedClient.SubscribeTicker(symbol, new(id, true, securityId, MessageTypes.Level1Change), cancellationToken);
	}

	public ValueTask UnSubscribeTicker(string symbol, SecurityId securityId, long id, CancellationToken cancellationToken)
	{
		return _feedClient.UnSubscribeTicker(symbol, new(id, false, securityId, MessageTypes.Level1Change), cancellationToken);
	}

	public ValueTask SubscribeBest(string symbol, SecurityId securityId, long id, CancellationToken cancellationToken)
	{
		return _feedClient.SubscribeBest(symbol, new(id, true, securityId, MessageTypes.Level1Change, Level1Fields.BestBidPrice), cancellationToken);
	}

	public ValueTask UnSubscribeBest(string symbol, SecurityId securityId, long id, CancellationToken cancellationToken)
	{
		return _feedClient.UnSubscribeBest(symbol, new(id, false, securityId, MessageTypes.Level1Change, Level1Fields.BestBidPrice), cancellationToken);
	}

	public ValueTask SubscribeTrades(string symbol, SecurityId securityId, long id, CancellationToken cancellationToken)
	{
		return _feedClient.SubscribeTrades(symbol, new(id, true, securityId, MessageTypes.Execution, DataType.Ticks), cancellationToken);
	}

	public ValueTask UnSubscribeTrades(string symbol, SecurityId securityId, long id, CancellationToken cancellationToken)
	{
		return _feedClient.UnSubscribeTrades(symbol, new(id, false, securityId, MessageTypes.Execution, DataType.Ticks), cancellationToken);
	}

	public ValueTask RequestOrderBook(SecurityId securityId, int depth, long id, CancellationToken cancellationToken)
	{
		throw new NotSupportedException();
	}

	public ValueTask SubscribeOrderBook(string symbol, SecurityId securityId, int depth, long id, CancellationToken cancellationToken)
	{
		return _feedClient.SubscribeOrderBook(symbol, depth, new(id, true, securityId, MessageTypes.QuoteChange, depth), cancellationToken);
	}

	public ValueTask UnSubscribeOrderBook(string symbol, SecurityId securityId, int depth, long id, CancellationToken cancellationToken)
	{
		return _feedClient.UnSubscribeOrderBook(symbol, depth, new(id, false, securityId, MessageTypes.QuoteChange, depth), cancellationToken);
	}

	public ValueTask RequestCandles(string symbol, SecurityId securityId, TimeSpan timeFrame, long id, long? from, long? to, CancellationToken cancellationToken)
	{
		return _feedClient.RequestCandles(symbol, timeFrame, from, to, new(id, null, securityId, MessageTypes.CandleTimeFrame, timeFrame), cancellationToken);
	}

	public ValueTask SubscribeCandles(string symbol, SecurityId securityId, TimeSpan timeFrame, long id, CancellationToken cancellationToken)
	{
		return _feedClient.SubscribeCandles(symbol, timeFrame, new(id, true, securityId, MessageTypes.CandleTimeFrame, timeFrame), cancellationToken);
	}

	public ValueTask UnSubscribeCandles(string symbol, SecurityId securityId, TimeSpan timeFrame, long id, CancellationToken cancellationToken)
	{
		return _feedClient.UnSubscribeCandles(symbol, timeFrame, new(id, false, securityId, MessageTypes.CandleTimeFrame, timeFrame), cancellationToken);
	}

	public ValueTask SubscribeAccounts(long id, CancellationToken cancellationToken)
	{
		return _accountClient.SubscribeAccounts(new(id, true, default, MessageTypes.PortfolioLookup), cancellationToken);
	}

	public ValueTask UnSubscribeAccounts(long id, CancellationToken cancellationToken)
	{
		return _accountClient.UnSubscribeAccounts(new(id, false, default, MessageTypes.PortfolioLookup), cancellationToken);
	}

	public ValueTask SubscribeOrders(long id, CancellationToken cancellationToken)
	{
		return _accountClient.SubscribeOrders(new(id, true, default, MessageTypes.OrderStatus), cancellationToken);
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
			//case 1:
			//	_mbpClient.Pong(pingId);
			//	break;
			case 2:
				await _accountClient.Pong(pingId, cancellationToken);
				break;
			default:
				this.AddErrorLog("Unk ping feed {0}.", feedId);
				break;
		}
	}
}