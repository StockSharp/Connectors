namespace StockSharp.KrxOpenApi;

/// <summary>
/// Public KRX daily datasets exposed by the adapter.
/// </summary>
[DataContract]
public enum KrxDataSets
{
    /// <summary>KOSPI-listed stocks.</summary>
    [EnumMember]
    [Display(Name = "KOSPI stocks")]
    KospiStocks,

    /// <summary>KOSDAQ-listed stocks.</summary>
    [EnumMember]
    [Display(Name = "KOSDAQ stocks")]
    KosdaqStocks,

    /// <summary>KONEX-listed stocks.</summary>
    [EnumMember]
    [Display(Name = "KONEX stocks")]
    KonexStocks,

    /// <summary>Exchange-traded funds.</summary>
    [EnumMember]
    [Display(Name = "ETF")]
    Etf,

    /// <summary>Exchange-traded notes.</summary>
    [EnumMember]
    [Display(Name = "ETN")]
    Etn,

    /// <summary>KRX index family.</summary>
    [EnumMember]
    [Display(Name = "KRX indices")]
    KrxIndices,

    /// <summary>KOSPI index family.</summary>
    [EnumMember]
    [Display(Name = "KOSPI indices")]
    KospiIndices,

    /// <summary>KOSDAQ index family.</summary>
    [EnumMember]
    [Display(Name = "KOSDAQ indices")]
    KosdaqIndices,
}
