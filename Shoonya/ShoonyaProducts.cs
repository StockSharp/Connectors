namespace StockSharp.Shoonya;

/// <summary>Shoonya order products.</summary>
[DataContract]
[Serializable]
public enum ShoonyaProducts
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
