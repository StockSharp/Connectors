namespace StockSharp.Transaq.Native.Commands;

class RequestOldNewsMessage : BaseCommandMessage
{
	public RequestOldNewsMessage() : base(ApiCommands.GetOldNews)
	{
	}

	public int Count { get; set; }
}