namespace StockSharp.GainsNetwork;

/// <summary>Gains Network gTrade-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.GainsNetworkKey)]
public class GainsNetworkOrderCondition : OrderCondition
{
	/// <summary>Position leverage.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 0)]
	public decimal Leverage
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Leverage)) ?? 10m;
		set => Parameters[nameof(Leverage)] = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Gains leverage must be positive.");
	}

	/// <summary>Collateral token symbol. Empty uses the adapter default.</summary>
	[DataMember]
	[Display(
		Name = "Collateral",
		Description = "Collateral token symbol; empty uses the adapter default.",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 1)]
	public string CollateralSymbol
	{
		get => (string)Parameters.TryGetValue(nameof(CollateralSymbol));
		set => Parameters[nameof(CollateralSymbol)] = value?.Trim();
	}

	/// <summary>Take-profit price, or zero when disabled.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 2)]
	public decimal TakeProfitPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TakeProfitPrice)) ?? 0m;
		set => Parameters[nameof(TakeProfitPrice)] = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Gains take-profit price cannot be negative.");
	}

	/// <summary>Stop-loss price, or zero when disabled.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 3)]
	public decimal StopLossPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLossPrice)) ?? 0m;
		set => Parameters[nameof(StopLossPrice)] = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Gains stop-loss price cannot be negative.");
	}

	/// <summary>Whether a pending entry is a stop order.</summary>
	[DataMember]
	[Display(
		Name = "Stop entry",
		Description = "Use stop-entry semantics instead of limit-entry semantics.",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 4)]
	public bool IsStopOrder
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsStopOrder)) ?? false;
		set => Parameters[nameof(IsStopOrder)] = value;
	}

	/// <summary>Whether the request closes an existing position.</summary>
	[DataMember]
	[Display(
		Name = "Close position",
		Description = "Close the existing trade identified by Trade index.",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 5)]
	public bool IsClosePosition
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsClosePosition)) ?? false;
		set => Parameters[nameof(IsClosePosition)] = value;
	}

	/// <summary>On-chain trade index for close and management operations.</summary>
	[DataMember]
	[Display(
		Name = "Trade index",
		Description = "On-chain Gains trade index used to manage or close a trade.",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 6)]
	public int? TradeIndex
	{
		get => (int?)Parameters.TryGetValue(nameof(TradeIndex));
		set => Parameters[nameof(TradeIndex)] = value is null or >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Gains trade index cannot be negative.");
	}
}
