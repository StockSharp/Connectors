namespace StockSharp.Fireblocks;

/// <summary>Fireblocks cloud environments.</summary>
[DataContract]
public enum FireblocksEnvironments
{
	/// <summary>US mainnet or testnet workspace.</summary>
	[EnumMember]
	[Display(Name = "US")]
	Us,

	/// <summary>EU mainnet or testnet workspace.</summary>
	[EnumMember]
	[Display(Name = "EU")]
	Eu,

	/// <summary>EU2 mainnet or testnet workspace.</summary>
	[EnumMember]
	[Display(Name = "EU2")]
	Eu2,

	/// <summary>US sandbox workspace.</summary>
	[EnumMember]
	[Display(Name = "Sandbox")]
	Sandbox,
}

/// <summary>Fireblocks transaction peer types.</summary>
[DataContract]
[JsonConverter(typeof(FireblocksEnumConverter<FireblocksPeerTypes>))]
public enum FireblocksPeerTypes
{
	/// <summary>Unknown peer.</summary>
	[EnumMember(Value = "UNKNOWN")]
	Unknown,

	/// <summary>Vault account.</summary>
	[EnumMember(Value = "VAULT_ACCOUNT")]
	VaultAccount,

	/// <summary>Exchange account.</summary>
	[EnumMember(Value = "EXCHANGE_ACCOUNT")]
	ExchangeAccount,

	/// <summary>Connected account.</summary>
	[EnumMember(Value = "CONNECTED_ACCOUNT")]
	ConnectedAccount,

	/// <summary>Internal wallet.</summary>
	[EnumMember(Value = "INTERNAL_WALLET")]
	InternalWallet,

	/// <summary>External wallet.</summary>
	[EnumMember(Value = "EXTERNAL_WALLET")]
	ExternalWallet,

	/// <summary>Unmanaged wallet.</summary>
	[EnumMember(Value = "UNMANAGED_WALLET")]
	UnmanagedWallet,

	/// <summary>Contract.</summary>
	[EnumMember(Value = "CONTRACT")]
	Contract,

	/// <summary>Network connection.</summary>
	[EnumMember(Value = "NETWORK_CONNECTION")]
	NetworkConnection,

	/// <summary>Fiat account.</summary>
	[EnumMember(Value = "FIAT_ACCOUNT")]
	FiatAccount,

	/// <summary>Compound account.</summary>
	[EnumMember(Value = "COMPOUND")]
	Compound,

	/// <summary>Gas station.</summary>
	[EnumMember(Value = "GAS_STATION")]
	GasStation,

	/// <summary>One-time blockchain address.</summary>
	[EnumMember(Value = "ONE_TIME_ADDRESS")]
	OneTimeAddress,

	/// <summary>End-user wallet.</summary>
	[EnumMember(Value = "END_USER_WALLET")]
	EndUserWallet,

	/// <summary>Solana program call.</summary>
	[EnumMember(Value = "PROGRAM_CALL")]
	ProgramCall,

	/// <summary>Multiple destinations.</summary>
	[EnumMember(Value = "MULTI_DESTINATION")]
	MultiDestination,

	/// <summary>Open exchange connectivity partner.</summary>
	[EnumMember(Value = "OEC_PARTNER")]
	OecPartner,

	/// <summary>Wallet pool.</summary>
	[EnumMember(Value = "WALLET_POOL")]
	WalletPool,
}

/// <summary>Fireblocks network fee levels.</summary>
[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
public enum FireblocksFeeLevels
{
	/// <summary>Low fee.</summary>
	[EnumMember(Value = "LOW")]
	Low,

	/// <summary>Medium fee.</summary>
	[EnumMember(Value = "MEDIUM")]
	Medium,

	/// <summary>High fee.</summary>
	[EnumMember(Value = "HIGH")]
	High,
}

static class FireblocksEnvironmentExtensions
{
	public static string GetApiEndpoint(this FireblocksEnvironments environment)
		=> environment switch
		{
			FireblocksEnvironments.Us => "https://api.fireblocks.io/v1",
			FireblocksEnvironments.Eu => "https://eu-api.fireblocks.io/v1",
			FireblocksEnvironments.Eu2 => "https://eu2-api.fireblocks.io/v1",
			FireblocksEnvironments.Sandbox =>
				"https://sandbox-api.fireblocks.io/v1",
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};
}
