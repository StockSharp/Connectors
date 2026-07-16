namespace StockSharp.KoreaInvestment.Native.Model;

class KisDomesticQuoteQuery
{
	[JsonProperty("FID_COND_MRKT_DIV_CODE")]
	public string MarketCode { get; set; }

	[JsonProperty("FID_INPUT_ISCD")]
	public string SecurityCode { get; set; }
}

sealed class KisDomesticCandleQuery : KisDomesticQuoteQuery
{
	[JsonProperty("FID_INPUT_DATE_1")]
	public string From { get; set; }

	[JsonProperty("FID_INPUT_DATE_2")]
	public string To { get; set; }

	[JsonProperty("FID_PERIOD_DIV_CODE")]
	public string Period { get; set; }

	[JsonProperty("FID_ORG_ADJ_PRC")]
	public string IsAdjusted { get; set; } = "0";
}

sealed class KisDomesticMinuteCandleQuery : KisDomesticQuoteQuery
{
	[JsonProperty("FID_INPUT_HOUR_1")]
	public string Time { get; set; }

	[JsonProperty("FID_INPUT_DATE_1")]
	public string Date { get; set; }

	[JsonProperty("FID_PW_DATA_INCU_YN")]
	public string IsPastIncluded { get; set; } = "Y";

	[JsonProperty("FID_FAKE_TICK_INCU_YN")]
	public string IsEmptyTickIncluded { get; set; } = "N";
}

class KisDerivativeQuoteQuery
{
	[JsonProperty("FID_COND_MRKT_DIV_CODE")]
	public string MarketCode { get; set; }

	[JsonProperty("FID_INPUT_ISCD")]
	public string SecurityCode { get; set; }
}

sealed class KisDerivativeCandleQuery : KisDerivativeQuoteQuery
{
	[JsonProperty("FID_INPUT_DATE_1")]
	public string From { get; set; }

	[JsonProperty("FID_INPUT_DATE_2")]
	public string To { get; set; }

	[JsonProperty("FID_PERIOD_DIV_CODE")]
	public string Period { get; set; }
}

sealed class KisDerivativeMinuteCandleQuery : KisDerivativeQuoteQuery
{
	[JsonProperty("FID_HOUR_CLS_CODE")]
	public string IntervalSeconds { get; set; } = "60";

	[JsonProperty("FID_PW_DATA_INCU_YN")]
	public string IsPastIncluded { get; set; } = "Y";

	[JsonProperty("FID_FAKE_TICK_INCU_YN")]
	public string IsEmptyTickIncluded { get; set; } = "N";

	[JsonProperty("FID_INPUT_DATE_1")]
	public string Date { get; set; }

	[JsonProperty("FID_INPUT_HOUR_1")]
	public string Time { get; set; }
}

class KisOverseasQuoteQuery
{
	[JsonProperty("AUTH")]
	public string Authority { get; set; } = string.Empty;

	[JsonProperty("EXCD")]
	public string ExchangeCode { get; set; }

	[JsonProperty("SYMB")]
	public string Symbol { get; set; }
}

sealed class KisOverseasCandleQuery : KisOverseasQuoteQuery
{
	[JsonProperty("GUBN")]
	public string Period { get; set; } = "0";

	[JsonProperty("BYMD")]
	public string BeforeDate { get; set; }

	[JsonProperty("MODP")]
	public string IsAdjusted { get; set; } = "0";
}

sealed class KisOverseasMinuteCandleQuery : KisOverseasQuoteQuery
{
	[JsonProperty("NMIN")]
	public string IntervalMinutes { get; set; }

	[JsonProperty("PINC")]
	public string IsPreviousIncluded { get; set; } = "1";

	[JsonProperty("NEXT")]
	public string Next { get; set; } = string.Empty;

	[JsonProperty("NREC")]
	public string Count { get; set; } = "120";

	[JsonProperty("FILL")]
	public string Fill { get; set; } = string.Empty;

	[JsonProperty("KEYB")]
	public string NextKey { get; set; } = string.Empty;
}

sealed class KisDomesticBalanceQuery
{
	[JsonProperty("CANO")]
	public string AccountNumber { get; set; }

	[JsonProperty("ACNT_PRDT_CD")]
	public string ProductCode { get; set; }

	[JsonProperty("AFHR_FLPR_YN")]
	public string IsAfterHours { get; set; } = "N";

	[JsonProperty("OFL_YN")]
	public string IsOffline { get; set; } = string.Empty;

	[JsonProperty("INQR_DVSN")]
	public string InquiryDivision { get; set; } = "02";

	[JsonProperty("UNPR_DVSN")]
	public string PriceDivision { get; set; } = "01";

	[JsonProperty("FUND_STTL_ICLD_YN")]
	public string IsFundSettlementIncluded { get; set; } = "N";

	[JsonProperty("FNCG_AMT_AUTO_RDPT_YN")]
	public string IsLoanAutoRepayment { get; set; } = "N";

	[JsonProperty("PRCS_DVSN")]
	public string ProcessingDivision { get; set; } = "00";

	[JsonProperty("CTX_AREA_FK100")]
	public string ContextFirst { get; set; } = string.Empty;

	[JsonProperty("CTX_AREA_NK100")]
	public string ContextNext { get; set; } = string.Empty;
}

sealed class KisOverseasBalanceQuery
{
	[JsonProperty("CANO")]
	public string AccountNumber { get; set; }

	[JsonProperty("ACNT_PRDT_CD")]
	public string ProductCode { get; set; }

	[JsonProperty("OVRS_EXCG_CD")]
	public string ExchangeCode { get; set; }

	[JsonProperty("TR_CRCY_CD")]
	public string CurrencyCode { get; set; }

	[JsonProperty("CTX_AREA_FK200")]
	public string ContextFirst { get; set; } = string.Empty;

	[JsonProperty("CTX_AREA_NK200")]
	public string ContextNext { get; set; } = string.Empty;
}

sealed class KisDerivativeBalanceQuery
{
	[JsonProperty("CANO")]
	public string AccountNumber { get; set; }

	[JsonProperty("ACNT_PRDT_CD")]
	public string ProductCode { get; set; }

	[JsonProperty("MGNA_DVSN")]
	public string MarginDivision { get; set; } = "01";

	[JsonProperty("EXCC_STAT_CD")]
	public string ExerciseStatusCode { get; set; } = "1";

	[JsonProperty("CTX_AREA_FK200")]
	public string ContextFirst { get; set; } = string.Empty;

	[JsonProperty("CTX_AREA_NK200")]
	public string ContextNext { get; set; } = string.Empty;
}

sealed class KisDomesticExecutionsQuery
{
	[JsonProperty("CANO")]
	public string AccountNumber { get; set; }

	[JsonProperty("ACNT_PRDT_CD")]
	public string ProductCode { get; set; }

	[JsonProperty("INQR_STRT_DT")]
	public string From { get; set; }

	[JsonProperty("INQR_END_DT")]
	public string To { get; set; }

	[JsonProperty("SLL_BUY_DVSN_CD")]
	public string SideCode { get; set; } = "00";

	[JsonProperty("PDNO")]
	public string ProductNumber { get; set; } = string.Empty;

	[JsonProperty("CCLD_DVSN")]
	public string ExecutionDivision { get; set; } = "00";

	[JsonProperty("INQR_DVSN")]
	public string InquiryDivision { get; set; } = "00";

	[JsonProperty("INQR_DVSN_3")]
	public string InquiryDivision3 { get; set; } = "00";

	[JsonProperty("ORD_GNO_BRNO")]
	public string BranchNumber { get; set; } = string.Empty;

	[JsonProperty("ODNO")]
	public string OrderNumber { get; set; } = string.Empty;

	[JsonProperty("INQR_DVSN_1")]
	public string InquiryDivision1 { get; set; } = string.Empty;

	[JsonProperty("CTX_AREA_FK100")]
	public string ContextFirst { get; set; } = string.Empty;

	[JsonProperty("CTX_AREA_NK100")]
	public string ContextNext { get; set; } = string.Empty;

	[JsonProperty("EXCG_ID_DVSN_CD")]
	public string ExchangeCode { get; set; } = "ALL";
}

sealed class KisOverseasExecutionsQuery
{
	[JsonProperty("CANO")]
	public string AccountNumber { get; set; }

	[JsonProperty("ACNT_PRDT_CD")]
	public string ProductCode { get; set; }

	[JsonProperty("PDNO")]
	public string ProductNumber { get; set; } = "%";

	[JsonProperty("ORD_STRT_DT")]
	public string From { get; set; }

	[JsonProperty("ORD_END_DT")]
	public string To { get; set; }

	[JsonProperty("SLL_BUY_DVSN")]
	public string SideCode { get; set; } = "00";

	[JsonProperty("CCLD_NCCS_DVSN")]
	public string ExecutionDivision { get; set; } = "00";

	[JsonProperty("OVRS_EXCG_CD")]
	public string ExchangeCode { get; set; } = "%";

	[JsonProperty("SORT_SQN")]
	public string SortOrder { get; set; } = "DS";

	[JsonProperty("ORD_DT")]
	public string OrderDate { get; set; } = string.Empty;

	[JsonProperty("ORD_GNO_BRNO")]
	public string BranchNumber { get; set; } = string.Empty;

	[JsonProperty("ODNO")]
	public string OrderNumber { get; set; } = string.Empty;

	[JsonProperty("CTX_AREA_FK200")]
	public string ContextFirst { get; set; } = string.Empty;

	[JsonProperty("CTX_AREA_NK200")]
	public string ContextNext { get; set; } = string.Empty;
}

sealed class KisDerivativeExecutionsQuery
{
	[JsonProperty("CANO")]
	public string AccountNumber { get; set; }

	[JsonProperty("ACNT_PRDT_CD")]
	public string ProductCode { get; set; }

	[JsonProperty("STRT_ORD_DT")]
	public string From { get; set; }

	[JsonProperty("END_ORD_DT")]
	public string To { get; set; }

	[JsonProperty("SLL_BUY_DVSN_CD")]
	public string SideCode { get; set; } = "00";

	[JsonProperty("CCLD_NCCS_DVSN")]
	public string ExecutionDivision { get; set; } = "00";

	[JsonProperty("SORT_SQN")]
	public string SortOrder { get; set; } = "DS";

	[JsonProperty("STRT_ODNO")]
	public string StartOrderNumber { get; set; } = string.Empty;

	[JsonProperty("PDNO")]
	public string ProductNumber { get; set; } = string.Empty;

	[JsonProperty("MKET_ID_CD")]
	public string MarketId { get; set; } = string.Empty;

	[JsonProperty("CTX_AREA_FK200")]
	public string ContextFirst { get; set; } = string.Empty;

	[JsonProperty("CTX_AREA_NK200")]
	public string ContextNext { get; set; } = string.Empty;
}
