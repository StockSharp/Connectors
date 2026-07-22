namespace StockSharp.Groww;

/// <summary>Groww-specific order parameters.</summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.GrowwKey)]
[Serializable]
[DataContract]
public class GrowwOrderCondition : OrderCondition
{
	/// <summary>Order product.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ProductKey,
		Description = LocalizedStrings.ProductKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	[DataMember]
	public GrowwProducts? Product
	{
		get => (GrowwProducts?)Parameters.TryGetValue(nameof(Product));
		set => Parameters[nameof(Product)] = value;
	}

	/// <summary>Stop trigger price.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.StopLossKey,
		Order = 1)]
	[DataMember]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>Native order validity.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeInForceKey,
		Description = LocalizedStrings.TimeInForceKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 2)]
	[DataMember]
	public GrowwValidities? Validity
	{
		get => (GrowwValidities?)Parameters.TryGetValue(nameof(Validity));
		set => Parameters[nameof(Validity)] = value;
	}
}
