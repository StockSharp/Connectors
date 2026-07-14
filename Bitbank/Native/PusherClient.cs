namespace StockSharp.Bitbank.Native;

using Ecng.ComponentModel;

class PusherClient : BaseLogReceiver
{
	// to get readable name after obfuscation
	public override string Name => nameof(Bitbank) + "_" + nameof(PusherClient);

	public event Func<string, Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<string, Trade, CancellationToken, ValueTask> NewTrade;
	public event Func<string, OrderBook, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly WebSocketClient _client;

	public PusherClient(WorkingTime workingTime)
	{
		_client = new(
			"wss://stream.bitbank.cc/socket.io/?EIO=4&transport=websocket",
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

		_client.PostConnect += OnPostConnect;
	}

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	private async ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		await _client.SendAsync("40", cancellationToken);
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
		var json = msg.AsString();

		if (long.TryParse(json, out _))
			return;

		var skip = 0;

		while (json[skip].IsDigit())
			skip++;

		dynamic obj = json[skip..].DeserializeObject<object>();

		if (obj is JArray array && array.Count > 1)
			obj = array[1];

		var channel = (string)obj.room_name;

		if (channel is null)
			return;

		var message = obj.message;
		var data = message.data;

		if (channel.StartsWithIgnoreCase(Channels.DepthWhole))
		{
			if (OrderBookChanged is { } handler)
				await handler(channel.Remove(Channels.DepthWhole, true), ((JToken)data).DeserializeObject<OrderBook>(), cancellationToken);
		}
		else if (channel.StartsWithIgnoreCase(Channels.Deals))
		{
			if (NewTrade is { } handler)
			{
				var symbol = channel.Remove(Channels.Deals, true);
				var trades = ((JToken)data.transactions).DeserializeObject<Trade[]>();

				foreach (var trade in trades)
				{
					await handler(symbol, trade, cancellationToken);
				}
			}
		}
		else if (channel.StartsWithIgnoreCase(Channels.Ticker))
		{
			if (TickerChanged is { } handler)
				await handler(channel.Remove(Channels.Ticker, true), ((JToken)data).DeserializeObject<Ticker>(), cancellationToken);
		}
		else
			this.AddErrorLog(LocalizedStrings.UnknownEvent, channel);
	}

	private static class Channels
	{
		public const string Ticker = "ticker_";
		public const string Deals = "transactions_";
		public const string DepthWhole = "depth_whole_";
	}

	public ValueTask SubscribeTickerAsync(string currency, CancellationToken cancellationToken)
	{
		return ProcessAsync(true, Channels.Ticker + currency, cancellationToken);
	}

	public ValueTask UnSubscribeTickerAsync(string currency, CancellationToken cancellationToken)
	{
		return ProcessAsync(false, Channels.Ticker + currency, cancellationToken);
	}

	public ValueTask SubscribeTradesAsync(string currency, CancellationToken cancellationToken)
	{
		return ProcessAsync(true, Channels.Deals + currency, cancellationToken);
	}

	public ValueTask UnSubscribeTradesAsync(string currency, CancellationToken cancellationToken)
	{
		return ProcessAsync(false, Channels.Deals + currency, cancellationToken);
	}

	public ValueTask SubscribeOrderBookAsync(string currency, CancellationToken cancellationToken)
	{
		return ProcessAsync(true, Channels.DepthWhole + currency, cancellationToken);
	}

	public ValueTask UnSubscribeOrderBookAsync(string currency, CancellationToken cancellationToken)
	{
		return ProcessAsync(false, Channels.DepthWhole + currency, cancellationToken);
	}

	private ValueTask ProcessAsync(bool isSubscribe, string channel, CancellationToken cancellationToken)
	{
		if (channel.IsEmpty())
			throw new ArgumentNullException(nameof(channel));

		if (isSubscribe)
			return _client.SendAsync($"42[\"join-room\",\"{channel}\"]", cancellationToken);
		else
			return _client.SendAsync(new[] { "leave-room", channel }, cancellationToken);
	}
}
