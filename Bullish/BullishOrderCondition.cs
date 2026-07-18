namespace StockSharp.Bullish;

/// <summary>
/// Bullish order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BullishKey)]
public class BullishOrderCondition : OrderCondition
{
	/// <summary>
	/// Trigger price for a stop-limit order.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceKey, GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>
	/// Allow the exchange to borrow assets for this order.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.IsMarginKey,
		Description = LocalizedStrings.IsMarginKey, GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public bool IsBorrowAllowed
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsBorrowAllowed)) ?? false;
		set => Parameters[nameof(IsBorrowAllowed)] = value;
	}

	/// <summary>
	/// Apply Bullish market-maker protection to an option order.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PosProtectionKey,
		Description = LocalizedStrings.PosProtectionKey, GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public bool IsMarketMakerProtection
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsMarketMakerProtection)) ?? false;
		set => Parameters[nameof(IsMarketMakerProtection)] = value;
	}

	/// <summary>
	/// Submit the order to an auction using good-till-crossing time in force.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ExtendedOrderTypeKey,
		Description = LocalizedStrings.ExtendedOrderTypeDescKey, GroupName = LocalizedStrings.ParametersKey, Order = 3)]
	public bool IsAuction
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsAuction)) ?? false;
		set => Parameters[nameof(IsAuction)] = value;
	}
}
