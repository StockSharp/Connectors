namespace StockSharp.Paxos;

/// <summary>Paxos API environments.</summary>
[DataContract]
public enum PaxosEnvironments
{
	/// <summary>Production environment.</summary>
	[EnumMember]
	[Display(
		Name = "Production")]
	Production,

	/// <summary>Sandbox environment.</summary>
	[EnumMember]
	[Display(
		Name = "Sandbox")]
	Sandbox,
}

/// <summary>Paxos trading and custody operations.</summary>
[DataContract]
public enum PaxosOperations
{
	/// <summary>Brokerage order.</summary>
	[EnumMember]
	Trade,

	/// <summary>Crypto withdrawal to an external address.</summary>
	[EnumMember]
	CryptoWithdrawal,

	/// <summary>Transfer between profiles owned by the same customer.</summary>
	[EnumMember]
	InternalTransfer,

	/// <summary>Transfer to another Paxos customer profile.</summary>
	[EnumMember]
	PaxosTransfer,

	/// <summary>Legacy fiat/stablecoin conversion.</summary>
	[EnumMember]
	StablecoinConversion,
}

/// <summary>Paxos crypto networks.</summary>
[DataContract]
[JsonConverter(typeof(PaxosEnumConverter<PaxosCryptoNetworks>))]
public enum PaxosCryptoNetworks
{
	/// <summary>Unspecified network.</summary>
	[EnumMember(Value = "UNKNOWN")]
	Unknown,

	/// <summary>Bitcoin.</summary>
	[EnumMember(Value = "BITCOIN")]
	Bitcoin,

	/// <summary>Ethereum.</summary>
	[EnumMember(Value = "ETHEREUM")]
	Ethereum,

	/// <summary>Bitcoin Cash.</summary>
	[EnumMember(Value = "BITCOIN_CASH")]
	BitcoinCash,

	/// <summary>Litecoin.</summary>
	[EnumMember(Value = "LITECOIN")]
	Litecoin,

	/// <summary>Solana.</summary>
	[EnumMember(Value = "SOLANA")]
	Solana,

	/// <summary>Polygon PoS.</summary>
	[EnumMember(Value = "POLYGON_POS")]
	PolygonPos,

	/// <summary>Base.</summary>
	[EnumMember(Value = "BASE")]
	Base,

	/// <summary>Arbitrum One.</summary>
	[EnumMember(Value = "ARBITRUM_ONE")]
	ArbitrumOne,

	/// <summary>Stellar.</summary>
	[EnumMember(Value = "STELLAR")]
	Stellar,

	/// <summary>Ink.</summary>
	[EnumMember(Value = "INK")]
	Ink,

	/// <summary>X Layer.</summary>
	[EnumMember(Value = "XLAYER")]
	XLayer,

	/// <summary>Avalanche.</summary>
	[EnumMember(Value = "AVALANCHE")]
	Avalanche,

	/// <summary>Dogecoin.</summary>
	[EnumMember(Value = "DOGECOIN")]
	Dogecoin,

	/// <summary>Sui.</summary>
	[EnumMember(Value = "SUI")]
	Sui,

	/// <summary>Robinhood Chain.</summary>
	[EnumMember(Value = "ROBINHOOD")]
	Robinhood,

	/// <summary>BNB Smart Chain.</summary>
	[EnumMember(Value = "BNB")]
	Bnb,
}

sealed class PaxosEnumConverter<TEnum> : JsonConverter<TEnum>
	where TEnum : struct, Enum
{
	private static readonly IReadOnlyDictionary<string, TEnum> _fromWire =
		Enum.GetValues<TEnum>().ToDictionary(GetWireValue,
			static value => value, StringComparer.OrdinalIgnoreCase);

	private static string GetWireValue(TEnum value)
	{
		var member = typeof(TEnum).GetMember(value.ToString())[0];
		return member.GetCustomAttribute<EnumMemberAttribute>()?.Value ??
			value.ToString();
	}

	internal static string ToWire(TEnum value) => GetWireValue(value);

	public override TEnum ReadJson(JsonReader reader, Type objectType,
		TEnum existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = hasExistingValue;
		_ = serializer;
		var value = reader.Value?.ToString();
		return !value.IsEmpty() && _fromWire.TryGetValue(value, out var result)
			? result
			: existingValue;
	}

	public override void WriteJson(JsonWriter writer, TEnum value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteValue(GetWireValue(value));
	}
}

static class PaxosEnvironmentExtensions
{
	public static string GetApiEndpoint(this PaxosEnvironments environment)
		=> environment switch
		{
			PaxosEnvironments.Production => "https://api.paxos.com/v2",
			PaxosEnvironments.Sandbox => "https://api.sandbox.paxos.com/v2",
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};

	public static string GetOAuthEndpoint(this PaxosEnvironments environment)
		=> environment switch
		{
			PaxosEnvironments.Production =>
				"https://oauth.paxos.com/oauth2/token",
			PaxosEnvironments.Sandbox =>
				"https://oauth.sandbox.paxos.com/oauth2/token",
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};

	public static string GetSocketEndpoint(this PaxosEnvironments environment)
		=> environment switch
		{
			PaxosEnvironments.Production => "wss://ws.paxos.com",
			PaxosEnvironments.Sandbox => "wss://ws.sandbox.paxos.com",
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};
}
