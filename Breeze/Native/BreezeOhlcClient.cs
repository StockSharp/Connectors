namespace StockSharp.Breeze.Native;

sealed class BreezeOhlcClient : BreezeSocketClient
{
	private const string _url = "wss://breezeapi.icicidirect.com/ohlcvstream/?EIO=4&transport=websocket";
	private static readonly HashSet<string> _events = new(StringComparer.OrdinalIgnoreCase) { "1SEC", "1MIN", "5MIN", "30MIN" };

	public BreezeOhlcClient(string user, string token, int reconnectAttempts, WorkingTime workingTime)
		: base(_url, user, token, reconnectAttempts, workingTime) { }

	public override string Name => nameof(Breeze) + "_" + nameof(BreezeOhlcClient);
	public event Func<BreezeStreamCandle, CancellationToken, ValueTask> CandleReceived;

	public ValueTask Subscribe(BreezeInstrument instrument, CancellationToken cancellationToken) => AddRoom(instrument.ToStreamToken(false), cancellationToken);
	public ValueTask Unsubscribe(BreezeInstrument instrument, CancellationToken cancellationToken) => RemoveRoom(instrument.ToStreamToken(false), cancellationToken);

	protected override ValueTask ProcessEvent(string message, CancellationToken cancellationToken)
	{
		var eventName = BreezeSocketCodec.GetEvent(message);
		if (!_events.Contains(eventName) || CandleReceived is not { } handler)
			return default;
		return handler(BreezeSocketCodec.ReadCandle(message, eventName), cancellationToken);
	}
}
