namespace StockSharp.KrxOpenApi;

/// <summary>
/// Public KRX daily datasets exposed by the adapter.
/// </summary>
[DataContract]
public enum KrxDataSets
{
    /// <summary>KOSPI-listed stocks.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KospiStocksKey)]
    KospiStocks,

    /// <summary>KOSDAQ-listed stocks.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KosdaqStocksKey)]
    KosdaqStocks,

    /// <summary>KONEX-listed stocks.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KonexStocksKey)]
    KonexStocks,

    /// <summary>Exchange-traded funds.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.EtfKey)]
    Etf,

    /// <summary>Exchange-traded notes.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.EtnKey)]
    Etn,

    /// <summary>KRX index family.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KrxIndicesKey)]
    KrxIndices,

    /// <summary>KOSPI index family.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KospiIndicesKey)]
    KospiIndices,

    /// <summary>KOSDAQ index family.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KosdaqIndicesKey)]
    KosdaqIndices,
}
