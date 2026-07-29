namespace StockSharp.Transaq.Native.Responses;

class TicksResponse : BaseResponse
{
	public IEnumerable<Tick> Ticks { get; set; }
}

class Tick
{
	public int SecId { get; set; }
	public string Board { get; set; }
	public string SecCode { get; set; }
	public long TradeNo { get; set; }
	public DateTime TradeTime { get; set; }
	public double Price { get; set; }
	public int Quantity { get; set; }
	public TicksPeriods? Period { get; set; }
	public BuySells BuySell { get; set; }
	public int OpenInterest { get; set; }
}

enum TicksPeriods
{
	O,
	N,
	C
}

enum BuySells
{
	B,
	S
}