namespace StockSharp.Digifinex.Native;

using Ecng.IO.Compression;

class PusherClient : BaseLogReceiver
{
	// to get readable name after obfuscation
	public override string Name => nameof(Digifinex) + "_" + nameof(PusherClient);

	public event Func<string, IEnumerable<Trade>, CancellationToken, ValueTask> NewTrades;
	public event Func<string, OrderBook, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<Ticker, CancellationToken, ValueTask> TickerReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;
	public event Func<long, Exception, CancellationToken, ValueTask> SubscriptionResult;

	private readonly SynchronizedDictionary<long, MessageTypes> _requests = new();
	private readonly WebSocketClient _client;

	private DateTime? _nextPing;

	public PusherClient(string domain, int attemptsCount, WorkingTime workingTime)
	{
		_client = new(
			$"wss://openapi.digifinex.{domain}/ws/v1/",
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
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
		};

		_client.PreProcess2 += ClientOnPreProcess;
	}

	private static int ClientOnPreProcess(ReadOnlyMemory<byte> source, Memory<byte> destination)
	{
		// https://stackoverflow.com/a/21544269/1296971
		const int offset = 2;

		return source.Span[offset..].UnDeflate(destination.Span);
	}

	protected override void DisposeManaged()
	{
		_client.PreProcess2 -= ClientOnPreProcess;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);

		_nextPing = null;
		return _client.ConnectAsync(cancellationToken);
	}

	public void Disconnect()
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		_client.Disconnect();
	}

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var obj = msg.AsObject();
		var id = (long?)obj.id;
		var error = obj.error == null ? null : new InvalidOperationException(obj.error.ToString());

		if (id == null)
		{
			if (error != null)
			{
				if (Error is { } errorHandler)
					await errorHandler(error, cancellationToken);
				return;
			}

			var method = (string)obj.method;

			switch (method)
			{
				case "trades.update":
				{
					var value = (JToken)obj.@params[1];
					var symbol = (string)obj.@params[2];

					if (NewTrades is { } tradesHandler)
						await tradesHandler(symbol, value.DeserializeObject<IEnumerable<Trade>>(), cancellationToken);
					break;
				}

				case "depth.update":
				{
					var value = (JToken)obj.@params[1];
					var symbol = (string)obj.@params[2];

					if (OrderBookChanged is { } bookHandler)
						await bookHandler(symbol, value.DeserializeObject<OrderBook>(), cancellationToken);
					break;
				}

				case "ticker.update":
				{
					var tickers = ((JArray)obj.@params).DeserializeObject<IEnumerable<Ticker>>();

					foreach (var ticker in tickers)
					{
						if (TickerReceived is { } tickerHandler)
							await tickerHandler(ticker, cancellationToken);
					}

					break;
				}

				default:
					this.AddErrorLog(LocalizedStrings.UnknownEvent, method);
					return;
			}
		}
		else
		{
			if (!_requests.TryGetValue(id.Value, out _))
			{
				this.AddErrorLog(LocalizedStrings.UnknownEvent, id.Value);
				return;
			}

			if (SubscriptionResult is { } subHandler)
				await subHandler(id.Value, error, cancellationToken);
		}
	}

	private static class Channels
	{
		public const string Depth = "depth.{0}";
		public const string Trades = "trades.{0}";
		public const string Ticker = "ticker.{0}";
	}

	private static class Methods
	{
		public const string Subscribe = "subscribe";
		public const string Unsubscribe = "unsubscribe";
	}

	public ValueTask SubscribeTrades(long id, string symbol, CancellationToken cancellationToken)
	{
		return Process(id, id, Channels.Trades.Put(Methods.Subscribe), MessageTypes.MarketData, cancellationToken, symbol);
	}

	public ValueTask UnSubscribeTrades(long originTransId, long id, string symbol, CancellationToken cancellationToken)
	{
		return Process(-originTransId, id, Channels.Trades.Put(Methods.Unsubscribe), MessageTypes.MarketData, cancellationToken, symbol);
	}

	public ValueTask SubscribeTicker(long id, string symbol, CancellationToken cancellationToken)
	{
		return Process(id, id, Channels.Ticker.Put(Methods.Subscribe), MessageTypes.MarketData, cancellationToken, symbol);
	}

	public ValueTask UnSubscribeTicker(long originTransId, long id, string symbol, CancellationToken cancellationToken)
	{
		return Process(-originTransId, id, Channels.Ticker.Put(Methods.Unsubscribe), MessageTypes.MarketData, cancellationToken, symbol);
	}

	public ValueTask SubscribeOrderBook(long id, string symbol, CancellationToken cancellationToken)
	{
		return Process(id, id, Channels.Depth.Put(Methods.Subscribe), MessageTypes.MarketData, cancellationToken, symbol);
	}

	public ValueTask UnSubscribeOrderBook(long originTransId, long id, string symbol, CancellationToken cancellationToken)
	{
		return Process(-originTransId, id, Channels.Depth.Put(Methods.Unsubscribe), MessageTypes.MarketData, cancellationToken, symbol);
	}

	public void ProcessPing()
	{
		if (_nextPing != null && DateTime.UtcNow < _nextPing.Value)
			return;

		//Process(null, "server.ping");
		_nextPing = DateTime.UtcNow.AddSeconds(30);
	}

	private ValueTask Process(long subId, long? id, string method, MessageTypes? type, CancellationToken cancellationToken, params string[] @params)
	{
		if (method.IsEmpty())
			throw new ArgumentNullException(nameof(method));

		//if (@params.IsEmpty())
		//	throw new ArgumentNullException(nameof(channel));

		if (id != null && type != null)
			_requests.Add(id.Value, type.Value);

		return _client.SendAsync(new
		{
			id,
			method,
			@params,
		}, cancellationToken, subId);
	}
}