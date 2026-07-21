namespace StockSharp.FalconX;

/// <summary>FalconX-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.FalconXKey)]
public sealed class FalconXOrderCondition : OrderCondition
{
	/// <summary>Execute the request as a TWAP order over the order WebSocket.</summary>
	[DataMember]
	[Display(Name = "TWAP",
		Description = "Execute the order as a FalconX TWAP order.",
		GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public bool IsTwap
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsTwap)) ?? false;
		set => Parameters[nameof(IsTwap)] = value;
	}

	/// <summary>Total TWAP execution duration.</summary>
	[DataMember]
	[Display(Name = "TWAP duration",
		Description = "Total duration over which FalconX executes the TWAP order.",
		GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public TimeSpan TwapDuration
	{
		get => (TimeSpan?)Parameters.TryGetValue(nameof(TwapDuration)) ??
			TimeSpan.FromMinutes(5);
		set => Parameters[nameof(TwapDuration)] = value;
	}

	/// <summary>Optional number of TWAP child transactions.</summary>
	[DataMember]
	[Display(Name = "TWAP transactions",
		Description = "Optional number of child transactions in the TWAP schedule.",
		GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public int? TwapTransactionsCount
	{
		get => (int?)Parameters.TryGetValue(nameof(TwapTransactionsCount));
		set => Parameters[nameof(TwapTransactionsCount)] = value;
	}

	/// <summary>Optional REST FOK limit-order slippage in basis points.</summary>
	[DataMember]
	[Display(Name = "Slippage (bps)",
		Description = "Optional FalconX REST limit-order slippage in basis points.",
		GroupName = LocalizedStrings.ParametersKey, Order = 3)]
	public decimal? SlippageBps
	{
		get => (decimal?)Parameters.TryGetValue(nameof(SlippageBps));
		set => Parameters[nameof(SlippageBps)] = value;
	}

	/// <inheritdoc />
	public override OrderCondition Clone()
	{
		var clone = new FalconXOrderCondition();
		clone.Parameters.AddRange(Parameters);
		return clone;
	}
}
