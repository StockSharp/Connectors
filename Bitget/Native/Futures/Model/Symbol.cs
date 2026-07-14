namespace StockSharp.Bitget.Native.Futures.Model;

class Symbol
{
	[JsonProperty("symbol")]
	public string Id { get; set; }

	[JsonProperty("baseCoin")]
	public string BaseCoin { get; set; }

	[JsonProperty("quoteCoin")]
	public string QuoteCoin { get; set; }

	[JsonProperty("buyLimitPriceRatio")]
	public double? BuyLimitPriceRatio { get; set; }

	[JsonProperty("sellLimitPriceRatio")]
	public double? SellLimitPriceRatio { get; set; }

	[JsonProperty("feeRateUpRatio")]
	public double? FeeRateUpRatio { get; set; }

	[JsonProperty("makerFeeRate")]
	public double? MakerFeeRate { get; set; }

	[JsonProperty("takerFeeRate")]
	public double? TakerFeeRate { get; set; }

	[JsonProperty("openCostUpRatio")]
	public double? OpenCostUpRatio { get; set; }

	[JsonProperty("supportMarginCoins")]
	public string[] SupportMarginCoins { get; set; }

	[JsonProperty("minTradeNum")]
	public double? MinTradeNum { get; set; }

	[JsonProperty("priceEndStep")]
	public string PriceEndStep { get; set; }

	[JsonProperty("volumePlace")]
	public string VolumePlace { get; set; }

	[JsonProperty("pricePlace")]
	public int? PricePlace { get; set; }

	[JsonProperty("sizeMultiplier")]
	public double? SizeMultiplier { get; set; }

	[JsonProperty("symbolType")]
	public string SymbolType { get; set; }

	[JsonProperty("symbolStatus")]
	public string SymbolStatus { get; set; }

	[JsonProperty("offTime")]
	public long? OffTime { get; set; }

	[JsonProperty("limitOpenTime")]
	public long? LimitOpenTime { get; set; }

	[JsonProperty("deliveryTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? DeliveryTime { get; set; }

	[JsonProperty("maxTradeAmount")]
	public double? MaxTradeAmount { get; set; }
}
