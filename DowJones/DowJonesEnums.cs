namespace StockSharp.DowJones;

/// <summary>Dow Jones authentication schemes.</summary>
[DataContract]
public enum DowJonesAuthenticationModes
{
	/// <summary>OAuth Bearer token supplied by the user.</summary>
	[EnumMember]
	BearerToken,

	/// <summary>OAuth service-account credentials with automatic token renewal.</summary>
	[EnumMember]
	ServiceAccount,

	/// <summary>Legacy <c>user-key</c> authentication supported during migration.</summary>
	[EnumMember]
	UserKey,
}
