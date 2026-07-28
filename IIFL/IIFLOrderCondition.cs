namespace StockSharp.IIFL;

/// <summary>IIFL order product.</summary>
[DataContract]
[Serializable]
public enum IIFLProducts
{
	/// <summary>Carry-forward derivatives position.</summary>
	[EnumMember]
	Normal,

	/// <summary>Intraday position.</summary>
	[EnumMember]
	Intraday,

	/// <summary>Delivery equity position.</summary>
	[EnumMember]
	Delivery,

	/// <summary>Buy now, pay later.</summary>
	[EnumMember]
	BNPL,
}

/// <summary>IIFL order complexity.</summary>
[DataContract]
[Serializable]
public enum IIFLOrderComplexities
{
	/// <summary>Regular order.</summary>
	[EnumMember]
	Regular,

	/// <summary>After-market order.</summary>
	[EnumMember]
	AMO,

	/// <summary>Bracket order.</summary>
	[EnumMember]
	BO,

	/// <summary>Cover order.</summary>
	[EnumMember]
	CO,
}

/// <summary>IIFL-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.IIFLKey)]
public sealed class IIFLOrderCondition : OrderCondition
{
	/// <summary>IIFL order product.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ProductKey,
		Description = LocalizedStrings.ProductKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public IIFLProducts? Product
	{
		get => (IIFLProducts?)Parameters.TryGetValue(nameof(Product));
		set => Parameters[nameof(Product)] = value;
	}

	/// <summary>IIFL order complexity.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OrderTypeKey,
		Description = LocalizedStrings.OrderTypeKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public IIFLOrderComplexities Complexity
	{
		get => (IIFLOrderComplexities?)Parameters.TryGetValue(
			nameof(Complexity)) ?? IIFLOrderComplexities.Regular;
		set => Parameters[nameof(Complexity)] = value;
	}

	/// <summary>Stop-loss trigger price.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TriggerPriceKey,
		Description = LocalizedStrings.TriggerPriceForSlOrSlmOrdersDescKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 2)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>Bracket-order stop-loss leg price.</summary>
	[DataMember]
	public decimal? StopLossLegPrice
	{
		get => (decimal?)Parameters.TryGetValue(
			nameof(StopLossLegPrice));
		set => Parameters[nameof(StopLossLegPrice)] = value;
	}

	/// <summary>Bracket-order target leg price.</summary>
	[DataMember]
	public decimal? TargetLegPrice
	{
		get => (decimal?)Parameters.TryGetValue(
			nameof(TargetLegPrice));
		set => Parameters[nameof(TargetLegPrice)] = value;
	}

	/// <summary>Quantity disclosed to the market.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DisclosedVolumeKey,
		Description = LocalizedStrings.DisclosedVolumeKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 3)]
	public decimal? DisclosedVolume
	{
		get => (decimal?)Parameters.TryGetValue(
			nameof(DisclosedVolume));
		set => Parameters[nameof(DisclosedVolume)] = value;
	}

	/// <summary>Market-protection percentage.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketProtectionKey,
		Description = LocalizedStrings.MarketProtectionKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 4)]
	public decimal? MarketProtectionPercent
	{
		get => (decimal?)Parameters.TryGetValue(
			nameof(MarketProtectionPercent));
		set => Parameters[nameof(MarketProtectionPercent)] = value;
	}

	/// <summary>Exchange-registered algorithm identifier.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AlgoIdKey,
		Description = LocalizedStrings.AlgoIdKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 5)]
	public string AlgoId
	{
		get => (string)Parameters.TryGetValue(nameof(AlgoId));
		set => Parameters[nameof(AlgoId)] = value;
	}

	/// <summary>Client order tag.</summary>
	[DataMember]
	public string Tag
	{
		get => (string)Parameters.TryGetValue(nameof(Tag));
		set => Parameters[nameof(Tag)] = value;
	}
}
