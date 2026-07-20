namespace StockSharp.Gmx;

/// <summary>GMX production networks.</summary>
[DataContract]
public enum GmxNetworks
{
	/// <summary>Arbitrum One.</summary>
	[EnumMember]
	Arbitrum,

	/// <summary>Avalanche C-Chain.</summary>
	[EnumMember]
	Avalanche,

	/// <summary>MegaETH Mainnet.</summary>
	[EnumMember]
	[Display(Name = "MegaETH")]
	MegaEth,
}
