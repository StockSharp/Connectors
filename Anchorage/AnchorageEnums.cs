namespace StockSharp.Anchorage;

/// <summary>Anchorage Digital API environments.</summary>
[DataContract]
public enum AnchorageEnvironments
{
	/// <summary>Production environment.</summary>
	[EnumMember]
	[Display(Name = "Production")]
	Production,

	/// <summary>Staging sandbox environment.</summary>
	[EnumMember]
	[Display(Name = "Staging")]
	Staging,
}

/// <summary>Anchorage operation kinds.</summary>
[DataContract]
public enum AnchorageOperations
{
	/// <summary>Trading order.</summary>
	[EnumMember]
	Trade,

	/// <summary>Automated transfer.</summary>
	[EnumMember]
	Transfer,

	/// <summary>Quorum-approved withdrawal.</summary>
	[EnumMember]
	Withdrawal,

	/// <summary>Stake assets.</summary>
	[EnumMember]
	Stake,

	/// <summary>Unstake assets.</summary>
	[EnumMember]
	Unstake,
}

/// <summary>Anchorage resource kinds.</summary>
[DataContract]
[JsonConverter(typeof(AnchorageEnumConverter<AnchorageResourceTypes>))]
public enum AnchorageResourceTypes
{
	/// <summary>Unknown resource.</summary>
	[EnumMember(Value = "UNKNOWN")]
	Unknown,

	/// <summary>Vault.</summary>
	[EnumMember(Value = "VAULT")]
	Vault,

	/// <summary>Wallet.</summary>
	[EnumMember(Value = "WALLET")]
	Wallet,

	/// <summary>Approved address resource.</summary>
	[EnumMember(Value = "ADDRESS")]
	Address,

	/// <summary>Standing instruction.</summary>
	[EnumMember(Value = "STANDING_INSTRUCTION")]
	StandingInstruction,

	/// <summary>Trusted destination.</summary>
	[EnumMember(Value = "TRUSTED_DESTINATION")]
	TrustedDestination,

	/// <summary>RIA subaccount.</summary>
	[EnumMember(Value = "SUBACCOUNT")]
	Subaccount,
}

/// <summary>Anchorage native trading order types.</summary>
[DataContract]
[JsonConverter(typeof(AnchorageEnumConverter<AnchorageNativeOrderTypes>))]
public enum AnchorageNativeOrderTypes
{
	/// <summary>Unknown type.</summary>
	[EnumMember(Value = "UNKNOWN")]
	Unknown,

	/// <summary>Market order.</summary>
	[EnumMember(Value = "MARKET")]
	Market,

	/// <summary>Limit order.</summary>
	[EnumMember(Value = "LIMIT")]
	Limit,

	/// <summary>Stop-loss order.</summary>
	[EnumMember(Value = "STOP_LOSS")]
	StopLoss,

	/// <summary>Stop-limit order.</summary>
	[EnumMember(Value = "STOP_LIMIT")]
	StopLimit,

	/// <summary>Take-profit limit order.</summary>
	[EnumMember(Value = "TAKE_PROFIT_LIMIT")]
	TakeProfitLimit,

	/// <summary>Time-weighted average price strategy.</summary>
	[EnumMember(Value = "TWAP")]
	Twap,

	/// <summary>Volume-weighted average price strategy.</summary>
	[EnumMember(Value = "VWAP")]
	Vwap,

	/// <summary>Pegged strategy.</summary>
	[EnumMember(Value = "PEGGED")]
	Pegged,

	/// <summary>Percentage-of-volume strategy.</summary>
	[EnumMember(Value = "POV")]
	Pov,

	/// <summary>Request-for-quote order.</summary>
	[EnumMember(Value = "RFQ")]
	Rfq,

	/// <summary>All-in limit order.</summary>
	[EnumMember(Value = "LIMIT_ALL_IN")]
	LimitAllIn,

	/// <summary>Manually entered order.</summary>
	[EnumMember(Value = "MANUAL")]
	Manual,

	/// <summary>Other historical order type.</summary>
	[EnumMember(Value = "OTHER")]
	Other,
}

/// <summary>Anchorage staking providers.</summary>
[DataContract]
[JsonConverter(typeof(AnchorageEnumConverter<AnchorageStakingProviders>))]
public enum AnchorageStakingProviders
{
	/// <summary>Unspecified provider.</summary>
	[EnumMember(Value = "UNKNOWN")]
	Unknown,

	/// <summary>Figment.</summary>
	[EnumMember(Value = "FIGMENT")]
	Figment,

	/// <summary>Blockdaemon.</summary>
	[EnumMember(Value = "BLOCKDAEMON")]
	Blockdaemon,
}

/// <summary>Ethereum validator withdrawal credential types.</summary>
[DataContract]
[JsonConverter(typeof(AnchorageEnumConverter<AnchorageValidatorTypes>))]
public enum AnchorageValidatorTypes
{
	/// <summary>Unspecified validator type.</summary>
	[EnumMember(Value = "UNKNOWN")]
	Unknown,

	/// <summary>Legacy BLS withdrawal credential.</summary>
	[EnumMember(Value = "0x01")]
	Legacy,

	/// <summary>Pectra compounding withdrawal credential.</summary>
	[EnumMember(Value = "0x02")]
	Compounding,
}

static class AnchorageEnvironmentExtensions
{
	public static string GetApiEndpoint(this AnchorageEnvironments environment)
		=> environment switch
		{
			AnchorageEnvironments.Production => "https://api.anchorage.com/v2",
			AnchorageEnvironments.Staging =>
				"https://api.anchorage-staging.com/v2",
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};

	public static string GetSocketEndpoint(
		this AnchorageEnvironments environment)
		=> environment switch
		{
			AnchorageEnvironments.Production =>
				"wss://api.anchorage.com/ws/v2/trading",
			AnchorageEnvironments.Staging =>
				"wss://api.anchorage-staging.com/ws/v2/trading",
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};
}
