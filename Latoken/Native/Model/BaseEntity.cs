namespace StockSharp.LATOKEN.Native.Model;

abstract class BaseEntity
{
	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }
}