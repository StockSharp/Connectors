namespace StockSharp.Transaq.Native.Commands;

class CancelOrderMessage : BaseCommandMessage
{
	public CancelOrderMessage() : base(ApiCommands.CancelOrder)
	{
	}

	public long TransactionId { get; set; }
}