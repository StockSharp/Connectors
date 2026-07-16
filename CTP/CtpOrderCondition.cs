namespace StockSharp.Ctp;

using System.ComponentModel.DataAnnotations;

/// <summary>CTP-specific order parameters.</summary>
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CtpKey)]
[Serializable]
[DataContract]
public class CtpOrderCondition : OrderCondition
{
	/// <summary>Native price instruction. Leave empty to derive it from the StockSharp order type.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.OrderTypeKey, Description = LocalizedStrings.OrderTypeKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.GeneralKey, Order = 0)]
	[DataMember]
	public CtpOrderPriceTypes? PriceType
	{
		get => (CtpOrderPriceTypes?)Parameters.TryGetValue(nameof(PriceType));
		set => Parameters[nameof(PriceType)] = value;
	}

	/// <summary>Open or close instruction.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PositionEffectKey, Description = LocalizedStrings.PositionEffectKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.GeneralKey, Order = 1)]
	[DataMember]
	public CtpOffsetFlags Offset
	{
		get => (CtpOffsetFlags?)Parameters.TryGetValue(nameof(Offset)) ?? CtpOffsetFlags.Open;
		set => Parameters[nameof(Offset)] = value;
	}

	/// <summary>Speculation, arbitrage, hedge, or market-maker instruction.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CtpHedgeKey, Description = LocalizedStrings.CtpHedgeDescKey, GroupName = LocalizedStrings.GeneralKey, Order = 2)]
	[DataMember]
	public CtpHedgeFlags Hedge
	{
		get => (CtpHedgeFlags?)Parameters.TryGetValue(nameof(Hedge)) ?? CtpHedgeFlags.Speculation;
		set => Parameters[nameof(Hedge)] = value;
	}

	/// <summary>Native time condition.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TimeInForceKey, Description = LocalizedStrings.TimeInForceKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.GeneralKey, Order = 3)]
	[DataMember]
	public CtpTimeConditions? TimeCondition
	{
		get => (CtpTimeConditions?)Parameters.TryGetValue(nameof(TimeCondition));
		set => Parameters[nameof(TimeCondition)] = value;
	}

	/// <summary>Good-till date used with <see cref="CtpTimeConditions.GoodTillDate"/>.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.GoodTilDateKey, Description = LocalizedStrings.GoodTilDateKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.GeneralKey, Order = 4)]
	[DataMember]
	public DateTime? GoodTillDate
	{
		get => (DateTime?)Parameters.TryGetValue(nameof(GoodTillDate));
		set => Parameters[nameof(GoodTillDate)] = value;
	}

	/// <summary>Native volume condition.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.VolumeKey, Description = LocalizedStrings.VolumeKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.GeneralKey, Order = 5)]
	[DataMember]
	public CtpVolumeConditions? VolumeCondition
	{
		get => (CtpVolumeConditions?)Parameters.TryGetValue(nameof(VolumeCondition));
		set => Parameters[nameof(VolumeCondition)] = value;
	}

	/// <summary>Minimum executable volume.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MinVolumeKey, Description = LocalizedStrings.MinVolumeKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.GeneralKey, Order = 6)]
	[DataMember]
	public int? MinimumVolume
	{
		get => (int?)Parameters.TryGetValue(nameof(MinimumVolume));
		set => Parameters[nameof(MinimumVolume)] = value;
	}

	/// <summary>Native contingent trigger.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ConditionKey, Description = LocalizedStrings.ConditionKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.StopLossKey, Order = 7)]
	[DataMember]
	public CtpContingentConditions? ContingentCondition
	{
		get => (CtpContingentConditions?)Parameters.TryGetValue(nameof(ContingentCondition));
		set => Parameters[nameof(ContingentCondition)] = value;
	}

	/// <summary>Stop price for a contingent order.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopPriceKey, Description = LocalizedStrings.StopPriceDescKey, GroupName = LocalizedStrings.StopLossKey, Order = 8)]
	[DataMember]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>Force-close reason.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CtpForceCloseReasonKey, Description = LocalizedStrings.CtpForceCloseReasonDescKey, GroupName = LocalizedStrings.GeneralKey, Order = 9)]
	[DataMember]
	public CtpForceCloseReasons ForceCloseReason
	{
		get => (CtpForceCloseReasons?)Parameters.TryGetValue(nameof(ForceCloseReason)) ?? CtpForceCloseReasons.None;
		set => Parameters[nameof(ForceCloseReason)] = value;
	}
}
