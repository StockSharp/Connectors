namespace StockSharp.Deribit.Native.Model;

class Symbol
{
	[JsonProperty("tick_size")]
	public double? TickSize { get; set; }

	[JsonProperty("strike")]
	public double? Strike { get; set; }

	[JsonProperty("settlement_period")]
	public string SettlementPeriod { get; set; }

	[JsonProperty("quote_currency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("option_type")]
	public string OptionType { get; set; }

	[JsonProperty("min_trade_amount")]
	public double? MinTradeAmount { get; set; }

	[JsonProperty("kind")]
	public string Kind { get; set; }

	[JsonProperty("is_active")]
	public bool IsActive { get; set; }

	[JsonProperty("instrument_name")]
	public string Instrument { get; set; }

	[JsonProperty("expiration_timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? Expiration { get; set; }

	[JsonProperty("creation_timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? Creation { get; set; }

	[JsonProperty("contract_size")]
	public double? ContractSize { get; set; }

	[JsonProperty("base_currency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("taker_commission")]
	public double? TakerCommission { get; set; }

	[JsonProperty("maker_commission")]
	public double? MakerCommission { get; set; }
}