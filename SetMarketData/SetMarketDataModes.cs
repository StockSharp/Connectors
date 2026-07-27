namespace StockSharp.SetMarketData;

/// <summary>
/// Stock Exchange of Thailand market-data modes.
/// </summary>
[DataContract]
public enum SetMarketDataModes
{
    /// <summary>Licensed real-time quotations.</summary>
    [EnumMember]
    [Display(Name = "Real-time")]
    RealTime,

    /// <summary>Licensed delayed quotations.</summary>
    [EnumMember]
    [Display(Name = "Delayed")]
    Delayed,
}
