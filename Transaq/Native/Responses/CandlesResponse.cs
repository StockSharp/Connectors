namespace StockSharp.Transaq.Native.Responses;

class CandlesResponse : BaseResponse
{
	public int SecId { get; set; }
	public string Board { get; set; }
	public string SecCode { get; set; }
	public int Period { get; set; }
	public CandleResponseStatus Status { get; set; }

	public TransaqCandle[] Candles { get; set; }
}

enum CandleResponseStatus
{
	Finished,
	Done,
	Continue,
	NotAvailable
}

class TransaqCandle
{
	public DateTime Date { get; set; }
	public double Open { get; set; }
	public double High { get; set; }
	public double Low { get; set; }
	public double Close { get; set; }
	public double? Volume { get; set; }
	public double? Oi { get; set; }
}