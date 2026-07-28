namespace StockSharp.MStock;

/// <summary>m.Stock order product.</summary>
[DataContract]
[Serializable]
public enum MStockProducts
{
	/// <summary>Cash-and-carry equity delivery.</summary>
	[EnumMember]
	Delivery,

	/// <summary>Intraday margin position.</summary>
	[EnumMember]
	Intraday,

	/// <summary>Margin delivery position.</summary>
	[EnumMember]
	Margin,

	/// <summary>Carry-forward derivatives position.</summary>
	[EnumMember]
	CarryForward,
}

/// <summary>m.Stock order variety.</summary>
[DataContract]
[Serializable]
public enum MStockOrderVarieties
{
	/// <summary>Regular order.</summary>
	[EnumMember]
	Normal,

	/// <summary>After-market order.</summary>
	[EnumMember]
	AMO,

	/// <summary>Stop-loss order.</summary>
	[EnumMember]
	StopLoss,
}

/// <summary>m.Stock-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MStockKey)]
public sealed class MStockOrderCondition : OrderCondition
{
	/// <summary>Order product.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ProductKey,
		Description = LocalizedStrings.ProductKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public MStockProducts? Product
	{
		get => (MStockProducts?)Parameters.TryGetValue(
			nameof(Product));
		set => Parameters[nameof(Product)] = value;
	}

	/// <summary>Order variety.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OrderTypeKey,
		Description = LocalizedStrings.OrderTypeKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public MStockOrderVarieties Variety
	{
		get => (MStockOrderVarieties?)Parameters.TryGetValue(
			nameof(Variety)) ?? MStockOrderVarieties.Normal;
		set => Parameters[nameof(Variety)] = value;
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

	/// <summary>Bracket-order square-off value.</summary>
	[DataMember]
	public decimal? SquareOff
	{
		get => (decimal?)Parameters.TryGetValue(nameof(SquareOff));
		set => Parameters[nameof(SquareOff)] = value;
	}

	/// <summary>Bracket-order stop-loss value.</summary>
	[DataMember]
	public decimal? StopLoss
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLoss));
		set => Parameters[nameof(StopLoss)] = value;
	}

	/// <summary>Trailing stop-loss value.</summary>
	[DataMember]
	public decimal? TrailingStopLoss
	{
		get => (decimal?)Parameters.TryGetValue(
			nameof(TrailingStopLoss));
		set => Parameters[nameof(TrailingStopLoss)] = value;
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

	/// <summary>Client order tag (maximum 20 characters).</summary>
	[DataMember]
	public string Tag
	{
		get => (string)Parameters.TryGetValue(nameof(Tag));
		set => Parameters[nameof(Tag)] = value;
	}
}
