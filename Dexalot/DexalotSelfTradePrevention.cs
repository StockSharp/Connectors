namespace StockSharp.Dexalot;

using System.Runtime.Serialization;

/// <summary>Dexalot self-trade prevention mode.</summary>
[DataContract]
public enum DexalotSelfTradePrevention
{
	/// <summary>Cancel the incoming taker order.</summary>
	[EnumMember]
	CancelTaker,

	/// <summary>Cancel the resting maker order.</summary>
	[EnumMember]
	CancelMaker,

	/// <summary>Cancel both orders.</summary>
	[EnumMember]
	CancelBoth,

	/// <summary>Allow self trades.</summary>
	[EnumMember]
	None,
}
