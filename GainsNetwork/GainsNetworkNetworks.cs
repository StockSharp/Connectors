namespace StockSharp.GainsNetwork;

/// <summary>Supported Gains Network deployments.</summary>
[DataContract]
public enum GainsNetworkEnvironments
{
	/// <summary>Arbitrum One.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ArbitrumOneKey)]
	Arbitrum,

	/// <summary>Base mainnet.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BaseKey)]
	Base,

	/// <summary>Polygon PoS.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PolygonKey)]
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
