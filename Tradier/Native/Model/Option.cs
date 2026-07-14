namespace StockSharp.Tradier.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Option
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("exch")]
	public string Exchange { get; set; }

	[JsonProperty("type")]
	public TradierSecurityTypes Type { get; set; }

	[JsonProperty("last")]
	public double? Last { get; set; }

	[JsonProperty("change")]
	public double? Change { get; set; }

	[JsonProperty("change_percentage")]
	public double? ChangePercentage { get; set; }

	[JsonProperty("volume")]
	public double? Volume { get; set; }

	[JsonProperty("average_volume")]
	public double? AverageVolume { get; set; }

	[JsonProperty("last_volume")]
	public double? LastVolume { get; set; }

	[JsonProperty("trade_date")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime TradeDate { get; set; }

	[JsonProperty("open")]
	public double? Open { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("close")]
	public double? Close { get; set; }

	[JsonProperty("prevclose")]
	public double? PrevClose { get; set; }

	[JsonProperty("week_52_high")]
	public double? Week52High { get; set; }

	[JsonProperty("week_52_low")]
	public double? Week52Low { get; set; }

	[JsonProperty("bid")]
	public double? Bid { get; set; }

	[JsonProperty("bidsize")]
	public double? BidSize { get; set; }

	[JsonProperty("bidexch")]
	public string BidExchange { get; set; }

	[JsonProperty("bid_date")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime BidDate { get; set; }

	[JsonProperty("ask")]
	public double? Ask { get; set; }

	[JsonProperty("asksize")]
	public double? AskSize { get; set; }

	[JsonProperty("askexch")]
	public string AskExchange { get; set; }

	[JsonProperty("ask_date")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime AskDate { get; set; }

	[JsonProperty("open_interest")]
	public double OpenInterest { get; set; }

	[JsonProperty("underlying")]
	public string Underlying { get; set; }

	[JsonProperty("strike")]
	public double Strike { get; set; }

	[JsonProperty("contract_size")]
	public double ContractSize { get; set; }

	[JsonProperty("expiration_date")]
	public DateTime ExpirationDate { get; set; }

	[JsonProperty("expiration_type")]
	public string ExpirationType { get; set; }

	[JsonProperty("option_type")]
	public TradierOptionTypes OptionType { get; set; }

	[JsonProperty("root_symbol")]
	public string RootSymbol { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class OptionExpiration
{
	[JsonProperty("date")]
	public DateTime Date { get; set; }

	[JsonProperty("contract_size")]
	public double ContractSize { get; set; }

	[JsonProperty("expiration_type")]
	public string ExpirationType { get; set; }

	[JsonProperty("strikes")]
	public Strikes Strikes { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Strikes
{
	[JsonProperty("strike")]
	public double[] Strike { get; set; }
}
