namespace StockSharp.Transaq.Native.Commands;

abstract class NewBaseOrderMessage(string commandId) : BaseCommandMessage(commandId)
{
    public int SecId { get; set; }
	public string Board { get; set; }
	public string SecCode { get; set; }
	public string Client { get; set; }
	public string Union { get; set; }
	public double Price { get; set; }
	public int Quantity { get; set; }
	public BuySells BuySell { get; set; }
	public string BrokerRef { get; set; }
}

class NewOrderMessage : NewBaseOrderMessage
{
	public NewOrderMessage() : base(ApiCommands.NewOrder)
	{
	}

	public int Hidden { get; set; }
	public bool ByMarket { get; set; }
	public bool UseCredit { get; set; }
	public bool NoSplit { get; set; }
	public DateTime? ExpDate { get; set; }
	public NewOrderUnfilleds Unfilled { get; set; }
}

enum NewOrderUnfilleds
{
	PutInQueue,
	CancelBalance,
	ImmOrCancel
}