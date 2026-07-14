namespace StockSharp.Mexc.Native.Spot.Model;

class UserTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("orderListId")]
	public long OrderListId { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("qty")]
	public double? Qty { get; set; }

	[JsonProperty("quoteQty")]
	public double? QuoteQty { get; set; }

	[JsonProperty("commission")]
	public double? Commission { get; set; }

	[JsonProperty("commissionAsset")]
	public string CommissionAsset { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("isBuyer")]
	public bool IsBuyer { get; set; }

	[JsonProperty("isMaker")]
	public bool IsMaker { get; set; }

	[JsonProperty("isBestMatch")]
	public bool IsBestMatch { get; set; }
}