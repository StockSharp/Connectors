namespace StockSharp.Transaq.Native.Commands;

class RequestNewsBodyMessage : BaseCommandMessage
{
	public RequestNewsBodyMessage() : base(ApiCommands.GetNewsBody)
	{
	}

	public int NewsId { get; set; }
}