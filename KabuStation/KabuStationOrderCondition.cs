namespace StockSharp.KabuStation;

/// <summary>kabu Station-specific order parameters.</summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.KabuStationKey)]
[Serializable]
[DataContract]
public class KabuStationOrderCondition : OrderCondition
{
	/// <summary>Exchange route used to submit the order.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ExchangeKey,
		Description = LocalizedStrings.KabuStationOrderExchangeDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 0)]
	[DataMember]
	public KabuStationExchanges? Exchange
	{
		get => (KabuStationExchanges?)Parameters.TryGetValue(nameof(Exchange));
		set => Parameters[nameof(Exchange)] = value;
	}

	/// <summary>Account type.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.KabuStationAccountTypeDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 1)]
	[DataMember]
	public KabuStationAccountTypes? AccountType
	{
		get => (KabuStationAccountTypes?)Parameters.TryGetValue(nameof(AccountType));
		set => Parameters[nameof(AccountType)] = value;
	}

	/// <summary>Cash or margin transaction mode.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MarginKey,
		Description = LocalizedStrings.MarginKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.GeneralKey, Order = 2)]
	[DataMember]
	public KabuStationCashMargins? CashMargin
	{
		get => (KabuStationCashMargins?)Parameters.TryGetValue(nameof(CashMargin));
		set => Parameters[nameof(CashMargin)] = value;
	}

	/// <summary>Margin transaction type.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TypeKey,
		Description = LocalizedStrings.MarginKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.GeneralKey, Order = 3)]
	[DataMember]
	public KabuStationMarginTradeTypes? MarginTradeType
	{
		get => (KabuStationMarginTradeTypes?)Parameters.TryGetValue(nameof(MarginTradeType));
		set => Parameters[nameof(MarginTradeType)] = value;
	}

	/// <summary>Derivative trade mode.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PositionKey,
		Description = LocalizedStrings.PositionKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.GeneralKey, Order = 4)]
	[DataMember]
	public KabuStationDerivativeTradeTypes? DerivativeTradeType
	{
		get => (KabuStationDerivativeTradeTypes?)Parameters.TryGetValue(nameof(DerivativeTradeType));
		set => Parameters[nameof(DerivativeTradeType)] = value;
	}

	/// <summary>Native derivative time in force.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TimeInForceKey,
		Description = LocalizedStrings.TimeInForceKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.GeneralKey, Order = 5)]
	[DataMember]
	public KabuStationTimeInForces? NativeTimeInForce
	{
		get => (KabuStationTimeInForces?)Parameters.TryGetValue(nameof(NativeTimeInForce));
		set => Parameters[nameof(NativeTimeInForce)] = value;
	}

	/// <summary>Specific position ID to close.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.IdKey,
		Description = LocalizedStrings.KabuStationClosePositionIdDescKey,
		GroupName = LocalizedStrings.PositionKey, Order = 6)]
	[DataMember]
	public string ClosePositionId
	{
		get => (string)Parameters.TryGetValue(nameof(ClosePositionId));
		set => Parameters[nameof(ClosePositionId)] = value;
	}

	/// <summary>Stop trigger price.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.StopLossKey, Order = 7)]
	[DataMember]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>Limit price used after the stop is triggered. Zero sends a market order.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PriceKey,
		Description = LocalizedStrings.KabuStationStopLimitPriceDescKey,
		GroupName = LocalizedStrings.StopLossKey, Order = 8)]
	[DataMember]
	public decimal? StopLimitPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLimitPrice));
		set => Parameters[nameof(StopLimitPrice)] = value;
	}

	/// <summary>Stop trigger comparison.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DirectionKey,
		Description = LocalizedStrings.KabuStationTriggerDirectionDescKey,
		GroupName = LocalizedStrings.StopLossKey, Order = 9)]
	[DataMember]
	public KabuStationTriggerComparisons? TriggerComparison
	{
		get => (KabuStationTriggerComparisons?)Parameters.TryGetValue(nameof(TriggerComparison));
		set => Parameters[nameof(TriggerComparison)] = value;
	}

	/// <summary>Calendar expiration date represented as a UTC date.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ExpiryDateKey,
		Description = LocalizedStrings.ExpiryDateKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.GeneralKey, Order = 10)]
	[DataMember]
	public DateTime? ExpireDate
	{
		get => (DateTime?)Parameters.TryGetValue(nameof(ExpireDate));
		set => Parameters[nameof(ExpireDate)] = value is { } date
			? DateTime.SpecifyKind(date.Date, DateTimeKind.Utc)
			: null;
	}
}
