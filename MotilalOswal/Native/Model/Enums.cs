namespace StockSharp.MotilalOswal;

/// <summary>Motilal Oswal order products.</summary>
[DataContract]
[Serializable]
public enum MotilalOswalProducts
{
	/// <summary>Normal carry-forward product.</summary>
	[EnumMember]
	Normal,

	/// <summary>Delivery product.</summary>
	[EnumMember]
	Delivery,

	/// <summary>Sell from depository.</summary>
	[EnumMember]
	SellFromDp,

	/// <summary>Value Plus intraday product.</summary>
	[EnumMember]
	ValuePlus,

	/// <summary>Buy today, sell tomorrow.</summary>
	[EnumMember]
	Btst,

	/// <summary>Margin trading facility.</summary>
	[EnumMember]
	Mtf,
}

/// <summary>Motilal Oswal order durations.</summary>
[DataContract]
[Serializable]
public enum MotilalOswalOrderDurations
{
	/// <summary>Valid for the current trading day.</summary>
	[EnumMember]
	Day,

	/// <summary>Good till cancelled.</summary>
	[EnumMember]
	GoodTillCancelled,

	/// <summary>Good till the specified date.</summary>
	[EnumMember]
	GoodTillDate,

	/// <summary>Immediate or cancel.</summary>
	[EnumMember]
	ImmediateOrCancel,
}
