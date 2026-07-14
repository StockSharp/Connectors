namespace StockSharp.DXtrade.Native.Model;

class Instrument
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("version")]
	public int Version { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("priceIncrement")]
	public double? PriceIncrement { get; set; }

	[JsonProperty("pipSize")]
	public double? PipSize { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("lotSize")]
	public double? LotSize { get; set; }

	[JsonProperty("multiplier")]
	public double? Multiplier { get; set; }

	[JsonProperty("underlying")]
	public string Underlying { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("expirationDetails")]
	public ExpirationDetails ExpirationDetails { get; set; }

	[JsonProperty("settlementType")]
	public string SettlementType { get; set; }

	[JsonProperty("firstCurrency")]
	public string FirstCurrency { get; set; }

	[JsonProperty("tradingHours")]
	public InstrumentSession[] TradingHours { get; set; }

	[JsonProperty("currencyType")]
	public string CurrencyType { get; set; }
}

class ExpirationDetails
{
	[JsonProperty("maturityDate")]
	public string MaturityDate { get; set; }

	[JsonProperty("lastTradeDate")]
	public string LastTradeDate { get; set; }
}

class InstrumentSession
{
	[JsonProperty("weekDay")]
	public string WeekDay { get; set; }

	[JsonProperty("eventType")]
	public string EventType { get; set; }
}

