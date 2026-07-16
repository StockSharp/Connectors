namespace StockSharp.LsSecurities;

/// <summary>Native LS Securities cash-equity order price types.</summary>
[DataContract]
public enum LsOrderPriceTypes
{
	/// <summary>Limit order.</summary>
	[EnumMember]
	Limit,

	/// <summary>Market order.</summary>
	[EnumMember]
	Market,

	/// <summary>Conditional limit order.</summary>
	[EnumMember]
	ConditionalLimit,

	/// <summary>Best-price limit order.</summary>
	[EnumMember]
	BestLimit,

	/// <summary>Priority-price limit order.</summary>
	[EnumMember]
	PriorityLimit,
}

/// <summary>LS Securities execution venues used by unified cash-equity orders.</summary>
[DataContract]
public enum LsOrderMarkets
{
	/// <summary>Let LS Securities route the order.</summary>
	[EnumMember]
	Auto,

	/// <summary>Korea Exchange.</summary>
	[EnumMember]
	Krx,

	/// <summary>Nextrade.</summary>
	[EnumMember]
	Nxt,
}
