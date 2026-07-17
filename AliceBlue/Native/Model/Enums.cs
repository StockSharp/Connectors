namespace StockSharp.AliceBlue;

/// <summary>Alice Blue order products.</summary>
[DataContract]
[Serializable]
public enum AliceBlueProducts
{
	/// <summary>Delivery or long-term product.</summary>
	[EnumMember]
	LongTerm,

	/// <summary>Intraday product.</summary>
	[EnumMember]
	Intraday,

	/// <summary>Margin trading facility.</summary>
	[EnumMember]
	Mtf,
}

/// <summary>Alice Blue order complexities.</summary>
[DataContract]
[Serializable]
public enum AliceBlueOrderComplexities
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

	/// <summary>Bracket order.</summary>
	[EnumMember]
	Bracket,
}
