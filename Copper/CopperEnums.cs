namespace StockSharp.Copper;

/// <summary>Copper Platform environments.</summary>
[DataContract]
public enum CopperEnvironments
{
	/// <summary>Production environment.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ProductionKey)]
	Production,

	/// <summary>Demonstration environment.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey)]
	Demo,

	/// <summary>Testnet environment.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TestnetKey)]
	Testnet,
}

/// <summary>Copper order destination types.</summary>
[DataContract]
public enum CopperDestinationTypes
{
	/// <summary>One-time blockchain address.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExternalAddressKey)]
	ExternalAddress,

	/// <summary>Approved Copper address-book entry.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressBookKey)]
	AddressBook,

	/// <summary>Another Copper or ClearLoop portfolio.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PortfolioKey)]
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
