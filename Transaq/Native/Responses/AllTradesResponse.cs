namespace StockSharp.Transaq.Native.Responses;

class AllTradesResponse : BaseResponse
{
	public IEnumerable<Tick> AllTrades { get; set; }
}