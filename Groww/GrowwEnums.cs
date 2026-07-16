namespace StockSharp.Groww;

/// <summary>Groww order products.</summary>
[DataContract]
[Serializable]
public enum GrowwProducts
{
	/// <summary>Cash and carry.</summary>
	[EnumMember(Value = "CNC")]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DeliveryKey)]
	Delivery,

	/// <summary>Margin intraday square-off.</summary>
	[EnumMember(Value = "MIS")]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.IntradayKey)]
	Intraday,

	/// <summary>Normal margin product.</summary>
	[EnumMember(Value = "NRML")]
	Normal,

	/// <summary>Margin trading facility.</summary>
	[EnumMember(Value = "MTF")]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MarginKey)]
	MarginTradingFacility,

	/// <summary>Arbitrage product.</summary>
	[EnumMember(Value = "ARB")]
	Arbitrage,

	/// <summary>Cover order product.</summary>
	[EnumMember(Value = "CO")]
	Cover,

	/// <summary>Bracket order product.</summary>
	[EnumMember(Value = "BO")]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BracketKey)]
	Bracket,
}

/// <summary>Groww order validities.</summary>
[DataContract]
[Serializable]
public enum GrowwValidities
{
	/// <summary>Day.</summary>
	[EnumMember(Value = "DAY")]
	Day,

	/// <summary>Immediate or cancel.</summary>
	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	/// <summary>End of session.</summary>
	[EnumMember(Value = "EOS")]
	EndOfSession,

	/// <summary>Good till cancelled.</summary>
	[EnumMember(Value = "GTC")]
	GoodTillCancelled,

	/// <summary>Good till date.</summary>
	[EnumMember(Value = "GTD")]
	GoodTillDate,
}
