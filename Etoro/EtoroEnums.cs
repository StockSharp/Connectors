namespace StockSharp.Etoro;

/// <summary>eToro settlement types.</summary>
[DataContract]
public enum EtoroSettlementTypes
{
	/// <summary>Contract for difference.</summary>
	[EnumMember(Value = "cfd")]
	Cfd,

	/// <summary>Real asset.</summary>
	[EnumMember(Value = "real")]
	Real,

	/// <summary>Real futures contract.</summary>
	[EnumMember(Value = "realFutures")]
	RealFutures,

	/// <summary>Margin trade.</summary>
	[EnumMember(Value = "marginTrade")]
	MarginTrade,
}

/// <summary>How <see cref="OrderRegisterMessage.Volume"/> is sent to eToro.</summary>
[DataContract]
public enum EtoroVolumeModes
{
	/// <summary>Asset units.</summary>
	[EnumMember]
	Units,

	/// <summary>Cash amount in <see cref="EtoroOrderCondition.OrderCurrency"/>.</summary>
	[EnumMember]
	Amount,

	/// <summary>Number of futures contracts.</summary>
	[EnumMember]
	Contracts,
}
