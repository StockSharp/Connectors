namespace StockSharp.AlorHistory.Native.Model;

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

	[JsonProperty("priceStep")]
	public double? PriceStep { get; set; }

	[JsonProperty("priceMultiplier")]
	public double? PriceMultiplier { get; set; }

	[JsonProperty("priceShownUnits")]
	public double? PriceShownUnits { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("ISIN")]
	public string ISIN { get; set; }

	[JsonProperty("board")]
	public string Board { get; set; }

	[JsonProperty("primaryBoard")]
	public string PrimaryBoard { get; set; }

	[JsonProperty("strikePrice")]
	public double? StrikePrice { get; set; }

	[JsonProperty("endExpiration")]
	public DateTime? EndExpiration { get; set; }

	[JsonProperty("underlyingSymbol")]
	public string UnderlyingSymbol { get; set; }

	[JsonProperty("optionSide")]
	public string OptionSide { get; set; }
}
