namespace StockSharp.Zerodha;

/// <summary>Zerodha Kite order products.</summary>
[DataContract]
public enum ZerodhaProducts
{
	/// <summary>Cash and carry/delivery.</summary>
	[EnumMember]
	CashAndCarry,

	/// <summary>Normal carry-forward derivatives product.</summary>
	[EnumMember]
	Normal,

	/// <summary>Intraday margin product.</summary>
	[EnumMember]
	Intraday,

	/// <summary>Cover order product.</summary>
	[EnumMember]
	Cover,
}

/// <summary>Zerodha order varieties.</summary>
[DataContract]
public enum ZerodhaOrderVarieties
{
	/// <summary>Regular order.</summary>
	[EnumMember]
	Regular,

	/// <summary>After-market order.</summary>
	[EnumMember]
	AfterMarket,

	/// <summary>Cover order.</summary>
	[EnumMember]
	Cover,

	/// <summary>Iceberg order.</summary>
	[EnumMember]
	Iceberg,

	/// <summary>Auction order.</summary>
	[EnumMember]
	Auction,
}

/// <summary>Zerodha-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ZerodhaKey)]
public class ZerodhaOrderCondition : OrderCondition
{
	/// <summary>Margin product. The adapter default is used when empty.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ProductKey,
		Description = LocalizedStrings.ZerodhaProductDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public ZerodhaProducts? Product
	{
		get => Parameters.TryGetValue(nameof(Product))?.To<ZerodhaProducts?>();
		set => Parameters[nameof(Product)] = value;
	}

	/// <summary>Order variety.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ZerodhaOrderVarietyKey,
		Description = LocalizedStrings.ZerodhaOrderVarietyDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public ZerodhaOrderVarieties Variety
	{
		get => Parameters.TryGetValue(nameof(Variety))?.To<ZerodhaOrderVarieties?>() ??
			ZerodhaOrderVarieties.Regular;
		set => Parameters[nameof(Variety)] = value;
	}

	/// <summary>Stop trigger price.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public decimal? TriggerPrice
	{
		get => Parameters.TryGetValue(nameof(TriggerPrice))?.To<decimal?>();
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>Quantity disclosed to the market.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ZerodhaDisclosedQuantityKey,
		Description = LocalizedStrings.ZerodhaDisclosedQuantityDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 3)]
	public decimal? DisclosedQuantity
	{
		get => Parameters.TryGetValue(nameof(DisclosedQuantity))?.To<decimal?>();
		set => Parameters[nameof(DisclosedQuantity)] = value;
	}

	/// <summary>TTL validity in minutes.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ZerodhaValidityTtlKey,
		Description = LocalizedStrings.ZerodhaValidityTtlDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 4)]
	public int? ValidityTtl
	{
		get => Parameters.TryGetValue(nameof(ValidityTtl))?.To<int?>();
		set => Parameters[nameof(ValidityTtl)] = value;
	}

	/// <summary>Market-protection percentage. Use -1 for Zerodha automatic protection.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ZerodhaMarketProtectionKey,
		Description = LocalizedStrings.ZerodhaMarketProtectionDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 5)]
	public decimal? MarketProtection
	{
		get => Parameters.TryGetValue(nameof(MarketProtection))?.To<decimal?>();
		set => Parameters[nameof(MarketProtection)] = value;
	}

	/// <summary>Whether Zerodha may slice an order above exchange freeze limits.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ZerodhaAutoSliceKey,
		Description = LocalizedStrings.ZerodhaAutoSliceDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 6)]
	public bool IsAutoSlice
	{
		get => Parameters.TryGetValue(nameof(IsAutoSlice))?.To<bool>() == true;
		set => Parameters[nameof(IsAutoSlice)] = value;
	}
}
