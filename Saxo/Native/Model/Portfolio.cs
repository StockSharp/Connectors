namespace StockSharp.Saxo.Native.Model;

sealed class SaxoBalance
{
	[JsonProperty("AccountKey")]
	public string AccountKey { get; set; }

	[JsonProperty("Currency")]
	public string Currency { get; set; }

	[JsonProperty("CashBalance")]
	public decimal? CashBalance { get; set; }

	[JsonProperty("TotalValue")]
	public decimal? TotalValue { get; set; }

	[JsonProperty("NetEquityForMargin")]
	public decimal? NetEquityForMargin { get; set; }

	[JsonProperty("MarginAvailableForTrading")]
	public decimal? MarginAvailableForTrading { get; set; }

	[JsonProperty("MarginUsedByCurrentPositions")]
	public decimal? MarginUsedByCurrentPositions { get; set; }

	[JsonProperty("UnrealizedMarginProfitLoss")]
	public decimal? UnrealizedMarginProfitLoss { get; set; }

	[JsonProperty("UnrealizedPositionsValue")]
	public decimal? UnrealizedPositionsValue { get; set; }

	[JsonProperty("CalculationReliability")]
	public string CalculationReliability { get; set; }
}

sealed class SaxoBalanceArguments
{
	[JsonProperty("AccountKey")]
	public string AccountKey { get; set; }

	[JsonProperty("ClientKey")]
	public string ClientKey { get; set; }

	[JsonProperty("FieldGroups")]
	public string[] FieldGroups { get; set; }
}

sealed class SaxoBalanceSubscriptionRequest : SaxoSubscriptionRequest
{
	[JsonProperty("Arguments")]
	public SaxoBalanceArguments Arguments { get; set; }
}

sealed class SaxoNetPositionBase
{
	[JsonProperty("AccountId")]
	public string AccountId { get; set; }

	[JsonProperty("Amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("AmountLong")]
	public decimal? AmountLong { get; set; }

	[JsonProperty("AmountShort")]
	public decimal? AmountShort { get; set; }

	[JsonProperty("AssetType")]
	public string AssetType { get; set; }

	[JsonProperty("ExpiryDate")]
	public DateTime? ExpiryDate { get; set; }

	[JsonProperty("Uic")]
	public long Uic { get; set; }
}

sealed class SaxoNetPositionView
{
	[JsonProperty("AverageOpenPrice")]
	public decimal? AverageOpenPrice { get; set; }

	[JsonProperty("CurrentPrice")]
	public decimal? CurrentPrice { get; set; }

	[JsonProperty("Exposure")]
	public decimal? Exposure { get; set; }

	[JsonProperty("ProfitLossOnTrade")]
	public decimal? ProfitLossOnTrade { get; set; }

	[JsonProperty("Status")]
	public string Status { get; set; }
}

sealed class SaxoNetPosition
{
	[JsonProperty("NetPositionId")]
	public string NetPositionId { get; set; }

	[JsonProperty("NetPositionBase")]
	public SaxoNetPositionBase NetPositionBase { get; set; }

	[JsonProperty("NetPositionView")]
	public SaxoNetPositionView NetPositionView { get; set; }
}

sealed class SaxoNetPositionArguments
{
	[JsonProperty("AccountKey")]
	public string AccountKey { get; set; }

	[JsonProperty("ClientKey")]
	public string ClientKey { get; set; }

	[JsonProperty("FieldGroups")]
	public string[] FieldGroups { get; set; }
}

sealed class SaxoNetPositionSubscriptionRequest : SaxoSubscriptionRequest
{
	[JsonProperty("Arguments")]
	public SaxoNetPositionArguments Arguments { get; set; }
}
