namespace StockSharp.Flattrade;

/// <summary>Flattrade order products.</summary>
[DataContract]
[Serializable]
public enum FlattradeProducts
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

	/// <summary>Margin trading facility.</summary>
	[EnumMember]
	MarginTrading,

	/// <summary>Cover order.</summary>
	[EnumMember]
	Cover,

	/// <summary>Bracket order.</summary>
	[EnumMember]
	Bracket,
}
