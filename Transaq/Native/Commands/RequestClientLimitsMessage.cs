namespace StockSharp.Transaq.Native.Commands;

class RequestClientLimitsMessage : RequestFortsPositionsMessage
{
	public RequestClientLimitsMessage()
	{
		Id = ApiCommands.GetClientLimits;
	}
}