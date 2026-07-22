namespace StockSharp.Polymarket;

/// <summary>Polymarket order signature types.</summary>
[DataContract]
public enum PolymarketSignatureTypes
{
	/// <summary>Externally owned account.</summary>
	[EnumMember]
	[Display(
		Name = "EOA")]
	Eoa = 0,

	/// <summary>Polymarket proxy wallet controlled by an EOA.</summary>
	[EnumMember]
	[Display(
		Name = "Polymarket proxy")]
	PolyProxy = 1,

	/// <summary>Polymarket Gnosis Safe controlled by an EOA.</summary>
	[EnumMember]
	[Display(
		Name = "Polymarket Gnosis Safe")]
	PolyGnosisSafe = 2,

	/// <summary>Polymarket deposit wallet using ERC-1271.</summary>
	[EnumMember]
	[Display(
		Name = "Polymarket ERC-1271")]
	Poly1271 = 3,
}
