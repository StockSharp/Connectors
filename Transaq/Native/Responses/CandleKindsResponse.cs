namespace StockSharp.Transaq.Native.Responses;

class CandleKindsResponse : BaseResponse
{
	public IEnumerable<CandleKind> Kinds { get; set; }
}

class CandleKind
{
	public int Id { get; set; }
	public int Period { get; set; }
	public string Name { get; set; }
}