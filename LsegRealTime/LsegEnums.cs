namespace StockSharp.LsegRealTime;

using System.Runtime.Serialization;

/// <summary>LSEG Real-Time authentication modes.</summary>
[DataContract]
public enum LsegAuthenticationModes
{
	/// <summary>Deployed RTDS login using a DACS user name.</summary>
	[EnumMember]
	Deployed,

	/// <summary>LSEG Delivery Platform OAuth v1 password grant.</summary>
	[EnumMember]
	PasswordGrant,

	/// <summary>LSEG Delivery Platform OAuth v2 client credentials.</summary>
	[EnumMember]
	ClientCredentials,
}
