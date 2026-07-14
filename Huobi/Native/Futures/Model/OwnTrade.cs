namespace StockSharp.Huobi.Native.Futures.Model;

class OwnTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("trade_id")]
	public long TradeId { get; set; }

	[JsonProperty("trade_volume")]
	public double Volume { get; set; }

	[JsonProperty("trade_price")]
	public double Price { get; set; }

	[JsonProperty("trade_fee")]
	public double? Fee { get; set; }

	[JsonProperty("trade_turnover")]
	public double? Turnover { get; set; }

	[JsonProperty("created_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("role")]
	public string Role { get; set; }

	[JsonProperty("profit")]
	public double? Profit { get; set; }

	[JsonProperty("real_profit")]
	public double? RealProfit { get; set; }

	[JsonProperty("fee_asset")]
	public string FeeAsset { get; set; }
}