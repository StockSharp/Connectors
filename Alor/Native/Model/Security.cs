namespace StockSharp.Alor.Native.Model;

class Security
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("shortName")]
	public string ShortName { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("lotSize")]
	public double? LotSize { get; set; }

	[JsonProperty("faceValue")]
	public double? FaceValue { get; set; }

	[JsonProperty("cfiCode")]
	public string CfiCode { get; set; }

	[JsonProperty("cancellation")]
	public DateTime Cancellation { get; set; }

	[JsonProperty("minStep")]
	public double? MinStep { get; set; }

	[JsonProperty("roundTo")]
	public int? RoundTo { get; set; }

	[JsonProperty("rating")]
	public double? Rating { get; set; }

	[JsonProperty("marginBuy")]
	public double? MarginBuy { get; set; }

	[JsonProperty("marginSell")]
	public double? MarginSell { get; set; }

	[JsonProperty("marginRate")]
	public double? MarginRate { get; set; }

	[JsonProperty("priceStep")]
	public double? PriceStep { get; set; }

	[JsonProperty("priceMax")]
	public double? PriceMax { get; set; }

	[JsonProperty("priceMin")]
	public double? PriceMin { get; set; }

	[JsonProperty("theorPrice")]
	public double? TheorPrice { get; set; }

	[JsonProperty("theorPriceLimit")]
	public double? TheorPriceLimit { get; set; }

	[JsonProperty("volatility")]
	public double? Volatility { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("ISIN")]
	public string ISIN { get; set; }

	[JsonProperty("yield")]
	public double? Yield { get; set; }

	[JsonProperty("board")]
	public string Board { get; set; }

	[JsonProperty("primaryBoard")]
	public string PrimaryBoard { get; set; }

	[JsonProperty("tradingStatus")]
	public int TradingStatus { get; set; }

	[JsonProperty("tradingStatusInfo")]
	public string TradingStatusInfo { get; set; }

	[JsonProperty("complexProductCategory")]
	public string ComplexProductCategory { get; set; }

	[JsonProperty("priceMultiplier")]
	public double? PriceMultiplier { get; set; }

	[JsonProperty("priceShownUnits")]
	public double? PriceShownUnits { get; set; }

	[JsonProperty("strikePrice")]
	public double? StrikePrice { get; set; }

	[JsonProperty("endExpiration")]
	public DateTime? EndExpiration { get; set; }

	[JsonProperty("underlyingSymbol")]
	public string UnderlyingSymbol { get; set; }

	[JsonProperty("optionSide")]
	public string OptionSide { get; set; }
}