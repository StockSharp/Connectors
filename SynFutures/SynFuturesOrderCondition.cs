namespace StockSharp.SynFutures;

/// <summary>SynFutures-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SynFuturesKey)]
public class SynFuturesOrderCondition : OrderCondition
{
	/// <summary>Target position leverage.</summary>
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
				"SynFutures leverage must be positive.");
	}

	/// <summary>
	/// Explicit margin transfer in quote units. When omitted, the connector
	/// calculates margin from leverage. Zero is appropriate for position
	/// reductions and closes.
	/// </summary>
	[DataMember]
	[Display(
		Name = "Margin",
		Description = "Optional explicit quote margin transferred with the order.",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 1)]
	public decimal? Margin
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Margin));
		set => Parameters[nameof(Margin)] = value is null or >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"SynFutures margin cannot be negative.");
	}
}
