namespace StockSharp.Copper;

/// <summary>Copper Platform environments.</summary>
[DataContract]
public enum CopperEnvironments
{
	/// <summary>Production environment.</summary>
	[EnumMember]
	[Display(Name = "Production")]
	Production,

	/// <summary>Demonstration environment.</summary>
	[EnumMember]
	[Display(Name = "Demo")]
	Demo,

	/// <summary>Testnet environment.</summary>
	[EnumMember]
	[Display(Name = "Testnet")]
	Testnet,
}

/// <summary>Copper order destination types.</summary>
[DataContract]
public enum CopperDestinationTypes
{
	/// <summary>One-time blockchain address.</summary>
	[EnumMember]
	[Display(Name = "External address")]
	ExternalAddress,

	/// <summary>Approved Copper address-book entry.</summary>
	[EnumMember]
	[Display(Name = "Address book")]
	AddressBook,

	/// <summary>Another Copper or ClearLoop portfolio.</summary>
	[EnumMember]
	[Display(Name = "Portfolio")]
	Portfolio,
}

/// <summary>Copper blockchain fee levels.</summary>
[DataContract]
[JsonConverter(typeof(CopperEnumConverter<CopperFeeLevels>))]
public enum CopperFeeLevels
{
	/// <summary>Unspecified fee level.</summary>
	[EnumMember(Value = "unknown")]
	Unknown,

	/// <summary>Low fee.</summary>
	[EnumMember(Value = "low")]
	Low,

	/// <summary>Medium fee.</summary>
	[EnumMember(Value = "medium")]
	Medium,

	/// <summary>High fee.</summary>
	[EnumMember(Value = "high")]
	High,
}

static class CopperEnvironmentExtensions
{
	public static string GetApiEndpoint(this CopperEnvironments environment)
		=> environment switch
		{
			CopperEnvironments.Production => "https://api.copper.co/platform",
			CopperEnvironments.Demo => "https://api.stage.copper.co/platform",
			CopperEnvironments.Testnet =>
				"https://api.testnet.copper.co/platform",
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};
}
