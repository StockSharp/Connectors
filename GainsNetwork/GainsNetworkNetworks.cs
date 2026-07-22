namespace StockSharp.GainsNetwork;

/// <summary>Supported Gains Network deployments.</summary>
[DataContract]
public enum GainsNetworkEnvironments
{
	/// <summary>Arbitrum One.</summary>
	[EnumMember]
	[Display(
		Name = "Arbitrum One")]
	Arbitrum,

	/// <summary>Base mainnet.</summary>
	[EnumMember]
	[Display(
		Name = "Base")]
	Base,

	/// <summary>Polygon PoS.</summary>
	[EnumMember]
	[Display(
		Name = "Polygon")]
	Polygon,
}

sealed class GainsNetworkDeployment
{
	public GainsNetworkEnvironments Environment { get; init; }
	public string Name { get; init; }
	public long ChainId { get; init; }
	public string RpcEndpoint { get; init; }
	public string BackendEndpoint { get; init; }
	public string DiamondAddress { get; init; }
	public string NativeSymbol { get; init; }
}
