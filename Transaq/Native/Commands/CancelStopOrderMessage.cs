namespace StockSharp.Transaq.Native.Commands;

class CancelStopOrderMessage : CancelOrderMessage
{
	public CancelStopOrderMessage()
	{
		Id = ApiCommands.CancelStopOrder;
	}
}