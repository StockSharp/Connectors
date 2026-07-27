namespace StockSharp.RavenPack;

/// <summary>RavenPack data products supported by the API.</summary>
[DataContract]
public enum RavenPackProducts
{
	/// <summary>Classic RavenPack Analytics product.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RavenPackAnalyticsKey)]
	Analytics,

	/// <summary>RavenPack Edge product.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RavenPackEdgeKey)]
	Edge,
}
