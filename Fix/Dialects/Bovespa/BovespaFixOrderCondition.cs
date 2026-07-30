namespace StockSharp.Fix.Dialects.Bovespa;

using System.Runtime.Serialization;

/// <summary>
/// Types.
/// </summary>
public enum BovespaFixOrderTypes
{
	/// <summary>
	/// Market with leftover as limit.
	/// </summary>
	MarketLeftOverLimit,

	/// <summary>
	/// Retail liquidity provider.
	/// </summary>
	RetailLiquidityProvider,
}

/// <summary>
/// Types.
/// </summary>
public enum BovespaFixTimeInForce
{
	/// <summary>
	/// At close.
	/// </summary>
	AtClose,

	/// <summary>
	/// Good for auction.
	/// </summary>
	GoodForAuction,
}

/// <summary>
/// B3 BM&amp;F Bovespa FIX order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BrasilBolsaKey)]
public class BovespaFixOrderCondition : FixOrderCondition
{
	/// <summary>
	/// Resets Market Protections.
	/// </summary>
	[DataMember]
	public bool? MarketProtectionReset
	{
		get => (bool?)Parameters.TryGetValue(nameof(MarketProtectionReset));
		set => Parameters[nameof(MarketProtectionReset)] = value;
	}

	/// <summary>
	/// Indicates additional order instruction.
	/// </summary>
	[DataMember]
	public bool? IsRetailLiquidity
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsRetailLiquidity));
		set => Parameters[nameof(IsRetailLiquidity)] = value;
	}

	/// <summary>
	/// Type.
	/// </summary>
	[DataMember]
	public BovespaFixOrderTypes? TypeEx
	{
		get => (BovespaFixOrderTypes?)Parameters.TryGetValue(nameof(TypeEx));
		set => Parameters[nameof(TypeEx)] = value;
	}

	/// <summary>
	/// Time in force.
	/// </summary>
	[DataMember]
	public BovespaFixTimeInForce? TimeInForce
	{
		get => (BovespaFixTimeInForce?)Parameters.TryGetValue(nameof(TimeInForce));
		set => Parameters[nameof(TimeInForce)] = value;
	}
}