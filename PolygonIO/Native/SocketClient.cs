namespace StockSharp.PolygonIO.Native;

class SocketClient : BaseLogReceiver
{
	private static class Actions
	{
		public const string Auth = "auth";
		public const string Subscribe = "subscribe";
		public const string Unsubscribe = "unsubscribe";
	}

	private static class EventTypes
	{
		public const string Bar1Sec = "A";
		public const string Bar1Min = "AM";
		public const string Trade = "T";
		public const string Quote = "Q";
		public const string Fair = "FVM";
	}

	// to get readable name after obfuscation
	public override string Name => nameof(PolygonIO) + "_" + nameof(SocketClient);

	public event Func<SocketTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<SocketQuote, CancellationToken, ValueTask> QuoteReceived;
	public event Func<SocketBar, CancellationToken, ValueTask> BarReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly SecureString _token;
	
	private readonly WebSocketClient _client;

	public SocketClient(string address, SecureString token, WorkingTime workingTime)
	{
		_token = token;

		_client = new(
			address.ThrowIfEmpty(nameof(address)),
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

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var obj = msg.AsObject();
		var token = (JToken)obj;

		async ValueTask process(JToken item)
		{
			dynamic obj = item;
			var ev = (string)obj.ev;

			switch (ev)
			{
				case "status":
				{
					var status = (string)obj.status;

					switch (status.ToLowerInvariant())
					{
						case "connected":
							await Send(Actions.Auth, _token.UnSecure(), cancellationToken);
							break;
						case "auth_success":
							this.AddInfoLog(status);
							break;
						case "success":
							break;
						default:
							this.AddErrorLog(LocalizedStrings.UnknownEvent, status);
							break;
					}
					break;
				}
				case EventTypes.Bar1Min:
				case EventTypes.Bar1Sec:
					if (BarReceived is { } barHandler)
						await barHandler(item.DeserializeObject<SocketBar>(), cancellationToken);
					break;
				case EventTypes.Trade:
					if (TradeReceived is { } tradeHandler)
						await tradeHandler(item.DeserializeObject<SocketTrade>(), cancellationToken);
					break;
				case EventTypes.Quote:
					if (QuoteReceived is { } quoteHandler)
						await quoteHandler(item.DeserializeObject<SocketQuote>(), cancellationToken);
					break;
				default:
					this.AddErrorLog(LocalizedStrings.UnknownEvent, ev);
					break;
			}
		}

		if (token.Type == JTokenType.Array)
		{
			foreach (var item in (JArray)token)
				await process(item);
		}
		else
			await process(token);
	}

	protected override void DisposeManaged()
	{
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);
		return _client.ConnectAsync(cancellationToken);
	}

	public void Disconnect()
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		_client.Disconnect();
	}

	public ValueTask SubscribeTrades(string symbol, CancellationToken cancellationToken)
		=> Send(Actions.Subscribe, $"{EventTypes.Trade}.{symbol}", cancellationToken);

	public ValueTask SubscribeQuotes(string symbol, CancellationToken cancellationToken)
		=> Send(Actions.Subscribe, $"{EventTypes.Quote}.{symbol}", cancellationToken);

	public ValueTask SubscribeBarsMin(string symbol, CancellationToken cancellationToken)
		=> Send(Actions.Subscribe, $"{EventTypes.Bar1Min}.{symbol}", cancellationToken);

	public ValueTask SubscribeBarsSec(string symbol, CancellationToken cancellationToken)
	=> Send(Actions.Subscribe, $"{EventTypes.Bar1Sec}.{symbol}", cancellationToken);

	public ValueTask UnSubscribeTrades(string symbol, CancellationToken cancellationToken)
		=> Send(Actions.Unsubscribe, $"{EventTypes.Trade}.{symbol}", cancellationToken);

	public ValueTask UnSubscribeQuotes(string symbol, CancellationToken cancellationToken)
		=> Send(Actions.Unsubscribe, $"{EventTypes.Quote}.{symbol}", cancellationToken);

	public ValueTask UnSubscribeBarsMin(string symbol, CancellationToken cancellationToken)
		=> Send(Actions.Unsubscribe, $"{EventTypes.Bar1Min}.{symbol}", cancellationToken);

	public ValueTask UnSubscribeBarsSec(string symbol, CancellationToken cancellationToken)
	=> Send(Actions.Unsubscribe, $"{EventTypes.Bar1Sec}.{symbol}", cancellationToken);

	private ValueTask Send(string action, string @params, CancellationToken cancellationToken)
	{
		return _client.SendAsync(new
		{
			action,
			@params,
		}, cancellationToken);
	}
}