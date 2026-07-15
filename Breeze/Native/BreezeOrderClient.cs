namespace StockSharp.Breeze.Native;

sealed class BreezeOrderClient : BreezeSocketClient
{
	private const string _url = "wss://livefeeds.icicidirect.com/socket.io/?EIO=4&transport=websocket";

	public BreezeOrderClient(string user, string token, int reconnectAttempts, WorkingTime workingTime)
		: base(_url, user, token, reconnectAttempts, workingTime) { }

	public override string Name => nameof(Breeze) + "_" + nameof(BreezeOrderClient);
	public event Func<BreezeOrderUpdate, CancellationToken, ValueTask> OrderReceived;

	protected override ValueTask ProcessEvent(string message, CancellationToken cancellationToken)
	{
		if (!BreezeSocketCodec.GetEvent(message).EqualsIgnoreCase("order") || OrderReceived is not { } handler)
			return default;
		return handler(BreezeSocketCodec.ReadOrder(message), cancellationToken);
	}
}
