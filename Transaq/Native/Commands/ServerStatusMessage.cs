namespace StockSharp.Transaq.Native.Commands;

class ServerStatusMessage : BaseCommandMessage
{
	public ServerStatusMessage() : base(ApiCommands.ServerStatus)
	{
	}
}