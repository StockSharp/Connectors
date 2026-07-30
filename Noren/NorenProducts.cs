namespace StockSharp.Noren;

/// <summary>Order products supported by the Noren protocol.</summary>
[DataContract]
[Serializable]
public enum NorenProducts
{
	/// <summary>Delivery or cash-and-carry.</summary>
	[EnumMember]
	Delivery,

	/// <summary>Intraday.</summary>
	[EnumMember]
	Intraday,

	/// <summary>Normal carry-forward product.</summary>
	[EnumMember]
	Normal,

	/// <summary>Cover order.</summary>
	[EnumMember]
	Cover,

	/// <summary>Bracket order.</summary>
	[EnumMember]
	Bracket,
}
