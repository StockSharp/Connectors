namespace StockSharp.Transaq.Native.Commands;

class RequestServerIdMessage : BaseCommandMessage
{
	public RequestServerIdMessage() : base(ApiCommands.GetServerId)
	{
	}
}