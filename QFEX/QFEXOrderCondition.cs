namespace StockSharp.QFEX;

/// <summary>QFEX order condition.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.QFEXKey)]
public class QFEXOrderCondition : OrderCondition
{
	/// <summary>Restrict the order to reducing an existing position.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PosConditionReduceOnlyKey,
		Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public bool IsReduceOnly
	{
		get => Parameters.TryGetValue(nameof(IsReduceOnly), out var value) &&
			value is true;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>Optional attached take-profit price.</summary>
	[DataMember]
	public decimal? TakeProfitPrice
	{
		get => Parameters.TryGetValue(nameof(TakeProfitPrice), out var value)
			? (decimal?)value
			: null;
		set => Parameters[nameof(TakeProfitPrice)] = value;
	}

	/// <summary>Optional attached stop-loss price.</summary>
	[DataMember]
	public decimal? StopLossPrice
	{
		get => Parameters.TryGetValue(nameof(StopLossPrice), out var value)
			? (decimal?)value
			: null;
		set => Parameters[nameof(StopLossPrice)] = value;
	}
}
