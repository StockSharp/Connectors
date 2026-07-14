namespace StockSharp.BingX.Native.Futures.Model;

class UserTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("qty")]
	public double? Quantity { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("quoteQty")]
	public double? QuoteQuantity { get; set; }

	[JsonProperty("realizedPnl")]
	public double? RealizedPnl { get; set; }

	[JsonProperty("marginAsset")]
	public string MarginAsset { get; set; }

	[JsonProperty("commission")]
	public double? Commission { get; set; }

	[JsonProperty("commissionAsset")]
	public string CommissionAsset { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("positionSide")]
	public string PositionSide { get; set; }

	[JsonProperty("buyer")]
	public bool IsBuyer { get; set; }

	[JsonProperty("maker")]
	public bool IsMaker { get; set; }
}