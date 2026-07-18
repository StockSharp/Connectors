namespace StockSharp.Quodd;

/// <summary>QUODD authentication modes.</summary>
[DataContract]
public enum QuoddAuthenticationModes
{
	/// <summary>Use an already issued JWT.</summary>
	[EnumMember]
	Token,

	/// <summary>Obtain a JWT with trial user credentials.</summary>
	[EnumMember]
	Trial,

	/// <summary>Obtain a JWT with user and firm credentials.</summary>
	[EnumMember]
	Firm,
}

enum QuoddAssetTypes
{
	Equities,
	Options,
}
