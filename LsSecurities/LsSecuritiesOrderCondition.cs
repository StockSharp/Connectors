namespace StockSharp.LsSecurities;

/// <summary>LS Securities native cash-equity order options.</summary>
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.LsSecuritiesOrderConditionKey)]
public sealed class LsSecuritiesOrderCondition : OrderCondition
{
	/// <summary>Initializes a new instance of the <see cref="LsSecuritiesOrderCondition"/> class.</summary>
	public LsSecuritiesOrderCondition()
	{
		PriceType = LsOrderPriceTypes.Limit;
		Market = LsOrderMarkets.Auto;
		MarginTransactionCode = "000";
	}

	/// <summary>Native price type.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LsSecuritiesPriceTypeKey,
		Description = LocalizedStrings.LsSecuritiesPriceTypeDescKey,
		GroupName = LocalizedStrings.OrderKey,
		Order = 0)]
	public LsOrderPriceTypes PriceType
	{
		get => Parameters.TryGetValue(nameof(PriceType))?.To<LsOrderPriceTypes>() ?? LsOrderPriceTypes.Limit;
		set => Parameters[nameof(PriceType)] = value;
	}

	/// <summary>Execution venue requested for the order.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LsSecuritiesOrderMarketKey,
		Description = LocalizedStrings.LsSecuritiesOrderMarketDescKey,
		GroupName = LocalizedStrings.OrderKey,
		Order = 1)]
	public LsOrderMarkets Market
	{
		get => Parameters.TryGetValue(nameof(Market))?.To<LsOrderMarkets>() ?? LsOrderMarkets.Auto;
		set => Parameters[nameof(Market)] = value;
	}

	/// <summary>Native margin transaction code. <c>000</c> means cash.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LsSecuritiesMarginCodeKey,
		Description = LocalizedStrings.LsSecuritiesMarginCodeDescKey,
		GroupName = LocalizedStrings.OrderKey,
		Order = 2)]
	public string MarginTransactionCode
	{
		get => Parameters.TryGetValue(nameof(MarginTransactionCode))?.ToString().IsEmpty("000");
		set => Parameters[nameof(MarginTransactionCode)] = value;
	}

	/// <summary>Loan date required by applicable credit orders.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LsSecuritiesLoanDateKey,
		Description = LocalizedStrings.LsSecuritiesLoanDateDescKey,
		GroupName = LocalizedStrings.OrderKey,
		Order = 3)]
	public DateTime? LoanDate
	{
		get => Parameters.TryGetValue(nameof(LoanDate))?.To<DateTime?>();
		set => Parameters[nameof(LoanDate)] = value?.ToUniversalTime();
	}
}
