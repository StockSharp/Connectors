namespace StockSharp.CQG;

/// <summary>Maximum real-time quote collapsing allowed by the client.</summary>
[DataContract]
public enum CqgCollapsingLevels
{
	/// <summary>Do not permit quote collapsing.</summary>
	[EnumMember]
	None = 0,

	/// <summary>Permit DOM collapsing.</summary>
	[EnumMember]
	MarketDepth = 1,

	/// <summary>Permit DOM and best bid/ask collapsing.</summary>
	[EnumMember]
	MarketDepthAndBestQuotes = 2,

	/// <summary>Permit DOM, best quote, and trade collapsing.</summary>
	[EnumMember]
	All = 3,
}

/// <summary>CQG native order types.</summary>
[DataContract]
public enum CqgOrderTypes
{
	/// <summary>Market.</summary>
	[EnumMember]
	Market = 1,

	/// <summary>Limit.</summary>
	[EnumMember]
	Limit = 2,

	/// <summary>Stop market.</summary>
	[EnumMember]
	Stop = 3,

	/// <summary>Stop limit.</summary>
	[EnumMember]
	StopLimit = 4,
}

/// <summary>CQG order duration.</summary>
[DataContract]
public enum CqgOrderDurations
{
	/// <summary>Day.</summary>
	[EnumMember]
	Day = 1,

	/// <summary>Good till canceled.</summary>
	[EnumMember]
	GoodTillCanceled = 2,

	/// <summary>Good till date.</summary>
	[EnumMember]
	GoodTillDate = 3,

	/// <summary>Good till time.</summary>
	[EnumMember]
	GoodTillTime = 4,

	/// <summary>Immediate or cancel.</summary>
	[EnumMember]
	ImmediateOrCancel = 5,

	/// <summary>Fill or kill.</summary>
	[EnumMember]
	FillOrKill = 6,
}

/// <summary>CQG execution instructions.</summary>
[Flags]
[DataContract]
public enum CqgExecutionInstructions
{
	/// <summary>No additional instruction.</summary>
	None = 0,

	/// <summary>All or none.</summary>
	AllOrNone = 1,

	/// <summary>Iceberg.</summary>
	Iceberg = 2,

	/// <summary>Quantity triggered.</summary>
	QuantityTriggered = 4,

	/// <summary>Trailing.</summary>
	Trailing = 8,

	/// <summary>Market if touched.</summary>
	MarketIfTouched = 16,

	/// <summary>Post only.</summary>
	PostOnly = 32,
}
