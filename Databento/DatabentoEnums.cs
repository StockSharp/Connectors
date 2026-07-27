namespace StockSharp.Databento;

/// <summary>Databento input symbology types.</summary>
[DataContract]
[Serializable]
public enum DatabentoSymbologyTypes
{
	/// <summary>Original symbol supplied by the publisher.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RawSymbolKey)]
	RawSymbol,

	/// <summary>Numeric Databento instrument identifier.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.InstrumentIdKey)]
	InstrumentId,

	/// <summary>Databento parent symbol, for example <c>ES.FUT</c>.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ParentKey)]
	Parent,

	/// <summary>Databento continuous-contract symbol.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ContinuousKey)]
	Continuous,
}
