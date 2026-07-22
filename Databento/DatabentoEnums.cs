namespace StockSharp.Databento;

/// <summary>Databento input symbology types.</summary>
[DataContract]
[Serializable]
public enum DatabentoSymbologyTypes
{
	/// <summary>Original symbol supplied by the publisher.</summary>
	[EnumMember]
	[Display(
		Name = "Raw symbol")]
	RawSymbol,

	/// <summary>Numeric Databento instrument identifier.</summary>
	[EnumMember]
	[Display(
		Name = "Instrument ID")]
	InstrumentId,

	/// <summary>Databento parent symbol, for example <c>ES.FUT</c>.</summary>
	[EnumMember]
	[Display(
		Name = "Parent")]
	Parent,

	/// <summary>Databento continuous-contract symbol.</summary>
	[EnumMember]
	[Display(
		Name = "Continuous")]
	Continuous,
}
