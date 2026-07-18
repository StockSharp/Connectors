namespace StockSharp.EodHistoricalData.Native.Model;

sealed class EodhdOptionQuery
{
	public string Contract { get; set; }
	public string UnderlyingSymbol { get; set; }
	public DateTime? Expiry { get; set; }
	public DateTime? ExpiryFrom { get; set; }
	public DateTime? ExpiryTo { get; set; }
	public DateTime? TradeTime { get; set; }
	public DateTime? TradeTimeFrom { get; set; }
	public DateTime? TradeTimeTo { get; set; }
	public OptionTypes? OptionType { get; set; }
	public decimal? Strike { get; set; }
	public decimal? StrikeFrom { get; set; }
	public decimal? StrikeTo { get; set; }
	public string Sort { get; set; }
	public int Offset { get; set; }
	public int Limit { get; set; } = 1000;
}

sealed class EodhdOptionPage
{
	[JsonProperty("meta")]
	public EodhdOptionMeta Meta { get; set; }

	[JsonProperty("data")]
	public EodhdOptionResource[] Data { get; set; }

	[JsonProperty("links")]
	public EodhdOptionLinks Links { get; set; }
}

sealed class EodhdOptionMeta
{
	[JsonProperty("offset")]
	public int? Offset { get; set; }

	[JsonProperty("limit")]
	public int? Limit { get; set; }

	[JsonProperty("total")]
	public long? Total { get; set; }

	[JsonProperty("fields")]
	public string[] Fields { get; set; }

	[JsonProperty("compact")]
	public bool? IsCompact { get; set; }
}

sealed class EodhdOptionLinks
{
	[JsonProperty("next")]
	public string Next { get; set; }
}

sealed class EodhdOptionResource
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("attributes")]
	public EodhdOptionAttributes Attributes { get; set; }
}

sealed class EodhdOptionAttributes
{
	[JsonProperty("contract")]
	public string Contract { get; set; }

	[JsonProperty("underlying_symbol")]
	public string UnderlyingSymbol { get; set; }

	[JsonProperty("exp_date")]
	public string ExpirationDate { get; set; }

	[JsonProperty("expiration_type")]
	public string ExpirationType { get; set; }

	[JsonProperty("type")]
	public string OptionType { get; set; }

	[JsonProperty("strike")]
	public decimal? Strike { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("last")]
	public decimal? Last { get; set; }

	[JsonProperty("last_size")]
	public decimal? LastSize { get; set; }

	[JsonProperty("change")]
	public decimal? Change { get; set; }

	[JsonProperty("pctchange")]
	public decimal? ChangePercent { get; set; }

	[JsonProperty("previous")]
	public decimal? Previous { get; set; }

	[JsonProperty("previous_date")]
	public string PreviousDate { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("bid_date")]
	public string BidDate { get; set; }

	[JsonProperty("bid_size")]
	public decimal? BidSize { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("ask_date")]
	public string AskDate { get; set; }

	[JsonProperty("ask_size")]
	public decimal? AskSize { get; set; }

	[JsonProperty("moneyness")]
	public decimal? Moneyness { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("volume_change")]
	public decimal? VolumeChange { get; set; }

	[JsonProperty("volume_pctchange")]
	public decimal? VolumeChangePercent { get; set; }

	[JsonProperty("open_interest")]
	public decimal? OpenInterest { get; set; }

	[JsonProperty("open_interest_change")]
	public decimal? OpenInterestChange { get; set; }

	[JsonProperty("open_interest_pctchange")]
	public decimal? OpenInterestChangePercent { get; set; }

	[JsonProperty("volatility")]
	public decimal? Volatility { get; set; }

	[JsonProperty("volatility_change")]
	public decimal? VolatilityChange { get; set; }

	[JsonProperty("volatility_pctchange")]
	public decimal? VolatilityChangePercent { get; set; }

	[JsonProperty("theoretical")]
	public decimal? Theoretical { get; set; }

	[JsonProperty("delta")]
	public decimal? Delta { get; set; }

	[JsonProperty("gamma")]
	public decimal? Gamma { get; set; }

	[JsonProperty("theta")]
	public decimal? Theta { get; set; }

	[JsonProperty("vega")]
	public decimal? Vega { get; set; }

	[JsonProperty("rho")]
	public decimal? Rho { get; set; }

	[JsonProperty("tradetime")]
	public string TradeTime { get; set; }

	[JsonProperty("vol_oi_ratio")]
	public decimal? VolumeOpenInterestRatio { get; set; }

	[JsonProperty("dte")]
	public int? DaysToExpiration { get; set; }

	[JsonProperty("midpoint")]
	public decimal? Midpoint { get; set; }
}
