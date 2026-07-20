namespace StockSharp.DydxChain;

/// <summary>dYdX Chain advanced order kinds.</summary>
[DataContract]
public enum DydxChainOrderKinds
{
	/// <summary>Regular market or limit order.</summary>
	[EnumMember]
	Regular,

	/// <summary>Stop-loss order.</summary>
	[EnumMember]
	StopLoss,

	/// <summary>Take-profit order.</summary>
	[EnumMember]
	TakeProfit,

	/// <summary>Time-weighted average price order.</summary>
	[EnumMember]
	Twap,
}

/// <summary>dYdX Chain-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DydxChainKey)]
public class DydxChainOrderCondition : OrderCondition
{
	/// <summary>Advanced order kind.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TypeKey,
		GroupName = LocalizedStrings.TransactionKey, Order = 0)]
	public DydxChainOrderKinds OrderKind
	{
		get => (DydxChainOrderKinds?)Parameters.TryGetValue(
			nameof(OrderKind)) ?? DydxChainOrderKinds.Regular;
		set => Parameters[nameof(OrderKind)] = value;
	}

	/// <summary>Oracle trigger price for stop-loss and take-profit orders.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		GroupName = LocalizedStrings.TransactionKey, Order = 1)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>Only reduce an existing perpetual position.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PosConditionReduceOnlyKey,
		GroupName = LocalizedStrings.TransactionKey, Order = 2)]
	public bool IsReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>Reject a limit order that would immediately take liquidity.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PostOnlyKey,
		GroupName = LocalizedStrings.TransactionKey, Order = 3)]
	public bool IsPostOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsPostOnly)) ?? false;
		set => Parameters[nameof(IsPostOnly)] = value;
	}

	/// <summary>UTC expiration for stateful, conditional, and TWAP orders.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExpiryDateKey,
		GroupName = LocalizedStrings.TransactionKey, Order = 4)]
	public DateTime? ExpirationTime
	{
		get => (DateTime?)Parameters.TryGetValue(nameof(ExpirationTime));
		set => Parameters[nameof(ExpirationTime)] = value?.EnsureUtc();
	}

	/// <summary>TWAP execution duration.</summary>
	[DataMember]
	[Display(Name = "TWAP duration",
		GroupName = LocalizedStrings.TransactionKey, Order = 5)]
	public TimeSpan TwapDuration
	{
		get => (TimeSpan?)Parameters.TryGetValue(nameof(TwapDuration)) ??
			TimeSpan.FromMinutes(30);
		set => Parameters[nameof(TwapDuration)] = value;
	}

	/// <summary>Interval between TWAP child orders.</summary>
	[DataMember]
	[Display(Name = "TWAP interval",
		GroupName = LocalizedStrings.TransactionKey, Order = 6)]
	public TimeSpan TwapInterval
	{
		get => (TimeSpan?)Parameters.TryGetValue(nameof(TwapInterval)) ??
			TimeSpan.FromMinutes(1);
		set => Parameters[nameof(TwapInterval)] = value;
	}

	/// <summary>Maximum TWAP oracle-price deviation in percent.</summary>
	[DataMember]
	[Display(Name = "TWAP price tolerance",
		GroupName = LocalizedStrings.TransactionKey, Order = 7)]
	public decimal TwapPriceTolerance
	{
		get => (decimal?)Parameters.TryGetValue(
			nameof(TwapPriceTolerance)) ?? 1m;
		set => Parameters[nameof(TwapPriceTolerance)] = value;
	}
}
