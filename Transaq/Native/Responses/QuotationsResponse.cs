namespace StockSharp.Transaq.Native.Responses;

class QuotationsResponse : BaseResponse
{
	public IEnumerable<Quotation> Quotations { get; set; }
}

class Quotation
{
	public int SecId { get; set; }
	public string Board { get; set; }
	public string SecCode { get; set; }
	public double? AccruedIntValue { get; set; }
	public double? Open { get; set; }
	public double? WAPrice { get; set; }
	public int? BestBidVolume { get; set; }
	public int? BidsVolume { get; set; }
	public int? BidsCount { get; set; }
	public int? BestAskVolume { get; set; }
	public int? AsksVolume { get; set; }
	public double? BestBidPrice { get; set; }
	public double? BestAskPrice { get; set; }
	public int? AsksCount { get; set; }
	public int? TradesCount { get; set; }
	public int? VolToday { get; set; }
	public int? OpenInterest { get; set; }
	public int? DeltaPositions { get; set; }
	public double? LastTradePrice { get; set; }
	public int? LastTradeVolume { get; set; }
	public DateTime? LastTradeTime { get; set; }
	public double? Change { get; set; }
	public double? PriceMinusPrevWAPrice { get; set; }
	public double? ValToday { get; set; }
	public double? Yield { get; set; }
	public double? YieldAtWAPrice { get; set; }
	public double? MarketPriceToday { get; set; }
	public double? HighBid { get; set; }
	public double? LowAsk { get; set; }
	public double? High { get; set; }
	public double? Low { get; set; }
	public double? ClosePrice { get; set; }
	public double? CloseYield { get; set; }
	public TransaqSecurityStatus? Status { get; set; }
	public string SessionStatus { get; set; }
	public double? BuyDeposit { get; set; }
	public double? SellDeposit { get; set; }
	public double? Volatility { get; set; }
	public double? TheoreticalPrice { get; set; }
	public double? BgoBuy { get; set; }
	public double? PointCost { get; set; }
}

enum TransaqSecurityStatus
{
	A,
	S,
	N,
	undefined,
}