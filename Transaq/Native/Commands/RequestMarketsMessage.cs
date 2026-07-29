namespace StockSharp.Transaq.Native.Commands;

class RequestMarketsMessage : BaseCommandMessage
{
	public RequestMarketsMessage() : base(ApiCommands.GetMarkets)
	{
	}
}