namespace StockSharp.LATOKEN.Native.Model;

class Symbol : BaseEntity
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("priceTick")]
	public double PriceTick { get; set; }

	[JsonProperty("priceDecimals")]
	public int PriceDecimals { get; set; }

	[JsonProperty("quantityTick")]
	public double QuantityTick { get; set; }

	[JsonProperty("quantityDecimals")]
	public int QuantityDecimals { get; set; }

	[JsonProperty("costDisplayDecimals")]
	public int CostDisplayDecimals { get; set; }

	[JsonProperty("created")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Created { get; set; }
}