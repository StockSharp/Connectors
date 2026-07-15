namespace StockSharp.TastyTrade.Native.Model;

sealed class TastyAccountAuthority
{
	[JsonProperty("account")]
	public TastyAccount Account { get; set; }

	[JsonProperty("authority-level")]
	public string AuthorityLevel { get; set; }
}

sealed class TastyAccount
{
	[JsonProperty("account-number")]
	public string AccountNumber { get; set; }

	[JsonProperty("nickname")]
	public string Nickname { get; set; }

	[JsonProperty("account-type-name")]
	public string AccountTypeName { get; set; }

	[JsonProperty("is-closed")]
	public bool IsClosed { get; set; }

	[JsonProperty("is-futures-approved")]
	public bool IsFuturesApproved { get; set; }
}

sealed class TastyBalance
{
	[JsonProperty("account-number")]
	public string AccountNumber { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("cash-balance")]
	public decimal? CashBalance { get; set; }

	[JsonProperty("cash-available-to-withdraw")]
	public decimal? CashAvailableToWithdraw { get; set; }

	[JsonProperty("net-liquidating-value")]
	public decimal? NetLiquidatingValue { get; set; }

	[JsonProperty("equity-buying-power")]
	public decimal? EquityBuyingPower { get; set; }

	[JsonProperty("derivative-buying-power")]
	public decimal? DerivativeBuyingPower { get; set; }

	[JsonProperty("day-trading-buying-power")]
	public decimal? DayTradingBuyingPower { get; set; }

	[JsonProperty("maintenance-requirement")]
	public decimal? MaintenanceRequirement { get; set; }

	[JsonProperty("updated-at")]
	public DateTime? UpdatedAt { get; set; }
}

sealed class TastyPosition
{
	[JsonProperty("account-number")]
	public string AccountNumber { get; set; }

	[JsonProperty("instrument-type")]
	public TastyInstrumentTypes InstrumentType { get; set; }

	[JsonProperty("streamer-symbol")]
	public string StreamerSymbol { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("underlying-symbol")]
	public string UnderlyingSymbol { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("quantity-direction")]
	public TastyQuantityDirections QuantityDirection { get; set; }

	[JsonProperty("average-open-price")]
	public decimal? AverageOpenPrice { get; set; }

	[JsonProperty("close-price")]
	public decimal? ClosePrice { get; set; }

	[JsonProperty("multiplier")]
	public decimal? Multiplier { get; set; }

	[JsonProperty("realized-day-gain")]
	public decimal? RealizedDayGain { get; set; }

	[JsonProperty("realized-today")]
	public decimal? RealizedToday { get; set; }

	[JsonProperty("updated-at")]
	public DateTime? UpdatedAt { get; set; }
}

[JsonConverter(typeof(StringEnumConverter))]
enum TastyQuantityDirections
{
	[EnumMember(Value = "Long")]
	Long,
	[EnumMember(Value = "Short")]
	Short,
}
