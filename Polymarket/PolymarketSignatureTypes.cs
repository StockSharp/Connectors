namespace StockSharp.Polymarket;

/// <summary>Polymarket order signature types.</summary>
[DataContract]
public enum PolymarketSignatureTypes
{
	/// <summary>Externally owned account.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.EoaKey)]
	Eoa = 0,

	/// <summary>Polymarket proxy wallet controlled by an EOA.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PolymarketProxyKey)]
	PolyProxy = 1,

	/// <summary>Polymarket Gnosis Safe controlled by an EOA.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PolymarketGnosisSafeKey)]
	PolyGnosisSafe = 2,

	/// <summary>Polymarket deposit wallet using ERC-1271.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PolymarketErc1271Key)]
	Poly1271 = 3,
}
