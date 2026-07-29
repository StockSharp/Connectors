namespace StockSharp.Transaq.Native.Commands;

class DisconnectMessage : BaseCommandMessage
{
	public DisconnectMessage() : base(ApiCommands.Disconnect)
	{
	}
}