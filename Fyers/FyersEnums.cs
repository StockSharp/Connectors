namespace StockSharp.Fyers;

/// <summary>FYERS order products.</summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum FyersProducts
{
	/// <summary>Cash and carry.</summary>
	[EnumMember(Value = "CNC")]
	Delivery,

	/// <summary>Intraday.</summary>
	[EnumMember(Value = "INTRADAY")]
	Intraday,

	/// <summary>Carry-forward margin.</summary>
	[EnumMember(Value = "MARGIN")]
	Margin,

	/// <summary>Margin trading facility.</summary>
	[EnumMember(Value = "MTF")]
	MarginTradingFacility,

	/// <summary>Cover order.</summary>
	[EnumMember(Value = "CO")]
	Cover,

	/// <summary>Bracket order.</summary>
	[EnumMember(Value = "BO")]
	Bracket,
}
