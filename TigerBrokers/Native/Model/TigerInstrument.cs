namespace StockSharp.TigerBrokers.Native.Model;

sealed class TigerInstrument
{
	public string Symbol { get; set; }
	public string SubscriptionSymbol { get; set; }
	public string Name { get; set; }
	public Market Market { get; set; }
	public SecType SecurityType { get; set; }
	public string Currency { get; set; }
	public string Exchange { get; set; }
	public DateTime? ExpiryDate { get; set; }
	public decimal? Strike { get; set; }
	public string Right { get; set; }
	public decimal? Multiplier { get; set; }
	public decimal? PriceStep { get; set; }
}

enum TigerFeedTypes
{
	Quote,
	Option,
	Future,
	Depth,
	TradeTick,
	Kline,
}

sealed class TigerMarketSubscription
{
	public long TransactionId { get; set; }
	public DataType DataType { get; set; }
	public SecurityId SecurityId { get; set; }
	public TigerInstrument Instrument { get; set; }
	public TimeSpan? TimeFrame { get; set; }
	public TigerFeedTypes FeedType { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class TigerOperationResponse : TigerResponse
{
	[JsonProperty("data")]
	public TigerOperationResult Data { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class TigerOperationResult
{
	[JsonProperty("id")]
	public long? Id { get; set; }
}
