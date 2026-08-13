namespace StockSharp.Breeze.Native;

sealed class BreezeOrderClient : BreezeSocketClient
{
	public BreezeOrderClient(string endpoint, string user, string token, int reconnectAttempts, WorkingTime workingTime)
		: base(endpoint, user, token, reconnectAttempts, workingTime) { }

	public override string Name => nameof(Breeze) + "_" + nameof(BreezeOrderClient);
	public event Func<BreezeOrderUpdate, CancellationToken, ValueTask> OrderReceived;

	protected override ValueTask ProcessEvent(string message, CancellationToken cancellationToken)
	{
		if (!BreezeSocketCodec.GetEvent(message).EqualsIgnoreCase("order") || OrderReceived is not { } handler)
			return default;
		return handler.InvokeAsync(BreezeSocketCodec.ReadOrder(message), cancellationToken);
	}
}
