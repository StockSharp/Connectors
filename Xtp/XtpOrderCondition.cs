namespace StockSharp.Xtp;

using System.ComponentModel.DataAnnotations;

/// <summary>XTP-specific order parameters.</summary>
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.XtpKey)]
[Serializable]
[DataContract]
public class XtpOrderCondition : OrderCondition
{
	/// <summary>Native price instruction.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.OrderTypeKey, Description = LocalizedStrings.OrderTypeKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.GeneralKey, Order = 0)]
	[DataMember]
	public XtpPriceTypes? PriceType
	{
		get => (XtpPriceTypes?)Parameters.TryGetValue(nameof(PriceType));
		set => Parameters[nameof(PriceType)] = value;
	}

	/// <summary>Native side instruction for subscriptions, ETF, margin, collateral, and option-combination operations.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SideKey, Description = LocalizedStrings.XtpNativeSideDescKey, GroupName = LocalizedStrings.GeneralKey, Order = 1)]
	[DataMember]
	public XtpOrderSides? NativeSide
	{
		get => (XtpOrderSides?)Parameters.TryGetValue(nameof(NativeSide));
		set => Parameters[nameof(NativeSide)] = value;
	}

	/// <summary>Position effect for option orders.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PositionEffectKey, Description = LocalizedStrings.PositionEffectKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.GeneralKey, Order = 2)]
	[DataMember]
	public XtpPositionEffects PositionEffect
	{
		get => (XtpPositionEffects?)Parameters.TryGetValue(nameof(PositionEffect)) ?? XtpPositionEffects.None;
		set => Parameters[nameof(PositionEffect)] = value;
	}

	/// <summary>Business instruction.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BusinessTypeKey, Description = LocalizedStrings.BusinessTypeKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.GeneralKey, Order = 3)]
	[DataMember]
	public XtpBusinessTypes BusinessType
	{
		get => (XtpBusinessTypes?)Parameters.TryGetValue(nameof(BusinessType)) ?? XtpBusinessTypes.Cash;
		set => Parameters[nameof(BusinessType)] = value;
	}

	/// <summary>Stop price reserved by XTP for compatible order instructions.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopPriceKey, Description = LocalizedStrings.StopPriceDescKey, GroupName = LocalizedStrings.StopLossKey, Order = 4)]
	[DataMember]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}
}
