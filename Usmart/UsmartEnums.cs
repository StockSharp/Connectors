namespace StockSharp.Usmart;

/// <summary>Native uSMART order instruction.</summary>
[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
public enum UsmartOrderInstructions
{
	/// <summary>Limit order.</summary>
	[EnumMember(Value = "0")]
	Limit,

	/// <summary>Enhanced limit order.</summary>
	[EnumMember(Value = "e")]
	EnhancedLimit,

	/// <summary>At-auction order.</summary>
	[EnumMember(Value = "d")]
	Auction,

	/// <summary>At-auction limit order.</summary>
	[EnumMember(Value = "g")]
	AuctionLimit,

	/// <summary>Market order.</summary>
	[EnumMember(Value = "w")]
	Market,
}

/// <summary>Native uSMART U.S. trading session.</summary>
[DataContract]
public enum UsmartTradingSessions
{
	/// <summary>Regular session.</summary>
	[EnumMember(Value = "0")]
	Regular,

	/// <summary>Pre-market session.</summary>
	[EnumMember(Value = "1")]
	PreMarket,

	/// <summary>Post-market session.</summary>
	[EnumMember(Value = "2")]
	PostMarket,

	/// <summary>Dark-pool session.</summary>
	[EnumMember(Value = "3")]
	DarkPool,
}
