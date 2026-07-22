namespace StockSharp.RavenPack;

/// <summary>RavenPack data products supported by the API.</summary>
[DataContract]
public enum RavenPackProducts
{
	/// <summary>Classic RavenPack Analytics product.</summary>
	[EnumMember]
	[Display(
		Name = "RavenPack Analytics")]
	Analytics,

	/// <summary>RavenPack Edge product.</summary>
	[EnumMember]
	[Display(
		Name = "RavenPack Edge")]
	Edge,
}
