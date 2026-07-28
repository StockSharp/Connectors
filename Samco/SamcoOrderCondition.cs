namespace StockSharp.Samco;

/// <summary>Samco order product.</summary>
[DataContract]
[Serializable]
public enum SamcoProducts
{
	/// <summary>Cash and carry.</summary>
	[EnumMember]
	CNC,

	/// <summary>Intraday.</summary>
	[EnumMember]
	MIS,

	/// <summary>Normal carry-forward derivatives.</summary>
	[EnumMember]
	NRML,
}

/// <summary>Samco-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SamcoKey)]
public sealed class SamcoOrderCondition : OrderCondition
{
	/// <summary>Order product.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ProductKey,
		Description = LocalizedStrings.ProductKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public SamcoProducts? Product
	{
		get => (SamcoProducts?)Parameters.TryGetValue(
			nameof(Product));
		set => Parameters[nameof(Product)] = value;
	}

	/// <summary>Submit as an after-market order.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AfterMarketKey,
		Description = LocalizedStrings.AfterMarketKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public bool AfterMarketOrder
	{
		get => (bool?)Parameters.TryGetValue(
			nameof(AfterMarketOrder)) ?? false;
		set => Parameters[nameof(AfterMarketOrder)] = value;
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
		get => (decimal?)Parameters.TryGetValue(
			nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
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
}
