namespace StockSharp.Transaq.Native.Responses;

class MarketsResponse : BaseResponse
{
	public IEnumerable<Market> Markets { get; set; }
}

class Market
{
	public int Id { get; set; }
	public string Name { get; set; }
}