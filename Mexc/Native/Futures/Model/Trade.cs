namespace StockSharp.Mexc.Native.Futures.Model;

class Trade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("qty")]
	public double? Qty { get; set; }

	[JsonProperty("quoteQty")]
	public double? QuoteQty { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("isBuyerMaker")]
	public bool IsBuyerMaker { get; set; }
}

class UserTrade
{
	[JsonProperty("buyer")]
	public bool Buyer { get; set; }

	[JsonProperty("commission")]
	public double? Commission { get; set; }

	[JsonProperty("commissionAsset")]
	public string CommissionAsset { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("maker")]
	public bool Maker { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("qty")]
	public double? Qty { get; set; }

	[JsonProperty("quoteQty")]
	public double? QuoteQty { get; set; }

	[JsonProperty("realizedPnl")]
	public double? RealizedPnl { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("positionSide")]
	public string PositionSide { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }
}