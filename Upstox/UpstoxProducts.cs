namespace StockSharp.Upstox;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Upstox order product.
/// </summary>
[DataContract]
[Serializable]
public enum UpstoxProducts
{
	/// <summary>Delivery.</summary>
	[EnumMember(Value = "D")]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DeliveryKey)]
	Delivery,

	/// <summary>Intraday.</summary>
	[EnumMember(Value = "I")]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.IntradayKey)]
	Intraday,

	/// <summary>Margin trading facility.</summary>
	[EnumMember(Value = "MTF")]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MarginKey)]
	Margin,
}
