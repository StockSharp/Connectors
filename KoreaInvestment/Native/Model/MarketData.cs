namespace StockSharp.KoreaInvestment.Native.Model;

sealed class KisDomesticQuoteResponse : KisResponse
{
	[JsonProperty("output")]
	public KisDomesticQuote Output { get; set; }
}

sealed class KisDomesticQuote
{
	[JsonProperty("stck_prpr")]
	public string LastPrice { get; set; }

	[JsonProperty("stck_oprc")]
	public string OpenPrice { get; set; }

	[JsonProperty("stck_hgpr")]
	public string HighPrice { get; set; }

	[JsonProperty("stck_lwpr")]
	public string LowPrice { get; set; }

	[JsonProperty("stck_sdpr")]
	public string PreviousClose { get; set; }

	[JsonProperty("acml_vol")]
	public string Volume { get; set; }

	[JsonProperty("acml_tr_pbmn")]
	public string Turnover { get; set; }

	[JsonProperty("bidp")]
	public string BidPrice { get; set; }

	[JsonProperty("bidp_rsqn")]
	public string BidVolume { get; set; }

	[JsonProperty("askp")]
	public string AskPrice { get; set; }

	[JsonProperty("askp_rsqn")]
	public string AskVolume { get; set; }

	[JsonProperty("hts_kor_isnm")]
	public string Name { get; set; }

	[JsonProperty("rprs_mrkt_kor_name")]
	public string MarketName { get; set; }
}

sealed class KisDomesticCandleResponse : KisResponse
{
	[JsonProperty("output1")]
	public KisDomesticCandleHeader Output1 { get; set; }

	[JsonProperty("output2")]
	public KisDomesticCandle[] Output2 { get; set; }
}

sealed class KisDomesticCandleHeader
{
	[JsonProperty("prdy_vrss")]
	public string PreviousChange { get; set; }
}

sealed class KisDomesticCandle
{
	[JsonProperty("stck_bsop_date")]
	public string Date { get; set; }

	[JsonProperty("stck_cntg_hour")]
	public string Time { get; set; }

	[JsonProperty("stck_oprc")]
	public string Open { get; set; }

	[JsonProperty("stck_hgpr")]
	public string High { get; set; }

	[JsonProperty("stck_lwpr")]
	public string Low { get; set; }

	[JsonProperty("stck_clpr")]
	public string Close { get; set; }

	[JsonProperty("stck_prpr")]
	public string CurrentPrice { get; set; }

	[JsonProperty("cntg_vol")]
	public string MinuteVolume { get; set; }

	[JsonProperty("acml_vol")]
	public string Volume { get; set; }

	[JsonProperty("acml_tr_pbmn")]
	public string Turnover { get; set; }
}

sealed class KisDerivativeQuoteResponse : KisResponse
{
	[JsonProperty("output1")]
	public KisDerivativeQuote Output1 { get; set; }

	[JsonProperty("output2")]
	public KisDerivativeQuoteDetails Output2 { get; set; }

	[JsonProperty("output3")]
	public KisDerivativeQuoteGreeks Output3 { get; set; }
}

sealed class KisDerivativeQuote
{
	[JsonProperty("futs_prpr")]
	public string FuturesPrice { get; set; }

	[JsonProperty("optn_prpr")]
	public string OptionPrice { get; set; }

	[JsonProperty("futs_oprc")]
	public string FuturesOpen { get; set; }

	[JsonProperty("optn_oprc")]
	public string OptionOpen { get; set; }

	[JsonProperty("futs_hgpr")]
	public string FuturesHigh { get; set; }

	[JsonProperty("optn_hgpr")]
	public string OptionHigh { get; set; }

	[JsonProperty("futs_lwpr")]
	public string FuturesLow { get; set; }

	[JsonProperty("optn_lwpr")]
	public string OptionLow { get; set; }

	[JsonProperty("acml_vol")]
	public string Volume { get; set; }

	[JsonProperty("acml_tr_pbmn")]
	public string Turnover { get; set; }

	[JsonProperty("futs_askp1")]
	public string FuturesAsk { get; set; }

	[JsonProperty("optn_askp1")]
	public string OptionAsk { get; set; }

	[JsonProperty("futs_bidp1")]
	public string FuturesBid { get; set; }

	[JsonProperty("optn_bidp1")]
	public string OptionBid { get; set; }

	[JsonProperty("askp_rsqn1")]
	public string AskVolume { get; set; }

	[JsonProperty("bidp_rsqn1")]
	public string BidVolume { get; set; }

	[JsonProperty("hts_otst_stpl_qty")]
	public string OpenInterest { get; set; }
}

sealed class KisDerivativeQuoteDetails
{
	[JsonProperty("futs_sdpr")]
	public string FuturesPreviousClose { get; set; }

	[JsonProperty("optn_sdpr")]
	public string OptionPreviousClose { get; set; }
}

sealed class KisDerivativeQuoteGreeks
{
	[JsonProperty("delta_val")]
	public string Delta { get; set; }
}

sealed class KisDerivativeCandleResponse : KisResponse
{
	[JsonProperty("output1")]
	public KisDerivativeCandleHeader Output1 { get; set; }

	[JsonProperty("output2")]
	public KisDerivativeCandle[] Output2 { get; set; }
}

sealed class KisDerivativeCandleHeader
{
	[JsonProperty("futs_prpr")]
	public string CurrentPrice { get; set; }
}

sealed class KisDerivativeCandle
{
	[JsonProperty("stck_bsop_date")]
	public string StockDate { get; set; }

	[JsonProperty("bsop_date")]
	public string Date { get; set; }

	[JsonProperty("stck_cntg_hour")]
	public string StockTime { get; set; }

	[JsonProperty("bsop_hour")]
	public string Time { get; set; }

	[JsonProperty("futs_oprc")]
	public string Open { get; set; }

	[JsonProperty("futs_hgpr")]
	public string High { get; set; }

	[JsonProperty("futs_lwpr")]
	public string Low { get; set; }

	[JsonProperty("futs_clpr")]
	public string Close { get; set; }

	[JsonProperty("futs_prpr")]
	public string CurrentPrice { get; set; }

	[JsonProperty("acml_vol")]
	public string Volume { get; set; }

	[JsonProperty("cntg_vol")]
	public string MinuteVolume { get; set; }

	[JsonProperty("acml_tr_pbmn")]
	public string Turnover { get; set; }
}

sealed class KisOverseasQuoteResponse : KisResponse
{
	[JsonProperty("output")]
	public KisOverseasQuote Output { get; set; }
}

sealed class KisOverseasQuote
{
	[JsonProperty("last")]
	public string LastPrice { get; set; }

	[JsonProperty("open")]
	public string OpenPrice { get; set; }

	[JsonProperty("high")]
	public string HighPrice { get; set; }

	[JsonProperty("low")]
	public string LowPrice { get; set; }

	[JsonProperty("base")]
	public string PreviousClose { get; set; }

	[JsonProperty("tvol")]
	public string Volume { get; set; }

	[JsonProperty("tamt")]
	public string Turnover { get; set; }

	[JsonProperty("pbid")]
	public string BidPrice { get; set; }

	[JsonProperty("vbid")]
	public string BidVolume { get; set; }

	[JsonProperty("pask")]
	public string AskPrice { get; set; }

	[JsonProperty("vask")]
	public string AskVolume { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("xymd")]
	public string Date { get; set; }

	[JsonProperty("xhms")]
	public string Time { get; set; }
}

sealed class KisOverseasCandleResponse : KisResponse
{
	[JsonProperty("output1")]
	public KisOverseasCandleHeader Output1 { get; set; }

	[JsonProperty("output2")]
	public KisOverseasCandle[] Output2 { get; set; }
}

sealed class KisOverseasCandleHeader
{
	[JsonProperty("last")]
	public string LastPrice { get; set; }
}

sealed class KisOverseasCandle
{
	[JsonProperty("xymd")]
	public string Date { get; set; }

	[JsonProperty("xdtm")]
	public string DateTime { get; set; }

	[JsonProperty("xhms")]
	public string Time { get; set; }

	[JsonProperty("open")]
	public string Open { get; set; }

	[JsonProperty("high")]
	public string High { get; set; }

	[JsonProperty("low")]
	public string Low { get; set; }

	[JsonProperty("clos")]
	public string Close { get; set; }

	[JsonProperty("last")]
	public string Last { get; set; }

	[JsonProperty("evol")]
	public string MinuteVolume { get; set; }

	[JsonProperty("tvol")]
	public string Volume { get; set; }

	[JsonProperty("tamt")]
	public string Turnover { get; set; }
}
