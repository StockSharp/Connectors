namespace StockSharp.LATOKEN.Native;

using System.Net.WebSockets;

class PusherClient : BaseLogReceiver
{
	// to get readable name after obfuscation
	public override string Name => nameof(LATOKEN) + "_" + nameof(PusherClient);

	public event Func<Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<string, string, OrderBook, bool, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<Trade, CancellationToken, ValueTask> NewTrade;
	public event Func<Order, CancellationToken, ValueTask> OrderChanged;
	public event Func<Balance, CancellationToken, ValueTask> BalanceChanged;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;
	//public event Action<string> TradesSubscribed;
	//public event Action<string> OrderBooksSubscribed;

	private readonly WebSocketClient _client;
	private readonly Authenticator _authenticator;

	public PusherClient(Authenticator authenticator, WorkingTime workingTime)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

		_client = new(
			"wss://api.latoken.com/stomp",
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

		if (_authenticator.CanSign)
			_client.Init += OnInit;
	}

	protected override void DisposeManaged()
	{
		if (_authenticator.CanSign)
			_client.Init -= OnInit;

		_client.Dispose();
		base.DisposeManaged();
	}

	private void OnInit(ClientWebSocket s)
	{
		var signData = ((long)TimeHelper.UnixNowS).ToString();

		s.Options.SetRequestHeader("X-LA-APIKEY", _authenticator.Key.UnSecure());
		s.Options.SetRequestHeader("X-LA-DIGEST", Authenticator.HashAlgo);
		s.Options.SetRequestHeader("X-LA-SIGNATURE", _authenticator.MakeSign(signData));
		s.Options.SetRequestHeader("X-LA-SIGDATA", signData);
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
		var obj = msg.AsObject();

		var method = (string)obj.method;
		var data = (JToken)obj.data;

		switch (method)
		{
			case Channels.Ticker:
				await (TickerChanged?.Invoke(data.DeserializeObject<Ticker>(), cancellationToken) ?? default);
				break;
			case Channels.Book:
				await (OrderBookChanged?.Invoke(default, default, data.DeserializeObject<OrderBook>(), true, cancellationToken) ?? default);
				break;
			case Channels.Trade:
				await (NewTrade?.Invoke(data.DeserializeObject<Trade>(), cancellationToken) ?? default);
				break;
			case Channels.Order:
				await (OrderChanged?.Invoke(data.DeserializeObject<Order>(), cancellationToken) ?? default);
				break;
			case Channels.Account:
				await (BalanceChanged?.Invoke(data.DeserializeObject<Balance>(), cancellationToken) ?? default);
				break;
			default:
				this.AddErrorLog(LocalizedStrings.UnknownEvent, method);
				break;
		}
	}

	private static class Commands
	{
		public const string Subscribe = "subscribe";
		public const string Unsubscribe = "unsubscribe";
	}

	private static class Channels
	{
		public const string Ticker = "ticker";
		public const string Book = "book";
		public const string Trade = "trade";
		public const string Order = "order";
		public const string Account = "account";
	}

	public ValueTask SubscribeTicker(string code, string board, CancellationToken cancellationToken)
	{
		return Process(Commands.Subscribe, Channels.Ticker, code, board, cancellationToken);
	}

	public ValueTask UnSubscribeTicker(string code, string board, CancellationToken cancellationToken)
	{
		return default;
		//return Process(Commands.Unsubscribe, Channels.Ticker, code, board, cancellationToken);
	}

	public ValueTask SubscribeTrades(string code, string board, CancellationToken cancellationToken)
	{
		return Process(Commands.Subscribe, Channels.Trade, code, board, cancellationToken);
	}

	public ValueTask UnSubscribeTrades(string code, string board, CancellationToken cancellationToken)
	{
		return default;
		//return Process(Commands.Unsubscribe, Channels.Trade, code, board, cancellationToken);
	}

	public ValueTask SubscribeOrderBook(string code, string board, CancellationToken cancellationToken)
	{
		return Process(Commands.Subscribe, Channels.Book, code, board, cancellationToken);
	}

	public ValueTask UnSubscribeOrderBook(string code, string board, CancellationToken cancellationToken)
	{
		return default;
		//return Process(Commands.Unsubscribe, Channels.Book, code, board, cancellationToken);
	}

	public ValueTask SubscribeOrders(CancellationToken cancellationToken)
	{
		return default;
		//return Process(Commands.Subscribe, Channels.Book, code, board, cancellationToken);
	}

	public ValueTask UnSubscribeOrders(CancellationToken cancellationToken)
	{
		return default;
		//return Process(Commands.Unsubscribe, Channels.Book, code, board, cancellationToken);
	}

	public ValueTask SubscribeAccounts(CancellationToken cancellationToken)
	{
		return default;
		//return Process(Commands.Subscribe, Channels.Book, code, board, cancellationToken);
	}

	public ValueTask UnSubscribeAccounts(CancellationToken cancellationToken)
	{
		return default;
		//return Process(Commands.Unsubscribe, Channels.Book, code, board, cancellationToken);
	}

	private ValueTask Process(string method, string channel, string code, string board, CancellationToken cancellationToken)
	{
		return _client.SendAsync($"/v1/{channel}/{code}/{board}", cancellationToken);
	}
}