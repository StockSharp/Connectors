namespace StockSharp.Transaq.Native.Commands;

class SubscribeTicksMessage : BaseCommandMessage
{
	public SubscribeTicksMessage() : base(ApiCommands.SubscribeTicks)
	{
		Items = [];
	}

	public bool Filter { get; set; }
	public List<SubscribeTicksSecurity> Items { get; }
}

class SubscribeTicksSecurity
{
	public string Board { get; set; }
	public string SecCode { get; set; }
	public int TradeNo { get; set; }
	public int SecId { get; set; }
}