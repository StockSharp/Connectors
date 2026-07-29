namespace StockSharp.Transaq.Native.Commands;

class RequestServTimeDifferenceMessage : BaseCommandMessage
{
	public RequestServTimeDifferenceMessage() : base(ApiCommands.GetServTimeDifference)
	{
	}
}