namespace StockSharp.Breeze.Native;

sealed class BreezeMarketDataClient : BreezeSocketClient
{
	private const string _url = "wss://livestream.icicidirect.com/socket.io/?EIO=4&transport=websocket";
	private readonly SynchronizedDictionary<string, BreezeInstrumentKinds> _instruments = new(StringComparer.OrdinalIgnoreCase);

	public BreezeMarketDataClient(string user, string token, int reconnectAttempts, WorkingTime workingTime)
		: base(_url, user, token, reconnectAttempts, workingTime) { }

	public override string Name => nameof(Breeze) + "_" + nameof(BreezeMarketDataClient);
	public event Func<BreezeMarketTick, CancellationToken, ValueTask> TickReceived;
	public event Func<BreezeDepthUpdate, CancellationToken, ValueTask> DepthReceived;

	public async ValueTask SetSubscription(BreezeInstrument instrument, bool quote, bool depth, CancellationToken cancellationToken)
	{
		_instruments[instrument.Token] = instrument.Kind;
		if (quote) await AddRoom(instrument.ToStreamToken(false), cancellationToken); else await RemoveRoom(instrument.ToStreamToken(false), cancellationToken);
		if (depth) await AddRoom(instrument.ToStreamToken(true), cancellationToken); else await RemoveRoom(instrument.ToStreamToken(true), cancellationToken);
		if (!quote && !depth) _instruments.Remove(instrument.Token);
	}

	protected override async ValueTask ProcessEvent(string message, CancellationToken cancellationToken)
	{
		if (!BreezeSocketCodec.GetEvent(message).EqualsIgnoreCase("stock"))
			return;
		if (BreezeSocketCodec.IsDepth(message))
		{
			if (DepthReceived is { } depthHandler)
				await depthHandler(BreezeSocketCodec.ReadDepth(message), cancellationToken);
		}
		else if (TickReceived is { } tickHandler)
		{
			await tickHandler(BreezeSocketCodec.ReadMarketTick(message,
				token => _instruments.TryGetValue(token, out var kind) && kind != BreezeInstrumentKinds.Equity), cancellationToken);
		}
	}
}
