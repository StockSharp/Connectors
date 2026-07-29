namespace StockSharp.Transaq.Native.Commands;

class RequestMaxBuySellMessage : BaseCommandMessage
{
	public RequestMaxBuySellMessage()
		: base(ApiCommands.GetMaxBuySell)
	{
	}

	public string Client { get; set; }
	public string Union { get; set; }

	public int Market { get; set; }
	public string SecCode { get; set; }
}