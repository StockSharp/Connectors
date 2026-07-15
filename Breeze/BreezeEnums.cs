namespace StockSharp.Breeze;

/// <summary>ICICI Direct Breeze order products.</summary>
public enum BreezeProducts
{
	/// <summary>Cash delivery.</summary>
	[EnumMember(Value = "cash")]
	Cash,

	/// <summary>Futures.</summary>
	[EnumMember(Value = "futures")]
	Futures,

	/// <summary>Options.</summary>
	[EnumMember(Value = "options")]
	Options,
}
