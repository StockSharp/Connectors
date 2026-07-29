namespace StockSharp.Transaq.Native.Responses;

class PitsResponse : BaseResponse
{
	public IEnumerable<Pit> Pits { get; set; }
}

class Pit
{
	public string SecCode { get; set; }
	public string Board { get; set; }
	public string Market { get; set; }
	public int Decimals { get; set; }
	public double? MinStep { get; set; }
	public int LotSize { get; set; }
	public double? PointCost { get; set; }
	public string CurrencyId { get; set; }
}
