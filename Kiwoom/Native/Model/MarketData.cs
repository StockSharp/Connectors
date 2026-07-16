namespace StockSharp.Kiwoom.Native.Model;

sealed class KiwoomDomesticSecurityRequest
{
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }
}

sealed class KiwoomDomesticQuoteResponse : KiwoomResponse
{
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }

	[JsonProperty("stk_nm")]
	public string Name { get; set; }

	[JsonProperty("cur_prc")]
	public string LastPrice { get; set; }

	[JsonProperty("open_pric")]
	public string OpenPrice { get; set; }

	[JsonProperty("high_pric")]
	public string HighPrice { get; set; }

	[JsonProperty("low_pric")]
	public string LowPrice { get; set; }

	[JsonProperty("base_pric")]
	public string PreviousClose { get; set; }

	[JsonProperty("trde_qty")]
	public string Volume { get; set; }

	[JsonProperty("sel_bid")]
	public string AskPrice { get; set; }

	[JsonProperty("buy_bid")]
	public string BidPrice { get; set; }
}

sealed class KiwoomDomesticDepthResponse : KiwoomResponse
{
	[JsonProperty("bid_req_base_tm")]
	public string Time { get; set; }

	[JsonProperty("sel_fpr_bid")]
	public string AskPrice1 { get; set; }
	[JsonProperty("sel_2th_pre_bid")]
	public string AskPrice2 { get; set; }
	[JsonProperty("sel_3th_pre_bid")]
	public string AskPrice3 { get; set; }
	[JsonProperty("sel_4th_pre_bid")]
	public string AskPrice4 { get; set; }
	[JsonProperty("sel_5th_pre_bid")]
	public string AskPrice5 { get; set; }
	[JsonProperty("sel_6th_pre_bid")]
	public string AskPrice6 { get; set; }
	[JsonProperty("sel_7th_pre_bid")]
	public string AskPrice7 { get; set; }
	[JsonProperty("sel_8th_pre_bid")]
	public string AskPrice8 { get; set; }
	[JsonProperty("sel_9th_pre_bid")]
	public string AskPrice9 { get; set; }
	[JsonProperty("sel_10th_pre_bid")]
	public string AskPrice10 { get; set; }

	[JsonProperty("sel_fpr_req")]
	public string AskVolume1 { get; set; }
	[JsonProperty("sel_2th_pre_req")]
	public string AskVolume2 { get; set; }
	[JsonProperty("sel_3th_pre_req")]
	public string AskVolume3 { get; set; }
	[JsonProperty("sel_4th_pre_req")]
	public string AskVolume4 { get; set; }
	[JsonProperty("sel_5th_pre_req")]
	public string AskVolume5 { get; set; }
	[JsonProperty("sel_6th_pre_req")]
	public string AskVolume6 { get; set; }
	[JsonProperty("sel_7th_pre_req")]
	public string AskVolume7 { get; set; }
	[JsonProperty("sel_8th_pre_req")]
	public string AskVolume8 { get; set; }
	[JsonProperty("sel_9th_pre_req")]
	public string AskVolume9 { get; set; }
	[JsonProperty("sel_10th_pre_req")]
	public string AskVolume10 { get; set; }

	[JsonProperty("buy_fpr_bid")]
	public string BidPrice1 { get; set; }
	[JsonProperty("buy_2th_pre_bid")]
	public string BidPrice2 { get; set; }
	[JsonProperty("buy_3th_pre_bid")]
	public string BidPrice3 { get; set; }
	[JsonProperty("buy_4th_pre_bid")]
	public string BidPrice4 { get; set; }
	[JsonProperty("buy_5th_pre_bid")]
	public string BidPrice5 { get; set; }
	[JsonProperty("buy_6th_pre_bid")]
	public string BidPrice6 { get; set; }
	[JsonProperty("buy_7th_pre_bid")]
	public string BidPrice7 { get; set; }
	[JsonProperty("buy_8th_pre_bid")]
	public string BidPrice8 { get; set; }
	[JsonProperty("buy_9th_pre_bid")]
	public string BidPrice9 { get; set; }
	[JsonProperty("buy_10th_pre_bid")]
	public string BidPrice10 { get; set; }

	[JsonProperty("buy_fpr_req")]
	public string BidVolume1 { get; set; }
	[JsonProperty("buy_2th_pre_req")]
	public string BidVolume2 { get; set; }
	[JsonProperty("buy_3th_pre_req")]
	public string BidVolume3 { get; set; }
	[JsonProperty("buy_4th_pre_req")]
	public string BidVolume4 { get; set; }
	[JsonProperty("buy_5th_pre_req")]
	public string BidVolume5 { get; set; }
	[JsonProperty("buy_6th_pre_req")]
	public string BidVolume6 { get; set; }
	[JsonProperty("buy_7th_pre_req")]
	public string BidVolume7 { get; set; }
	[JsonProperty("buy_8th_pre_req")]
	public string BidVolume8 { get; set; }
	[JsonProperty("buy_9th_pre_req")]
	public string BidVolume9 { get; set; }
	[JsonProperty("buy_10th_pre_req")]
	public string BidVolume10 { get; set; }

	public string[] AskPrices => [AskPrice1, AskPrice2, AskPrice3, AskPrice4, AskPrice5, AskPrice6, AskPrice7, AskPrice8, AskPrice9, AskPrice10];
	public string[] AskVolumes => [AskVolume1, AskVolume2, AskVolume3, AskVolume4, AskVolume5, AskVolume6, AskVolume7, AskVolume8, AskVolume9, AskVolume10];
	public string[] BidPrices => [BidPrice1, BidPrice2, BidPrice3, BidPrice4, BidPrice5, BidPrice6, BidPrice7, BidPrice8, BidPrice9, BidPrice10];
	public string[] BidVolumes => [BidVolume1, BidVolume2, BidVolume3, BidVolume4, BidVolume5, BidVolume6, BidVolume7, BidVolume8, BidVolume9, BidVolume10];
}

sealed class KiwoomUsSecurityRequest
{
	[JsonProperty("stex_tp")]
	public string ExchangeType { get; set; }

	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }
}

sealed class KiwoomUsQuoteResponse : KiwoomResponse
{
	[JsonProperty("cur_prc")]
	public string LastPrice { get; set; }
	[JsonProperty("base_close_pric")]
	public string PreviousClose { get; set; }
	[JsonProperty("open_pric")]
	public string OpenPrice { get; set; }
	[JsonProperty("high_pric")]
	public string HighPrice { get; set; }
	[JsonProperty("low_pric")]
	public string LowPrice { get; set; }
	[JsonProperty("acc_trde_qty")]
	public string Volume { get; set; }
	[JsonProperty("curr_unit")]
	public string Currency { get; set; }
}

sealed class KiwoomUsDepthResponse : KiwoomResponse
{
	[JsonProperty("bid_tm")]
	public string Time { get; set; }
	[JsonProperty("dt")]
	public string Date { get; set; }
	[JsonProperty("cur_prc")]
	public string LastPrice { get; set; }
	[JsonProperty("trde_qty")]
	public string Volume { get; set; }
	[JsonProperty("trde_prica")]
	public string Turnover { get; set; }
	[JsonProperty("open_pric")]
	public string OpenPrice { get; set; }
	[JsonProperty("high_pric")]
	public string HighPrice { get; set; }
	[JsonProperty("low_pric")]
	public string LowPrice { get; set; }

	[JsonProperty("sel_1bid")]
	public string AskPrice1 { get; set; }
	[JsonProperty("sel_2bid")]
	public string AskPrice2 { get; set; }
	[JsonProperty("sel_3bid")]
	public string AskPrice3 { get; set; }
	[JsonProperty("sel_4bid")]
	public string AskPrice4 { get; set; }
	[JsonProperty("sel_5bid")]
	public string AskPrice5 { get; set; }
	[JsonProperty("sel_6bid")]
	public string AskPrice6 { get; set; }
	[JsonProperty("sel_7bid")]
	public string AskPrice7 { get; set; }
	[JsonProperty("sel_8bid")]
	public string AskPrice8 { get; set; }
	[JsonProperty("sel_9bid")]
	public string AskPrice9 { get; set; }
	[JsonProperty("sel_10bid")]
	public string AskPrice10 { get; set; }
	[JsonProperty("sel_1bid_req")]
	public string AskVolume1 { get; set; }
	[JsonProperty("sel_2bid_req")]
	public string AskVolume2 { get; set; }
	[JsonProperty("sel_3bid_req")]
	public string AskVolume3 { get; set; }
	[JsonProperty("sel_4bid_req")]
	public string AskVolume4 { get; set; }
	[JsonProperty("sel_5bid_req")]
	public string AskVolume5 { get; set; }
	[JsonProperty("sel_6bid_req")]
	public string AskVolume6 { get; set; }
	[JsonProperty("sel_7bid_req")]
	public string AskVolume7 { get; set; }
	[JsonProperty("sel_8bid_req")]
	public string AskVolume8 { get; set; }
	[JsonProperty("sel_9bid_req")]
	public string AskVolume9 { get; set; }
	[JsonProperty("sel_10bid_req")]
	public string AskVolume10 { get; set; }

	[JsonProperty("buy_1bid")]
	public string BidPrice1 { get; set; }
	[JsonProperty("buy_2bid")]
	public string BidPrice2 { get; set; }
	[JsonProperty("buy_3bid")]
	public string BidPrice3 { get; set; }
	[JsonProperty("buy_4bid")]
	public string BidPrice4 { get; set; }
	[JsonProperty("buy_5bid")]
	public string BidPrice5 { get; set; }
	[JsonProperty("buy_6bid")]
	public string BidPrice6 { get; set; }
	[JsonProperty("buy_7bid")]
	public string BidPrice7 { get; set; }
	[JsonProperty("buy_8bid")]
	public string BidPrice8 { get; set; }
	[JsonProperty("buy_9bid")]
	public string BidPrice9 { get; set; }
	[JsonProperty("buy_10bid")]
	public string BidPrice10 { get; set; }
	[JsonProperty("buy_1bid_req")]
	public string BidVolume1 { get; set; }
	[JsonProperty("buy_2bid_req")]
	public string BidVolume2 { get; set; }
	[JsonProperty("buy_3bid_req")]
	public string BidVolume3 { get; set; }
	[JsonProperty("buy_4bid_req")]
	public string BidVolume4 { get; set; }
	[JsonProperty("buy_5bid_req")]
	public string BidVolume5 { get; set; }
	[JsonProperty("buy_6bid_req")]
	public string BidVolume6 { get; set; }
	[JsonProperty("buy_7bid_req")]
	public string BidVolume7 { get; set; }
	[JsonProperty("buy_8bid_req")]
	public string BidVolume8 { get; set; }
	[JsonProperty("buy_9bid_req")]
	public string BidVolume9 { get; set; }
	[JsonProperty("buy_10bid_req")]
	public string BidVolume10 { get; set; }

	public string[] AskPrices => [AskPrice1, AskPrice2, AskPrice3, AskPrice4, AskPrice5, AskPrice6, AskPrice7, AskPrice8, AskPrice9, AskPrice10];
	public string[] AskVolumes => [AskVolume1, AskVolume2, AskVolume3, AskVolume4, AskVolume5, AskVolume6, AskVolume7, AskVolume8, AskVolume9, AskVolume10];
	public string[] BidPrices => [BidPrice1, BidPrice2, BidPrice3, BidPrice4, BidPrice5, BidPrice6, BidPrice7, BidPrice8, BidPrice9, BidPrice10];
	public string[] BidVolumes => [BidVolume1, BidVolume2, BidVolume3, BidVolume4, BidVolume5, BidVolume6, BidVolume7, BidVolume8, BidVolume9, BidVolume10];
}

sealed class KiwoomDomesticMinuteCandleRequest
{
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }
	[JsonProperty("tic_scope")]
	public string Interval { get; set; }
	[JsonProperty("upd_stkpc_tp")]
	public string AdjustedType { get; set; } = "1";
	[JsonProperty("base_dt")]
	public string BaseDate { get; set; }
}

sealed class KiwoomDomesticDailyCandleRequest
{
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }
	[JsonProperty("base_dt")]
	public string BaseDate { get; set; }
	[JsonProperty("upd_stkpc_tp")]
	public string AdjustedType { get; set; } = "1";
}

sealed class KiwoomDomesticMinuteCandleResponse : KiwoomResponse
{
	[JsonProperty("stk_min_pole_chart_qry")]
	public KiwoomDomesticCandle[] Candles { get; set; }
}

sealed class KiwoomDomesticDailyCandleResponse : KiwoomResponse
{
	[JsonProperty("stk_dt_pole_chart_qry")]
	public KiwoomDomesticCandle[] Candles { get; set; }
}

sealed class KiwoomDomesticCandle
{
	[JsonProperty("cur_prc")]
	public string Close { get; set; }
	[JsonProperty("trde_qty")]
	public string Volume { get; set; }
	[JsonProperty("trde_prica")]
	public string Turnover { get; set; }
	[JsonProperty("cntr_tm")]
	public string DateTime { get; set; }
	[JsonProperty("dt")]
	public string Date { get; set; }
	[JsonProperty("open_pric")]
	public string Open { get; set; }
	[JsonProperty("high_pric")]
	public string High { get; set; }
	[JsonProperty("low_pric")]
	public string Low { get; set; }
}

sealed class KiwoomUsMinuteCandleRequest
{
	[JsonProperty("stex_tp")]
	public string ExchangeType { get; set; }
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }
	[JsonProperty("strt_dt")]
	public string StartDate { get; set; }
	[JsonProperty("tic_scope")]
	public string Interval { get; set; }
	[JsonProperty("upd_stkpc_tp")]
	public string AdjustedType { get; set; } = "1";
	[JsonProperty("exrt_appl_tp")]
	public string ExchangeRateType { get; set; } = "0";
}

sealed class KiwoomUsDailyCandleRequest
{
	[JsonProperty("stex_tp")]
	public string ExchangeType { get; set; }
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }
	[JsonProperty("strt_dt")]
	public string StartDate { get; set; }
	[JsonProperty("upd_stkpc_tp")]
	public string AdjustedType { get; set; } = "1";
	[JsonProperty("exrt_appl_tp")]
	public string ExchangeRateType { get; set; } = "0";
}

sealed class KiwoomUsCandleResponse : KiwoomResponse
{
	[JsonProperty("result_list")]
	public KiwoomUsCandle[] Candles { get; set; }
}

sealed class KiwoomUsCandle
{
	[JsonProperty("cur_prc")]
	public string Close { get; set; }
	[JsonProperty("trde_qty")]
	public string Volume { get; set; }
	[JsonProperty("acc_trde_qty")]
	public string AccumulatedVolume { get; set; }
	[JsonProperty("acc_trde_prica")]
	public string Turnover { get; set; }
	[JsonProperty("cntr_tm")]
	public string Time { get; set; }
	[JsonProperty("bus_dt")]
	public string BusinessDate { get; set; }
	[JsonProperty("dt")]
	public string Date { get; set; }
	[JsonProperty("open_pric")]
	public string Open { get; set; }
	[JsonProperty("high_pric")]
	public string High { get; set; }
	[JsonProperty("low_pric")]
	public string Low { get; set; }
}
