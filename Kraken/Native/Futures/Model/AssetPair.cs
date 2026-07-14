namespace StockSharp.Kraken.Native.Futures.Model;

class AssetPair
{
	[JsonProperty("altname")]
	public string AlternateName { get; set; }

	[JsonProperty("aclass_base")]
	public string AssetClassBase { get; set; }

	[JsonProperty("base")]
	public string Base { get; set; }

	[JsonProperty("aclass_quote")]
	public string AssetClassQuote { get; set; }

	[JsonProperty("quote")]
	public string Quote { get; set; }

	[JsonProperty("lot")]
	public string Lot { get; set; }

	[JsonProperty("pair_decimals")]
	public int PairDecimals { get; set; }

	[JsonProperty("lot_decimals")]
	public int LotDecimals { get; set; }

	[JsonProperty("lot_multiplier")]
	public int LotMultiplier { get; set; }

	[JsonProperty("leverage_buy")]
	public decimal[] LeverageBuy { get; set; }

	[JsonProperty("leverage_sell")]
	public decimal[] LeverageSell { get; set; }

	[JsonProperty("fees")]
	public Fee[] Fees { get; set; }

	[JsonProperty("fees_maker")]
	public Fee[] FeesMaker { get; set; }

	[JsonProperty("fee_volume_currency")]
	public string FeeVolumeCurrency { get; set; }

	[JsonProperty("margin_call")]
	public decimal MarginCall { get; set; }

	[JsonProperty("margin_stop")]
	public decimal MarginStop { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
struct Fee
{
	public decimal Volume { get; set; }
	public decimal PercentFee { get; set; }
}