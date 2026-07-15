namespace StockSharp.Oanda.Native.DataTypes;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class TradeData
{
	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("tradeID")]
	public long? TradeId { get; set; }

	[JsonProperty("units")]
	public double? Units { get; set; }

	[JsonProperty("initialUnits")]
	public double? InitialUnits { get; set; }

	[JsonProperty("currentUnits")]
	public double? CurrentUnits { get; set; }

	//[JsonProperty("side")]
	//public string Side { get; set; }

	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("openTime")]
	public string OpenTime { get; set; }

	[JsonProperty("closeTime")]
	public string CloseTime { get; set; }

	//[JsonProperty("timeInForce")]
	//public string TimeInForce { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("averageClosePrice")]
	public double? AverageClosePrice { get; set; }

	[JsonProperty("closingTransactionIDs")]
	public IEnumerable<long> ClosingTransactionIds { get; set; }

	[JsonProperty("takeProfitOrder")]
	public Order TakeProfitOrder { get; set; }

	[JsonProperty("stopLossOrder")]
	public Order StopLossOrder { get; set; }

	[JsonProperty("trailingStopLossOrder")]
	public Order TrailingStopLossOrder { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("realizedPL")]
	public double? RealizedPnL { get; set; }

	[JsonProperty("financing")]
	public double? Financing { get; set; }

	[JsonProperty("unrealizedPL")]
	public double? UnrealizedPnL { get; set; }

	[JsonProperty("clientExtensions")]
	public ClientExtensions ClientExtensions { get; set; }
}