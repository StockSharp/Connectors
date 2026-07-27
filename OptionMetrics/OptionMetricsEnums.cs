namespace StockSharp.OptionMetrics;

/// <summary>Price adjustments for IvyDB underlying-security history.</summary>
[DataContract]
[Serializable]
public enum IvyDbPriceAdjustments
{
	/// <summary>Original provider prices.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RawKey)]
	Raw,

	/// <summary>Prices adjusted for splits and other capital distributions.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SplitAdjustedKey)]
	SplitAdjusted,

	/// <summary>Prices adjusted with the provider total-return factor.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TotalReturnAdjustedKey)]
	TotalReturnAdjusted,
}

enum IvyDbMarkets
{
	Stocks,
	Options,
}

enum IvyDbFileKinds
{
	Security,
	SecurityName,
	SecurityPrice,
	OptionPrice,
}

enum IvyDbIssueTypes
{
	Unknown,
	CommonStock,
	Index,
	Fund,
	DepositaryReceipt,
	ExchangeTradedFund,
	StructuredProduct,
	Unit,
}

enum IvyDbSymbolFormats
{
	Unknown,
	Legacy,
	Osi,
}
