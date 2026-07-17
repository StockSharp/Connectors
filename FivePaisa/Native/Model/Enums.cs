namespace StockSharp.FivePaisa;

/// <summary>5paisa order products.</summary>
[DataContract]
[Serializable]
public enum FivePaisaProducts
{
	/// <summary>Delivery.</summary>
	[EnumMember]
	Delivery,

	/// <summary>Intraday.</summary>
	[EnumMember]
	Intraday,
}
