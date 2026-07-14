namespace StockSharp.CoinCap.Native;

abstract class BasePusherClient<TData> : BaseLogReceiver
{
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly WebSocketClient _client;
	private readonly string _url;

	protected BasePusherClient(string url, int attemptsCount, WorkingTime workingTime)
	{
		if (url.IsEmpty())
			throw new ArgumentNullException(nameof(url));

		_url = url;

		_client = new(
			$"wss://ws.coincap.io/{_url}",
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
	}

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
		var recv = msg.AsString();

		if (recv.StartsWithIgnoreCase("invalid exchange"))
		{
			if (Error is { } handler)
				await handler(new InvalidOperationException(recv), cancellationToken);
			return;
		}

		await OnProcess(recv.DeserializeObject<TData>(), cancellationToken);
	}

	protected abstract ValueTask OnProcess(TData recv, CancellationToken cancellationToken);
}

class TradesPusherClient(string exchange, int attemptsCount, WorkingTime workingTime) : BasePusherClient<Trade>($"trades/{exchange}", attemptsCount, workingTime)
{
	// to get readable name after obfuscation
	public override string Name => nameof(CoinCap) + "_" + nameof(TradesPusherClient);

	public event Func<Trade, CancellationToken, ValueTask> NewTrade;

	protected override ValueTask OnProcess(Trade trade, CancellationToken cancellationToken)
	{
		if (NewTrade is { } handler)
			return handler(trade, cancellationToken);
		return default;
	}
}

class PricesPusherClient(IEnumerable<string> assets, int attemptsCount, WorkingTime workingTime) : BasePusherClient<IDictionary<string, double>>($"prices?assets={assets.JoinComma()}", attemptsCount, workingTime)
{
	// to get readable name after obfuscation
	public override string Name => nameof(CoinCap) + "_" + nameof(PricesPusherClient);

	public event Func<IDictionary<string, double>, CancellationToken, ValueTask> PricesChanged;

	protected override ValueTask OnProcess(IDictionary<string, double> recv, CancellationToken cancellationToken)
	{
		if (PricesChanged is { } handler)
			return handler(recv, cancellationToken);
		return default;
	}
}