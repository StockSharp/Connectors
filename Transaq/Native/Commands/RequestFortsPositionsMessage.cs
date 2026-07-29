namespace StockSharp.Transaq.Native.Commands;

class RequestFortsPositionsMessage : BaseCommandMessage
{
	public RequestFortsPositionsMessage() : base(ApiCommands.GetFortsPositions)
	{
	}

	public string Client { get; set; }
}