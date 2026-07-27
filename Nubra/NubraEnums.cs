namespace StockSharp.Nubra;

/// <summary>Nubra order delivery products.</summary>
[DataContract]
[Serializable]
public enum NubraProducts
{
	/// <summary>Cash-and-carry delivery.</summary>
	[EnumMember]
	Cnc,

	/// <summary>Intraday product.</summary>
	[EnumMember]
	Iday,
}
