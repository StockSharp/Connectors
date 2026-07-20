namespace StockSharp.Nado;

using Native;

/// <summary>Nado order condition.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.NadoKey)]
public class NadoOrderCondition : OrderCondition
{
	/// <summary>Restrict the order to reducing an existing position.</summary>
	[DataMember]
	public bool IsReduceOnly
	{
		get => Parameters.TryGetValue(nameof(IsReduceOnly), out var value) &&
			value is true;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>Execution mode encoded in the order appendix.</summary>
	[DataMember]
	public NadoOrderExecutionTypes ExecutionType
	{
		get => Parameters.TryGetValue(nameof(ExecutionType), out var value)
			? (NadoOrderExecutionTypes)value
			: NadoOrderExecutionTypes.Default;
		set => Parameters[nameof(ExecutionType)] = value;
	}

	/// <summary>Use an isolated-margin position.</summary>
	[DataMember]
	public bool IsIsolated
	{
		get => Parameters.TryGetValue(nameof(IsIsolated), out var value) &&
			value is true;
		set => Parameters[nameof(IsIsolated)] = value;
	}

	/// <summary>Margin transferred into an isolated position.</summary>
	[DataMember]
	public decimal? IsolatedMargin
	{
		get => Parameters.TryGetValue(nameof(IsolatedMargin), out var value)
			? (decimal?)value
			: null;
		set => Parameters[nameof(IsolatedMargin)] = value;
	}

	/// <summary>Allow spot leverage for this order.</summary>
	[DataMember]
	public bool IsSpotLeverage
	{
		get => Parameters.TryGetValue(nameof(IsSpotLeverage), out var value) &&
			value is true;
		set => Parameters[nameof(IsSpotLeverage)] = value;
	}

	/// <summary>Allow borrowing isolated margin from the cross account.</summary>
	[DataMember]
	public bool IsBorrowMargin
	{
		get => Parameters.TryGetValue(nameof(IsBorrowMargin), out var value) &&
			value is true;
		set => Parameters[nameof(IsBorrowMargin)] = value;
	}

	/// <summary>Optional registered builder identifier.</summary>
	[DataMember]
	public int? BuilderId
	{
		get => Parameters.TryGetValue(nameof(BuilderId), out var value)
			? (int?)value
			: null;
		set => Parameters[nameof(BuilderId)] = value;
	}

	/// <summary>Builder fee rate in tenths of a basis point.</summary>
	[DataMember]
	public int? BuilderFeeRate
	{
		get => Parameters.TryGetValue(nameof(BuilderFeeRate), out var value)
			? (int?)value
			: null;
		set => Parameters[nameof(BuilderFeeRate)] = value;
	}
}
