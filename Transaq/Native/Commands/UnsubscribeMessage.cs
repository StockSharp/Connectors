namespace StockSharp.Transaq.Native.Commands;

class UnsubscribeMessage : SubscribeMessage
{
	public UnsubscribeMessage()
	{
		Id = ApiCommands.Unsubscribe;
	}
}