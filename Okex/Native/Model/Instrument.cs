namespace StockSharp.Okex.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Instrument
{
	[JsonProperty("instType")]
	public string InstType { get; set; }

	[JsonProperty("instId")]
	public string Id { get; set; }

	[JsonProperty("uly")]
	public string Underlying { get; set; } // only for futures/swap/option

	[JsonProperty("category")]
	public string Category { get; set; } // fee schedule

	[JsonProperty("baseCcy")]
	public string BaseCurrency { get; set; } // base currency -- only for spot

	[JsonProperty("quoteCcy")]
	public string QuoteCurrency { get; set; } // quote currency -- only for spot

	[JsonProperty("settleCcy")]
	public string SettleCurrency { get; set; } // settle currency -- only for futures/swap/option

	[JsonProperty("ctVal")]
	public string ContractValue { get; set; } // only for futures/swap/option

	[JsonProperty("ctMult")]
	public string ContractMultiplier { get; set; } // only for futures/swap/option

	[JsonProperty("ctValCcy")]
	public string ContractValueCurrency { get; set; } // only for futures/swap/option

	[JsonProperty("optType")]
	public string OptionType { get; set; }

	[JsonProperty("stk")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("listTime")]
	public long? Listing { get; set; }

	[JsonProperty("expTime")]
	public long? Delivery { get; set; }

	[JsonProperty("lever")]
	public decimal? Leverage { get; set; } // not applicable to spot. used to distinguish between margin and spot

	[JsonProperty("tickSz")]
	public decimal? TickSize { get; set; }

	[JsonProperty("lotSz")]
	public decimal? LotSize { get; set; }

	[JsonProperty("minSz")]
	public decimal? MinSize { get; set; }

	[JsonProperty("ctType")]
	public string ContractType { get; set; } // only for futures/swap

	[JsonProperty("alias")]
	public string Alias { get; set; } // only for futures

	[JsonProperty("state")]
	public string State { get; set; }
}