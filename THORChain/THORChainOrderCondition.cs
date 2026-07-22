namespace StockSharp.THORChain;

/// <summary>THORChain-specific native swap parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.THORChainKey)]
public class THORChainOrderCondition : OrderCondition
{
	/// <summary>Recipient address on the destination asset chain.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public string DestinationAddress
	{
		get => (string)Parameters.TryGetValue(nameof(DestinationAddress));
		set => Parameters[nameof(DestinationAddress)] = value;
	}

	/// <summary>Optional address receiving a protocol refund.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public string RefundAddress
	{
		get => (string)Parameters.TryGetValue(nameof(RefundAddress));
		set => Parameters[nameof(RefundAddress)] = value;
	}

	/// <summary>THORChain blocks between streaming subswaps.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 2)]
	public int StreamingInterval
	{
		get => (int?)Parameters.TryGetValue(nameof(StreamingInterval)) ?? 1;
		set => Parameters[nameof(StreamingInterval)] = value;
	}

	/// <summary>Number of subswaps; zero lets THORChain choose.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 3)]
	public int StreamingQuantity
	{
		get => (int?)Parameters.TryGetValue(nameof(StreamingQuantity)) ?? 0;
		set => Parameters[nameof(StreamingQuantity)] = value;
	}

	/// <summary>Optional per-order liquidity tolerance in basis points.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 4)]
	public int? LiquidityToleranceBasisPoints
	{
		get => (int?)Parameters.TryGetValue(
			nameof(LiquidityToleranceBasisPoints));
		set => Parameters[nameof(LiquidityToleranceBasisPoints)] = value;
	}
}
