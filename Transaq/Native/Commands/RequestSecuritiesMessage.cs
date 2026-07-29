namespace StockSharp.Transaq.Native.Commands;

class RequestSecuritiesMessage : BaseCommandMessage
{
	public RequestSecuritiesMessage() : base(ApiCommands.GetSecurities)
	{
	}
}