namespace StockSharp.Tradier.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Quote
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("exch")]
	public string Exch { get; set; }

	[JsonProperty("type")]
	public TradierSecurityTypes Type { get; set; }

	[JsonProperty("last")]
	public double? Last { get; set; }

	[JsonProperty("change")]
	public double? Change { get; set; }

	[JsonProperty("volume")]
	public double? Volume { get; set; }

	[JsonProperty("open")]
	public double? Open { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("close")]
	public double? Close { get; set; }

	[JsonProperty("bid")]
	public double? Bid { get; set; }

	[JsonProperty("ask")]
	public double? Ask { get; set; }

	[JsonProperty("change_percentage")]
	public double? ChangePercentage { get; set; }

	[JsonProperty("average_volume")]
	public double? AverageVolume { get; set; }

	[JsonProperty("last_volume")]
	public double? LastVolume { get; set; }

	[JsonProperty("trade_date")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? TradeDate { get; set; }

	[JsonProperty("prevclose")]
	public double? PrevClose { get; set; }

	[JsonProperty("week_52_high")]
	public double? Week52High { get; set; }

	[JsonProperty("week_52_low")]
	public double? Week52Low { get; set; }

	[JsonProperty("bidsize")]
	public double? BidSize { get; set; }

	[JsonProperty("bidsz")]
	public double? BidSize2 { get; set; }

	[JsonProperty("bidexch")]
	public string BidExch { get; set; }

	[JsonProperty("bid_date")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? BidDate { get; set; }

	[JsonProperty("biddate")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? BidDate2 { get; set; }

	[JsonProperty("asksize")]
	public double? AskSize { get; set; }

	[JsonProperty("asksz")]
	public double? AskSize2 { get; set; }

	[JsonProperty("askexch")]
	public string AskExch { get; set; }

	[JsonProperty("ask_date")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? AskDate { get; set; }

	[JsonProperty("askdate")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? AskDate2 { get; set; }

	[JsonProperty("root_symbols")]
	public string RootSymbols { get; set; }
}
