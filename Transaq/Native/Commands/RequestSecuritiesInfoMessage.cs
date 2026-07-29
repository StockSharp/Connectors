namespace StockSharp.Transaq.Native.Commands;

class RequestSecuritiesInfoMessage : BaseCommandMessage
{
	public RequestSecuritiesInfoMessage() : base(ApiCommands.GetSecuritiesInfo)
	{
	}

	public int Market { get; set; }
	public string SecCode { get; set; }
}