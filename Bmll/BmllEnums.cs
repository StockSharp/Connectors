namespace StockSharp.Bmll;

/// <summary>BMLL authentication modes.</summary>
public enum BmllAuthenticationModes
{
	/// <summary>Sign the BMLL authentication challenge with an RSA private key.</summary>
	[Display(Name = "SSH key")]
	SshKey,

	/// <summary>Use a previously issued bearer token.</summary>
	[Display(Name = "Bearer token")]
	BearerToken,
}

enum BmllQueryStatuses
{
	Unknown,
	Running,
	Processing,
	Success,
	Failed,
	Cancelled,
}

enum BmllDataKinds
{
	Trades,
	Level3,
}

enum BmllLobActions
{
	Unknown = 0,
	Insert = 2,
	Remove = 3,
	Update = 4,
}

enum BmllSides
{
	Bid = 1,
	Ask = 2,
}

enum BmllAggressorSides
{
	Buy = 1,
	Sell = 2,
}

enum BmllOrderTypes
{
	Unknown = 0,
	Limit = 1,
	Market = 2,
}
