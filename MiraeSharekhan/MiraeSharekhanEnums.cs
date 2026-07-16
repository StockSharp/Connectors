namespace StockSharp.MiraeSharekhan;

/// <summary>Mirae Asset Sharekhan order products.</summary>
[DataContract]
public enum MiraeSharekhanProducts
{
	/// <summary>Investment/delivery.</summary>
	[EnumMember]
	Investment,

	/// <summary>BigTrade.</summary>
	[EnumMember]
	BigTrade,

	/// <summary>BigTrade+.</summary>
	[EnumMember]
	BigTradePlus,
}

/// <summary>Mirae Asset Sharekhan native instrument types.</summary>
[DataContract]
public enum MiraeSharekhanInstrumentTypes
{
	/// <summary>Cash equity.</summary>
	[EnumMember]
	Equity,

	/// <summary>Stock future.</summary>
	[EnumMember]
	StockFuture,

	/// <summary>Index future.</summary>
	[EnumMember]
	IndexFuture,

	/// <summary>Stock option.</summary>
	[EnumMember]
	StockOption,

	/// <summary>Index option.</summary>
	[EnumMember]
	IndexOption,

	/// <summary>Currency future.</summary>
	[EnumMember]
	CurrencyFuture,

	/// <summary>Currency option.</summary>
	[EnumMember]
	CurrencyOption,

	/// <summary>Commodity future.</summary>
	[EnumMember]
	CommodityFuture,

	/// <summary>Commodity option.</summary>
	[EnumMember]
	CommodityOption,
}
