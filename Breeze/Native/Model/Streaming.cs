namespace StockSharp.Breeze.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class BreezeSocketAuth
{
	[JsonProperty("user")]
	public string User { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }
}

sealed class BreezeRoomRequest
{
	public string Event { get; set; }
	public string[] Symbols { get; set; }
}

sealed class BreezeMarketTick
{
	public string InstrumentToken { get; set; }
	public DateTime ServerTime { get; set; } = DateTime.UtcNow;
	public decimal? OpenPrice { get; set; }
	public decimal? LastPrice { get; set; }
	public decimal? HighPrice { get; set; }
	public decimal? LowPrice { get; set; }
	public decimal? Change { get; set; }
	public decimal? BidPrice { get; set; }
	public decimal? BidVolume { get; set; }
	public decimal? AskPrice { get; set; }
	public decimal? AskVolume { get; set; }
	public decimal? LastVolume { get; set; }
	public decimal? AveragePrice { get; set; }
	public decimal? Volume { get; set; }
	public decimal? TotalBuyVolume { get; set; }
	public decimal? TotalSellVolume { get; set; }
	public decimal? OpenInterest { get; set; }
	public decimal? OpenInterestChange { get; set; }
	public decimal? LowerCircuit { get; set; }
	public decimal? UpperCircuit { get; set; }
	public decimal? ClosePrice { get; set; }
	public DateTime? LastTradeTime { get; set; }
}

sealed class BreezeDepthUpdate
{
	public string InstrumentToken { get; set; }
	public DateTime ServerTime { get; set; } = DateTime.UtcNow;
	public BreezeDepthLevel[] Bids { get; set; } = [];
	public BreezeDepthLevel[] Asks { get; set; } = [];
}

sealed class BreezeDepthLevel
{
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
	public int? OrdersCount { get; set; }
}

sealed class BreezeOrderUpdate
{
	public string OrderId { get; set; }
	public string StockCode { get; set; }
	public BreezeProducts Product { get; set; }
	public Sides Side { get; set; }
	public string OrderType { get; set; }
	public string Validity { get; set; }
	public decimal Price { get; set; }
	public decimal TriggerPrice { get; set; }
	public decimal Quantity { get; set; }
	public decimal ExecutedQuantity { get; set; }
	public decimal CancelledQuantity { get; set; }
	public decimal AveragePrice { get; set; }
	public string Status { get; set; }
	public DateTime? OrderTime { get; set; }
	public DateTime? TradeTime { get; set; }
	public string Message { get; set; }
	public DateTime? ExpiryDate { get; set; }
	public decimal? StrikePrice { get; set; }
	public OptionTypes? OptionType { get; set; }
}

sealed class BreezeStreamCandle
{
	public string Event { get; set; }
	public string ExchangeCode { get; set; }
	public string StockCode { get; set; }
	public string ExpiryDate { get; set; }
	public decimal? StrikePrice { get; set; }
	public string Right { get; set; }
	public decimal Low { get; set; }
	public decimal High { get; set; }
	public decimal Open { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
	public decimal? OpenInterest { get; set; }
	public DateTime Time { get; set; }
}
