namespace StockSharp.Tardis.Native.Model;

sealed class TardisInstrument
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("datasetId")]
	public string DatasetId { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("type")]
	public TardisInstrumentTypes Type { get; set; }

	[JsonProperty("contractType")]
	public TardisContractTypes ContractType { get; set; }

	[JsonProperty("active")]
	public bool IsActive { get; set; }

	[JsonProperty("availableSince")]
	public string AvailableSince { get; set; }

	[JsonProperty("availableTo")]
	public string AvailableTo { get; set; }

	[JsonProperty("listing")]
	public string Listing { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("underlyingIndex")]
	public string UnderlyingIndex { get; set; }

	[JsonProperty("priceIncrement")]
	public decimal? PriceIncrement { get; set; }

	[JsonProperty("amountIncrement")]
	public decimal? AmountIncrement { get; set; }

	[JsonProperty("minTradeAmount")]
	public decimal? MinimumTradeAmount { get; set; }

	[JsonProperty("minNotional")]
	public decimal? MinimumNotional { get; set; }

	[JsonProperty("inverse")]
	public bool? IsInverse { get; set; }

	[JsonProperty("quanto")]
	public bool? IsQuanto { get; set; }

	[JsonProperty("contractMultiplier")]
	public decimal? ContractMultiplier { get; set; }

	[JsonProperty("settlementCurrency")]
	public string SettlementCurrency { get; set; }

	[JsonProperty("strikePrice")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("optionType")]
	public TardisOptionTypes OptionType { get; set; }

	[JsonProperty("margin")]
	public bool? IsMargin { get; set; }

	[JsonProperty("aliasFor")]
	public string AliasFor { get; set; }

	[JsonIgnore]
	public string Key => Exchange + ":" + Id + ":" + AvailableSince;
}

sealed class TardisErrorResponse
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}
