namespace StockSharp.Transaq.Native.Commands;

class RequestLeverageControlMessage : BaseCommandMessage
{
	public RequestLeverageControlMessage() : base(ApiCommands.GetLeverageControl)
	{
		SecIds = [];
	}

	public string Client { get; set; }
	public List<(string code, string board, int id)> SecIds { get; }
}