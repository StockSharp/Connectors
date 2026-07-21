namespace StockSharp.Balancer;

/// <summary>Balancer deployments supported by the connector.</summary>
[DataContract]
public enum BalancerNetworks
{
	/// <summary>Ethereum mainnet.</summary>
	[EnumMember]
	[Display(Name = "Ethereum")]
	Ethereum = 1,

	/// <summary>Arbitrum One.</summary>
	[EnumMember]
	Arbitrum = 42161,

	/// <summary>Base mainnet.</summary>
	[EnumMember]
	Base = 8453,

	/// <summary>Optimism mainnet.</summary>
	[EnumMember]
	Optimism = 10,

	/// <summary>Polygon PoS.</summary>
	[EnumMember]
	Polygon = 137,

	/// <summary>Gnosis Chain.</summary>
	[EnumMember]
	Gnosis = 100,

	/// <summary>Avalanche C-Chain.</summary>
	[EnumMember]
	Avalanche = 43114,
}

sealed class BalancerDeployment
{
	public BalancerNetworks Network { get; init; }
	public string Name { get; init; }
	public long ChainId { get; init; }
	public BalancerGraphChains GraphChain { get; init; }
	public string RpcEndpoint { get; init; }
	public string WebSocketEndpoint { get; init; }
	public string NativeSymbol { get; init; }
	public string V2Vault { get; init; }
	public string V3Vault { get; init; }
	public string V3Router { get; init; }
}
