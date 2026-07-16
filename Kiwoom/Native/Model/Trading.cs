namespace StockSharp.Kiwoom.Native.Model;

sealed class KiwoomDomesticPositionsRequest
{
	[JsonProperty("qry_tp")]
	public string QueryType { get; set; } = "2";

	[JsonProperty("dmst_stex_tp")]
	public string ExchangeType { get; set; } = "KRX";
}

sealed class KiwoomDomesticPositionsResponse : KiwoomResponse
{
	[JsonProperty("acnt_evlt_remn_indv_tot")]
	public KiwoomDomesticPosition[] Positions { get; set; }
}

sealed class KiwoomDomesticPosition
{
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }
	[JsonProperty("stk_nm")]
	public string Name { get; set; }
	[JsonProperty("evltv_prft")]
	public string UnrealizedPnL { get; set; }
	[JsonProperty("pur_pric")]
	public string AveragePrice { get; set; }
	[JsonProperty("rmnd_qty")]
	public string Quantity { get; set; }
	[JsonProperty("cur_prc")]
	public string CurrentPrice { get; set; }
	[JsonProperty("pur_amt")]
	public string PurchaseAmount { get; set; }
	[JsonProperty("evlt_amt")]
	public string MarketValue { get; set; }
}

sealed class KiwoomUsPositionsRequest
{
	[JsonProperty("stex_tp")]
	public string ExchangeType { get; set; } = string.Empty;

	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; } = string.Empty;
}

sealed class KiwoomUsPositionsResponse : KiwoomResponse
{
	[JsonProperty("result_list")]
	public KiwoomUsPosition[] Positions { get; set; }
}

sealed class KiwoomUsPosition
{
	[JsonProperty("stex_nm")]
	public string ExchangeName { get; set; }
	[JsonProperty("crnc_code")]
	public string Currency { get; set; }
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }
	[JsonProperty("frgn_stk_nm")]
	public string Name { get; set; }
	[JsonProperty("qty")]
	public string Quantity { get; set; }
	[JsonProperty("frgn_stk_book_uv")]
	public string AveragePrice { get; set; }
	[JsonProperty("now_pric")]
	public string CurrentPrice { get; set; }
	[JsonProperty("evlt_amt")]
	public string MarketValue { get; set; }
	[JsonProperty("pl_amt")]
	public string UnrealizedPnL { get; set; }
}

sealed class KiwoomDomesticOpenOrdersRequest
{
	[JsonProperty("all_stk_tp")]
	public string AllSecuritiesType { get; set; } = "0";
	[JsonProperty("trde_tp")]
	public string TradeType { get; set; } = "0";
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; } = string.Empty;
	[JsonProperty("stex_tp")]
	public string ExchangeType { get; set; } = "0";
}

sealed class KiwoomDomesticOpenOrdersResponse : KiwoomResponse
{
	[JsonProperty("oso")]
	public KiwoomDomesticOrderRow[] Orders { get; set; }
}

sealed class KiwoomDomesticExecutionsRequest
{
	[JsonProperty("qry_tp")]
	public string QueryType { get; set; } = "0";
	[JsonProperty("sell_tp")]
	public string SideType { get; set; } = "0";
	[JsonProperty("stex_tp")]
	public string ExchangeType { get; set; } = "0";
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; } = string.Empty;
	[JsonProperty("ord_no")]
	public string OrderNumber { get; set; } = string.Empty;
}

sealed class KiwoomDomesticExecutionsResponse : KiwoomResponse
{
	[JsonProperty("cntr")]
	public KiwoomDomesticOrderRow[] Orders { get; set; }
}

sealed class KiwoomDomesticOrderRow
{
	[JsonProperty("ord_no")]
	public string OrderNumber { get; set; }
	[JsonProperty("orig_ord_no")]
	public string OriginalOrderNumber { get; set; }
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }
	[JsonProperty("stk_nm")]
	public string Name { get; set; }
	[JsonProperty("io_tp_nm")]
	public string SideName { get; set; }
	[JsonProperty("ord_pric")]
	public string OrderPrice { get; set; }
	[JsonProperty("ord_qty")]
	public string OrderQuantity { get; set; }
	[JsonProperty("cntr_pric")]
	public string FillPrice { get; set; }
	[JsonProperty("cntr_qty")]
	public string FillQuantity { get; set; }
	[JsonProperty("oso_qty")]
	public string Balance { get; set; }
	[JsonProperty("ord_stt")]
	public string Status { get; set; }
	[JsonProperty("trde_tp")]
	public string TradeType { get; set; }
	[JsonProperty("tm")]
	public string Time { get; set; }
	[JsonProperty("ord_tm")]
	public string OrderTime { get; set; }
	[JsonProperty("cntr_no")]
	public string TradeNumber { get; set; }
	[JsonProperty("stex_tp")]
	public string ExchangeType { get; set; }
	[JsonProperty("stex_tp_txt")]
	public string ExchangeName { get; set; }
}

sealed class KiwoomUsOpenOrdersRequest
{
	[JsonProperty("ord_dt")]
	public string OrderDate { get; set; }
	[JsonProperty("slby_tp")]
	public string SideType { get; set; } = "0";
	[JsonProperty("stex_tp")]
	public string ExchangeType { get; set; } = string.Empty;
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; } = string.Empty;
}

sealed class KiwoomUsOpenOrdersResponse : KiwoomResponse
{
	[JsonProperty("result_list")]
	public KiwoomUsOrderRow[] Orders { get; set; }
}

sealed class KiwoomUsExecutionsRequest
{
	[JsonProperty("ord_dt")]
	public string OrderDate { get; set; }
	[JsonProperty("query_tp")]
	public string QueryType { get; set; } = "1";
	[JsonProperty("slby_tp")]
	public string SideType { get; set; } = "0";
	[JsonProperty("stex_tp")]
	public string ExchangeType { get; set; } = string.Empty;
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; } = string.Empty;
	[JsonProperty("oppo_trde_tp")]
	public string OppositeTradeType { get; set; } = string.Empty;
	[JsonProperty("fr_ord_no")]
	public string ForeignOrderNumber { get; set; } = string.Empty;
}

sealed class KiwoomUsExecutionsResponse : KiwoomResponse
{
	[JsonProperty("result_list")]
	public KiwoomUsOrderRow[] Orders { get; set; }
}

sealed class KiwoomUsOrderRow
{
	[JsonProperty("ord_no")]
	public string OrderNumber { get; set; }
	[JsonProperty("orig_ord_no")]
	public string OriginalOrderNumber { get; set; }
	[JsonProperty("stex_nm")]
	public string ExchangeName { get; set; }
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }
	[JsonProperty("frgn_stk_nm")]
	public string Name { get; set; }
	[JsonProperty("slby_tp")]
	public string SideType { get; set; }
	[JsonProperty("slby_tp_nm")]
	public string SideName { get; set; }
	[JsonProperty("ord_qty")]
	public string OrderQuantity { get; set; }
	[JsonProperty("ord_uv")]
	public string OrderPrice { get; set; }
	[JsonProperty("cntr_qty")]
	public string FillQuantity { get; set; }
	[JsonProperty("cntr_uv")]
	public string FillPrice { get; set; }
	[JsonProperty("ord_remnq")]
	public string Balance { get; set; }
	[JsonProperty("ord_time")]
	public string OrderTime { get; set; }
	[JsonProperty("cntr_time")]
	public string FillTime { get; set; }
	[JsonProperty("ord_stat")]
	public string Status { get; set; }
	[JsonProperty("ord_stat_nm")]
	public string StatusName { get; set; }
	[JsonProperty("frgn_trde_tp")]
	public string TradeType { get; set; }
}

sealed class KiwoomDomesticOrderRequest
{
	[JsonProperty("dmst_stex_tp")]
	public string ExchangeType { get; set; }
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }
	[JsonProperty("ord_qty")]
	public string Quantity { get; set; }
	[JsonProperty("ord_uv")]
	public string Price { get; set; }
	[JsonProperty("trde_tp")]
	public string TradeType { get; set; }
	[JsonProperty("cond_uv")]
	public string ConditionPrice { get; set; } = string.Empty;
}

sealed class KiwoomDomesticReplaceRequest
{
	[JsonProperty("dmst_stex_tp")]
	public string ExchangeType { get; set; }
	[JsonProperty("orig_ord_no")]
	public string OriginalOrderNumber { get; set; }
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }
	[JsonProperty("mdfy_qty")]
	public string Quantity { get; set; }
	[JsonProperty("mdfy_uv")]
	public string Price { get; set; }
	[JsonProperty("mdfy_cond_uv")]
	public string ConditionPrice { get; set; } = string.Empty;
}

sealed class KiwoomDomesticCancelRequest
{
	[JsonProperty("dmst_stex_tp")]
	public string ExchangeType { get; set; }
	[JsonProperty("orig_ord_no")]
	public string OriginalOrderNumber { get; set; }
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }
	[JsonProperty("cncl_qty")]
	public string Quantity { get; set; } = "0";
}

sealed class KiwoomDomesticOrderResponse : KiwoomResponse
{
	[JsonProperty("ord_no")]
	public string OrderNumber { get; set; }
	[JsonProperty("base_orig_ord_no")]
	public string BaseOriginalOrderNumber { get; set; }
	[JsonProperty("mdfy_qty")]
	public string ModifiedQuantity { get; set; }
	[JsonProperty("cncl_qty")]
	public string CanceledQuantity { get; set; }
	[JsonProperty("dmst_stex_tp")]
	public string ExchangeType { get; set; }
}

sealed class KiwoomUsOrderRequest
{
	[JsonProperty("stex_tp")]
	public string ExchangeType { get; set; }
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }
	[JsonProperty("ord_qty")]
	public string Quantity { get; set; }
	[JsonProperty("ord_uv")]
	public string Price { get; set; }
	[JsonProperty("stop_pric")]
	public string StopPrice { get; set; }
	[JsonProperty("trde_tp")]
	public string TradeType { get; set; }
}

sealed class KiwoomUsReplaceRequest
{
	[JsonProperty("orig_ord_no")]
	public string OriginalOrderNumber { get; set; }
	[JsonProperty("stex_tp")]
	public string ExchangeType { get; set; }
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }
	[JsonProperty("mdfy_uv")]
	public string Price { get; set; }
	[JsonProperty("stop_pric")]
	public string StopPrice { get; set; }
}

sealed class KiwoomUsCancelRequest
{
	[JsonProperty("orig_ord_no")]
	public string OriginalOrderNumber { get; set; }
	[JsonProperty("stex_tp")]
	public string ExchangeType { get; set; }
	[JsonProperty("stk_cd")]
	public string SecurityCode { get; set; }
}

sealed class KiwoomUsOrderResponse : KiwoomResponse
{
	[JsonProperty("stk_nm")]
	public string SecurityName { get; set; }
	[JsonProperty("ord_no")]
	public string OrderNumber { get; set; }
	[JsonProperty("mdfy_ord_qty")]
	public string ModifiedQuantity { get; set; }
	[JsonProperty("cncl_ord_qty")]
	public string CanceledQuantity { get; set; }
}
