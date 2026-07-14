namespace StockSharp.Alor.Native.Model;

class InstrumentStatus
{
	[JsonProperty("priceMax")]
	public double? PriceMax { get; set; }

	[JsonProperty("priceMin")]
	public double? PriceMin { get; set; }

	[JsonProperty("marginbuy")]
	public double? MarginBuy { get; set; }

	[JsonProperty("marginsell")]
	public double? MarginSell { get; set; }

	[JsonProperty("tradingStatus")]
	public int TradingStatus { get; set; }

	[JsonProperty("tradingStatusInfo")]
	public string TradingStatusInfo { get; set; }
}