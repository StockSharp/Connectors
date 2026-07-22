namespace StockSharp.Deepcoin;

/// <summary>
/// Deepcoin margin modes.
/// </summary>
[DataContract]
[Serializable]
public enum DeepcoinMarginModes
{
	/// <summary>
	/// Select cash for spot and cross margin for perpetuals.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AutoKey)]
	Auto,

	/// <summary>
	/// Cash trading.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CashKey)]
	Cash,

	/// <summary>
	/// Cross margin.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DeepcoinCrossMarginKey)]
	Cross,

	/// <summary>
	/// Isolated margin.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DeepcoinIsolatedMarginKey)]
	Isolated,
}

/// <summary>
/// Deepcoin perpetual position sides.
/// </summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum DeepcoinPositionSides
{
	/// <summary>
	/// Long position.
	/// </summary>
	[EnumMember(Value = "long")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LongKey)]
	Long,

	/// <summary>
	/// Short position.
	/// </summary>
	[EnumMember(Value = "short")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ShortKey)]
	Short,
}

/// <summary>
/// Deepcoin order execution policies.
/// </summary>
[DataContract]
[Serializable]
public enum DeepcoinOrderPolicies
{
	/// <summary>
	/// Regular order.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RegularKey)]
	Regular,

	/// <summary>
	/// Post-only order.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PostOnlyKey)]
	PostOnly,

	/// <summary>
	/// Immediate-or-cancel order.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ImmediateOrCancelKey)]
	ImmediateOrCancel,
}

/// <summary>
/// Deepcoin order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DeepcoinKey)]
public class DeepcoinOrderCondition : OrderCondition
{
	/// <summary>
	/// Trading margin mode.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MarginKey,
		Description = LocalizedStrings.MarginKey, GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public DeepcoinMarginModes MarginMode
	{
		get => (DeepcoinMarginModes?)Parameters.TryGetValue(nameof(MarginMode)) ?? DeepcoinMarginModes.Auto;
		set => Parameters[nameof(MarginMode)] = value;
	}

	/// <summary>
	/// Perpetual position side.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PositionKey,
		Description = LocalizedStrings.PositionKey, GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public DeepcoinPositionSides PositionSide
	{
		get => (DeepcoinPositionSides?)Parameters.TryGetValue(nameof(PositionSide)) ?? DeepcoinPositionSides.Long;
		set => Parameters[nameof(PositionSide)] = value;
	}

	/// <summary>
	/// Use split-position mode instead of merged-position mode.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SplitKey,
		Description = LocalizedStrings.SplitKey, GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public bool IsSplitPosition
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsSplitPosition)) ?? false;
		set => Parameters[nameof(IsSplitPosition)] = value;
	}

	/// <summary>
	/// Order execution policy.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.OrderTypeKey,
		Description = LocalizedStrings.OrderTypeKey, GroupName = LocalizedStrings.ParametersKey, Order = 3)]
	public DeepcoinOrderPolicies Policy
	{
		get => (DeepcoinOrderPolicies?)Parameters.TryGetValue(nameof(Policy)) ?? DeepcoinOrderPolicies.Regular;
		set => Parameters[nameof(Policy)] = value;
	}

	/// <summary>
	/// Perpetual leverage to set before submitting the order.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LeverageKey,
		Description = LocalizedStrings.LeverageKey, GroupName = LocalizedStrings.ParametersKey, Order = 4)]
	public int? Leverage
	{
		get => (int?)Parameters.TryGetValue(nameof(Leverage));
		set => Parameters[nameof(Leverage)] = value;
	}

	/// <summary>
	/// Reduce an existing position only.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PosConditionReduceOnlyKey,
		Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 5)]
	public bool IsReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>
	/// Position ID required when closing a split perpetual position.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PositionKey,
		Description = LocalizedStrings.PositionKey, GroupName = LocalizedStrings.ParametersKey, Order = 6)]
	public string ClosePositionId
	{
		get => (string)Parameters.TryGetValue(nameof(ClosePositionId));
		set => Parameters[nameof(ClosePositionId)] = value;
	}

	/// <summary>
	/// Use quote-currency volume for spot market orders instead of base-currency volume.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.QuoteVolumeKey,
		Description = LocalizedStrings.QuoteVolumeKey, GroupName = LocalizedStrings.ParametersKey, Order = 7)]
	public bool IsQuoteVolume
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsQuoteVolume)) ?? false;
		set => Parameters[nameof(IsQuoteVolume)] = value;
	}

	/// <summary>
	/// Attached take-profit trigger price.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TakeProfitKey,
		Description = LocalizedStrings.TakeProfitKey, GroupName = LocalizedStrings.ParametersKey, Order = 8)]
	public decimal? TakeProfitTriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TakeProfitTriggerPrice));
		set => Parameters[nameof(TakeProfitTriggerPrice)] = value;
	}

	/// <summary>
	/// Attached stop-loss trigger price.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopLossKey,
		Description = LocalizedStrings.StopLossKey, GroupName = LocalizedStrings.ParametersKey, Order = 9)]
	public decimal? StopLossTriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLossTriggerPrice));
		set => Parameters[nameof(StopLossTriggerPrice)] = value;
	}
}
