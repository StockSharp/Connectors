namespace StockSharp.Upstox;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Upstox-specific order parameters.
/// </summary>
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UpstoxKey)]
[Serializable]
[DataContract]
public class UpstoxOrderCondition : OrderCondition
{
	/// <summary>Order product.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ProductKey, Description = LocalizedStrings.ProductKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.GeneralKey, Order = 0)]
	[DataMember]
	public UpstoxProducts? Product
	{
		get => (UpstoxProducts?)Parameters.TryGetValue(nameof(Product));
		set => Parameters[nameof(Product)] = value;
	}

	/// <summary>Stop trigger price.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopPriceKey, Description = LocalizedStrings.StopPriceDescKey, GroupName = LocalizedStrings.StopLossKey, Order = 1)]
	[DataMember]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>Disclosed quantity.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.VisibleVolumeKey, Description = LocalizedStrings.VisibleVolumeDescKey, GroupName = LocalizedStrings.GeneralKey, Order = 2)]
	[DataMember]
	public decimal? DisclosedQuantity
	{
		get => (decimal?)Parameters.TryGetValue(nameof(DisclosedQuantity));
		set => Parameters[nameof(DisclosedQuantity)] = value;
	}

	/// <summary>After-market order.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UpstoxAfterMarketKey, Description = LocalizedStrings.UpstoxAfterMarketDescKey, GroupName = LocalizedStrings.GeneralKey, Order = 3)]
	[DataMember]
	public bool IsAfterMarket
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsAfterMarket)) == true;
		set => Parameters[nameof(IsAfterMarket)] = value;
	}

	/// <summary>Automatically slice orders above exchange freeze quantity.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UpstoxAutoSliceKey, Description = LocalizedStrings.UpstoxAutoSliceDescKey, GroupName = LocalizedStrings.GeneralKey, Order = 4)]
	[DataMember]
	public bool AutoSlice
	{
		get => (bool?)Parameters.TryGetValue(nameof(AutoSlice)) == true;
		set => Parameters[nameof(AutoSlice)] = value;
	}

	/// <summary>Market protection percentage.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UpstoxMarketProtectionKey, Description = LocalizedStrings.UpstoxMarketProtectionDescKey, GroupName = LocalizedStrings.GeneralKey, Order = 5)]
	[DataMember]
	public decimal? MarketProtection
	{
		get => (decimal?)Parameters.TryGetValue(nameof(MarketProtection));
		set => Parameters[nameof(MarketProtection)] = value;
	}
}
