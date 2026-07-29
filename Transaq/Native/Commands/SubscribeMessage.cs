namespace StockSharp.Transaq.Native.Commands;

class SubscribeMessage : BaseCommandMessage
{
	public SubscribeMessage() : base(ApiCommands.Subscribe)
	{
		AllTrades = [];
		Quotations = [];
		Quotes = [];
	}

	public List<(string secCode, string board, int nativeId)> AllTrades { get; }
	public List<(string secCode, string board, int nativeId)> Quotations { get; }
	public List<(string secCode, string board, int nativeId)> Quotes { get; }
}