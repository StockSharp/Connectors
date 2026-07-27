namespace StockSharp.KrxOpenApi.Native;

sealed class KrxResponse<T>
{
    [JsonProperty("OutBlock_1")]
    public T[] Items { get; set; }
}

sealed class KrxDailyRow
{
    [JsonProperty("BAS_DD")]
    public string BaseDate { get; set; }

    [JsonProperty("ISU_CD")]
    public string IssueCode { get; set; }

    [JsonProperty("ISU_NM")]
    public string IssueName { get; set; }

    [JsonProperty("MKT_NM")]
    public string MarketName { get; set; }

    [JsonProperty("SECT_TP_NM")]
    public string SectionName { get; set; }

    [JsonProperty("IDX_CLSS")]
    public string IndexClass { get; set; }

    [JsonProperty("IDX_NM")]
    public string IndexName { get; set; }

    [JsonProperty("TDD_CLSPRC")]
    public string ClosePrice { get; set; }

    [JsonProperty("CLSPRC_IDX")]
    public string IndexClosePrice { get; set; }

    [JsonProperty("CMPPREVDD_PRC")]
    public string PreviousDayChange { get; set; }

    [JsonProperty("CMPPREVDD_IDX")]
    public string IndexPreviousDayChange { get; set; }

    [JsonProperty("FLUC_RT")]
    public string ChangePercent { get; set; }

    [JsonProperty("TDD_OPNPRC")]
    public string OpenPrice { get; set; }

    [JsonProperty("OPNPRC_IDX")]
    public string IndexOpenPrice { get; set; }

    [JsonProperty("TDD_HGPRC")]
    public string HighPrice { get; set; }

    [JsonProperty("HGPRC_IDX")]
    public string IndexHighPrice { get; set; }

    [JsonProperty("TDD_LWPRC")]
    public string LowPrice { get; set; }

    [JsonProperty("LWPRC_IDX")]
    public string IndexLowPrice { get; set; }

    [JsonProperty("ACC_TRDVOL")]
    public string Volume { get; set; }

    [JsonProperty("ACC_TRDVAL")]
    public string Turnover { get; set; }

    [JsonProperty("MKTCAP")]
    public string MarketCapitalization { get; set; }

    [JsonProperty("LIST_SHRS")]
    public string ListedShares { get; set; }

    [JsonProperty("NAV")]
    public string NetAssetValue { get; set; }

    [JsonProperty("PER1SECU_INDIC_VAL")]
    public string IndicativeValue { get; set; }

    [JsonProperty("IDX_IND_NM")]
    public string UnderlyingIndexName { get; set; }
}

sealed class KrxSecurityInfoRow
{
    [JsonProperty("ISU_CD")]
    public string Isin { get; set; }

    [JsonProperty("ISU_SRT_CD")]
    public string IssueCode { get; set; }

    [JsonProperty("ISU_NM")]
    public string IssueName { get; set; }

    [JsonProperty("ISU_ABBRV")]
    public string AbbreviatedName { get; set; }

    [JsonProperty("ISU_ENG_NM")]
    public string EnglishName { get; set; }

    [JsonProperty("LIST_DD")]
    public string ListingDate { get; set; }

    [JsonProperty("MKT_TP_NM")]
    public string MarketName { get; set; }

    [JsonProperty("SECUGRP_NM")]
    public string SecurityGroupName { get; set; }

    [JsonProperty("SECT_TP_NM")]
    public string SectionName { get; set; }

    [JsonProperty("KIND_STKCERT_TP_NM")]
    public string StockCertificateTypeName { get; set; }

    [JsonProperty("PARVAL")]
    public string ParValue { get; set; }

    [JsonProperty("LIST_SHRS")]
    public string ListedShares { get; set; }
}
