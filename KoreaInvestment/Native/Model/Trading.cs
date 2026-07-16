namespace StockSharp.KoreaInvestment.Native.Model;

sealed class KisDomesticOrderRequest
{
	[JsonProperty("CANO")]
	public string AccountNumber { get; set; }

	[JsonProperty("ACNT_PRDT_CD")]
	public string ProductCode { get; set; }

	[JsonProperty("PDNO")]
	public string ProductNumber { get; set; }

	[JsonProperty("ORD_DVSN")]
	public string OrderDivision { get; set; }

	[JsonProperty("ORD_QTY")]
	public string Quantity { get; set; }

	[JsonProperty("ORD_UNPR")]
	public string Price { get; set; }

	[JsonProperty("EXCG_ID_DVSN_CD")]
	public string ExchangeCode { get; set; }

	[JsonProperty("SLL_TYPE")]
	public string SellType { get; set; }

	[JsonProperty("CNDT_PRIC")]
	public string ConditionPrice { get; set; }
}

sealed class KisDomesticCancelRequest
{
	[JsonProperty("CANO")]
	public string AccountNumber { get; set; }

	[JsonProperty("ACNT_PRDT_CD")]
	public string ProductCode { get; set; }

	[JsonProperty("KRX_FWDG_ORD_ORGNO")]
	public string OrganizationNumber { get; set; }

	[JsonProperty("ORGN_ODNO")]
	public string OriginalOrderNumber { get; set; }

	[JsonProperty("ORD_DVSN")]
	public string OrderDivision { get; set; }

	[JsonProperty("RVSE_CNCL_DVSN_CD")]
	public string RevisionCancelCode { get; set; } = "02";

	[JsonProperty("ORD_QTY")]
	public string Quantity { get; set; }

	[JsonProperty("ORD_UNPR")]
	public string Price { get; set; }

	[JsonProperty("QTY_ALL_ORD_YN")]
	public string IsAllQuantity { get; set; } = "Y";

	[JsonProperty("EXCG_ID_DVSN_CD")]
	public string ExchangeCode { get; set; }

	[JsonProperty("CNDT_PRIC")]
	public string ConditionPrice { get; set; }
}

sealed class KisDerivativeOrderRequest
{
	[JsonProperty("ORD_PRCS_DVSN_CD")]
	public string ProcessingCode { get; set; } = "02";

	[JsonProperty("CANO")]
	public string AccountNumber { get; set; }

	[JsonProperty("ACNT_PRDT_CD")]
	public string ProductCode { get; set; }

	[JsonProperty("SLL_BUY_DVSN_CD")]
	public string SideCode { get; set; }

	[JsonProperty("SHTN_PDNO")]
	public string ProductNumber { get; set; }

	[JsonProperty("ORD_QTY")]
	public string Quantity { get; set; }

	[JsonProperty("UNIT_PRICE")]
	public string Price { get; set; }

	[JsonProperty("NMPR_TYPE_CD")]
	public string QuoteTypeCode { get; set; }

	[JsonProperty("KRX_NMPR_CNDT_CD")]
	public string QuoteConditionCode { get; set; }

	[JsonProperty("ORD_DVSN_CD")]
	public string OrderDivisionCode { get; set; }

	[JsonProperty("CTAC_TLNO")]
	public string ContactNumber { get; set; } = string.Empty;

	[JsonProperty("FUOP_ITEM_DVSN_CD")]
	public string ItemDivisionCode { get; set; } = string.Empty;
}

sealed class KisDerivativeCancelRequest
{
	[JsonProperty("ORD_PRCS_DVSN_CD")]
	public string ProcessingCode { get; set; } = "02";

	[JsonProperty("CANO")]
	public string AccountNumber { get; set; }

	[JsonProperty("ACNT_PRDT_CD")]
	public string ProductCode { get; set; }

	[JsonProperty("RVSE_CNCL_DVSN_CD")]
	public string RevisionCancelCode { get; set; } = "02";

	[JsonProperty("ORGN_ODNO")]
	public string OriginalOrderNumber { get; set; }

	[JsonProperty("ORD_QTY")]
	public string Quantity { get; set; } = "0";

	[JsonProperty("UNIT_PRICE")]
	public string Price { get; set; } = "0";

	[JsonProperty("NMPR_TYPE_CD")]
	public string QuoteTypeCode { get; set; } = "02";

	[JsonProperty("KRX_NMPR_CNDT_CD")]
	public string QuoteConditionCode { get; set; } = "0";

	[JsonProperty("RMN_QTY_YN")]
	public string IsRemainingQuantity { get; set; } = "Y";

	[JsonProperty("ORD_DVSN_CD")]
	public string OrderDivisionCode { get; set; } = "01";

	[JsonProperty("FUOP_ITEM_DVSN_CD")]
	public string ItemDivisionCode { get; set; } = string.Empty;
}

sealed class KisOverseasOrderRequest
{
	[JsonProperty("CANO")]
	public string AccountNumber { get; set; }

	[JsonProperty("ACNT_PRDT_CD")]
	public string ProductCode { get; set; }

	[JsonProperty("OVRS_EXCG_CD")]
	public string ExchangeCode { get; set; }

	[JsonProperty("PDNO")]
	public string ProductNumber { get; set; }

	[JsonProperty("ORD_QTY")]
	public string Quantity { get; set; }

	[JsonProperty("OVRS_ORD_UNPR")]
	public string Price { get; set; }

	[JsonProperty("CTAC_TLNO")]
	public string ContactNumber { get; set; } = string.Empty;

	[JsonProperty("MGCO_APTM_ODNO")]
	public string ManagementOrderNumber { get; set; } = string.Empty;

	[JsonProperty("SLL_TYPE")]
	public string SellType { get; set; }

	[JsonProperty("ORD_SVR_DVSN_CD")]
	public string OrderServerCode { get; set; } = "0";

	[JsonProperty("ORD_DVSN")]
	public string OrderDivision { get; set; }
}

sealed class KisOverseasCancelRequest
{
	[JsonProperty("CANO")]
	public string AccountNumber { get; set; }

	[JsonProperty("ACNT_PRDT_CD")]
	public string ProductCode { get; set; }

	[JsonProperty("OVRS_EXCG_CD")]
	public string ExchangeCode { get; set; }

	[JsonProperty("PDNO")]
	public string ProductNumber { get; set; }

	[JsonProperty("ORGN_ODNO")]
	public string OriginalOrderNumber { get; set; }

	[JsonProperty("RVSE_CNCL_DVSN_CD")]
	public string RevisionCancelCode { get; set; } = "02";

	[JsonProperty("ORD_QTY")]
	public string Quantity { get; set; } = "0";

	[JsonProperty("OVRS_ORD_UNPR")]
	public string Price { get; set; } = "0";

	[JsonProperty("CTAC_TLNO")]
	public string ContactNumber { get; set; } = string.Empty;

	[JsonProperty("MGCO_APTM_ODNO")]
	public string ManagementOrderNumber { get; set; } = string.Empty;

	[JsonProperty("ORD_SVR_DVSN_CD")]
	public string OrderServerCode { get; set; } = "0";
}

sealed class KisDomesticBalanceResponse : KisResponse
{
	[JsonProperty("output1")]
	public KisDomesticBalance[] Output1 { get; set; }

	[JsonProperty("output2")]
	public KisDomesticBalanceSummary[] Output2 { get; set; }

	[JsonProperty("ctx_area_fk100")]
	public string ContextFirst { get; set; }

	[JsonProperty("ctx_area_nk100")]
	public string ContextNext { get; set; }
}

sealed class KisDomesticBalance
{
	[JsonProperty("pdno")]
	public string ProductNumber { get; set; }

	[JsonProperty("prdt_name")]
	public string Name { get; set; }

	[JsonProperty("hldg_qty")]
	public string Quantity { get; set; }

	[JsonProperty("pchs_avg_pric")]
	public string AveragePrice { get; set; }

	[JsonProperty("prpr")]
	public string CurrentPrice { get; set; }

	[JsonProperty("evlu_pfls_amt")]
	public string UnrealizedPnL { get; set; }

	[JsonProperty("evlu_amt")]
	public string MarketValue { get; set; }
}

sealed class KisDomesticBalanceSummary
{
	[JsonProperty("dnca_tot_amt")]
	public string Cash { get; set; }

	[JsonProperty("tot_evlu_amt")]
	public string TotalValue { get; set; }
}

sealed class KisOverseasBalanceResponse : KisResponse
{
	[JsonProperty("output1")]
	public KisOverseasBalance[] Output1 { get; set; }

	[JsonProperty("output2")]
	public KisOverseasBalanceSummary Output2 { get; set; }

	[JsonProperty("ctx_area_fk200")]
	public string ContextFirst { get; set; }

	[JsonProperty("ctx_area_nk200")]
	public string ContextNext { get; set; }
}

sealed class KisOverseasBalance
{
	[JsonProperty("ovrs_pdno")]
	public string ProductNumber { get; set; }

	[JsonProperty("ovrs_item_name")]
	public string Name { get; set; }

	[JsonProperty("ovrs_excg_cd")]
	public string ExchangeCode { get; set; }

	[JsonProperty("ovrs_cblc_qty")]
	public string Quantity { get; set; }

	[JsonProperty("pchs_avg_pric")]
	public string AveragePrice { get; set; }

	[JsonProperty("now_pric2")]
	public string CurrentPrice { get; set; }

	[JsonProperty("frcr_evlu_pfls_amt")]
	public string UnrealizedPnL { get; set; }

	[JsonProperty("ovrs_stck_evlu_amt")]
	public string MarketValue { get; set; }

	[JsonProperty("tr_crcy_cd")]
	public string Currency { get; set; }
}

sealed class KisOverseasBalanceSummary
{
	[JsonProperty("frcr_dncl_amt_2")]
	public string Cash { get; set; }
}

sealed class KisDerivativeBalanceResponse : KisResponse
{
	[JsonProperty("output1")]
	public KisDerivativeBalance[] Output1 { get; set; }

	[JsonProperty("output2")]
	public KisDerivativeBalanceSummary Output2 { get; set; }
}

sealed class KisDerivativeBalance
{
	[JsonProperty("shtn_pdno")]
	public string ProductNumber { get; set; }

	[JsonProperty("prdt_name")]
	public string Name { get; set; }

	[JsonProperty("cblc_qty")]
	public string Quantity { get; set; }

	[JsonProperty("ccld_avg_unpr")]
	public string AveragePrice { get; set; }

	[JsonProperty("avg_unpr")]
	public string AlternativeAveragePrice { get; set; }

	[JsonProperty("now_pric")]
	public string CurrentPrice { get; set; }

	[JsonProperty("evlu_pfls_amt")]
	public string UnrealizedPnL { get; set; }

	[JsonProperty("evlu_amt")]
	public string MarketValue { get; set; }
}

sealed class KisDerivativeBalanceSummary
{
	[JsonProperty("dnca_cash")]
	public string Cash { get; set; }
}

sealed class KisDomesticExecutionsResponse : KisResponse
{
	[JsonProperty("output1")]
	public KisExecutionItem[] Output1 { get; set; }

	[JsonProperty("ctx_area_fk100")]
	public string ContextFirst { get; set; }

	[JsonProperty("ctx_area_nk100")]
	public string ContextNext { get; set; }
}

sealed class KisOverseasExecutionsResponse : KisResponse
{
	[JsonProperty("output")]
	public KisExecutionItem[] Output { get; set; }

	[JsonProperty("ctx_area_fk200")]
	public string ContextFirst { get; set; }

	[JsonProperty("ctx_area_nk200")]
	public string ContextNext { get; set; }
}

sealed class KisDerivativeExecutionsResponse : KisResponse
{
	[JsonProperty("output1")]
	public KisExecutionItem[] Output1 { get; set; }
}

sealed class KisExecutionItem
{
	[JsonProperty("odno")]
	public string OrderNumber { get; set; }

	[JsonProperty("orgn_odno")]
	public string OriginalOrderNumber { get; set; }

	[JsonProperty("pdno")]
	public string ProductNumber { get; set; }

	[JsonProperty("shtn_pdno")]
	public string ShortProductNumber { get; set; }

	[JsonProperty("prdt_name")]
	public string Name { get; set; }

	[JsonProperty("ovrs_item_name")]
	public string OverseasName { get; set; }

	[JsonProperty("sll_buy_dvsn_cd")]
	public string SideCode { get; set; }

	[JsonProperty("ord_qty")]
	public string OrderQuantity { get; set; }

	[JsonProperty("ord_unpr")]
	public string OrderPrice { get; set; }

	[JsonProperty("ovrs_ord_unpr")]
	public string OverseasOrderPrice { get; set; }

	[JsonProperty("tot_ccld_qty")]
	public string FilledQuantity { get; set; }

	[JsonProperty("ccld_qty")]
	public string AlternativeFilledQuantity { get; set; }

	[JsonProperty("avg_prvs")]
	public string AveragePrice { get; set; }

	[JsonProperty("ccld_unpr3")]
	public string AlternativeAveragePrice { get; set; }

	[JsonProperty("ord_dt")]
	public string OrderDate { get; set; }

	[JsonProperty("ord_tmd")]
	public string OrderTime { get; set; }

	[JsonProperty("cncl_yn")]
	public string IsCanceled { get; set; }

	[JsonProperty("ovrs_excg_cd")]
	public string ExchangeCode { get; set; }
}
