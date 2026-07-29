namespace StockSharp.Transaq.Native.Commands;

class RequestConnectorVersionMessage : BaseCommandMessage
{
	public RequestConnectorVersionMessage() : base(ApiCommands.GetConnectorVersion)
	{
	}
}