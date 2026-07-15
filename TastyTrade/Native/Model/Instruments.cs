namespace StockSharp.TastyTrade.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum TastyInstrumentTypes
{
	[EnumMember(Value = "Cryptocurrency")]
	Cryptocurrency,
	[EnumMember(Value = "Equity")]
	Equity,
	[EnumMember(Value = "Equity Option")]
	EquityOption,
	[EnumMember(Value = "Event Contract")]
	EventContract,
	[EnumMember(Value = "Fixed Income Security")]
	FixedIncome,
	[EnumMember(Value = "Future")]
	Future,
	[EnumMember(Value = "Future Option")]
	FutureOption,
	[EnumMember(Value = "Liquidity Pool")]
	LiquidityPool,
}

sealed class TastySymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("instrument-type")]
	public TastyInstrumentTypes InstrumentType { get; set; }
}

class TastyInstrument
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("instrument-type")]
	public TastyInstrumentTypes InstrumentType { get; set; }

	[JsonProperty("streamer-symbol")]
	public string StreamerSymbol { get; set; }

	[JsonProperty("listed-market")]
	public string ListedMarket { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("tick-size")]
	public decimal? TickSize { get; set; }

	[JsonProperty("active")]
	public bool IsActive { get; set; }
}

sealed class TastyDerivativeInstrument : TastyInstrument
{
	[JsonProperty("underlying-symbol")]
	public string UnderlyingSymbol { get; set; }

	[JsonProperty("expiration-date")]
	public DateTime? ExpirationDate { get; set; }

	[JsonProperty("strike-price")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("option-type")]
	public TastyOptionTypes? OptionType { get; set; }

	[JsonProperty("shares-per-contract")]
	public decimal? SharesPerContract { get; set; }

	[JsonProperty("multiplier")]
	public decimal? Multiplier { get; set; }

	[JsonProperty("contract-size")]
	public decimal? ContractSize { get; set; }
}

[JsonConverter(typeof(StringEnumConverter))]
enum TastyOptionTypes
{
	[EnumMember(Value = "Call")]
	Call,
	[EnumMember(Value = "Put")]
	Put,
}
