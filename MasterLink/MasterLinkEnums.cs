namespace StockSharp.MasterLink;

/// <summary>Taishin Nova market-data connection modes.</summary>
[DataContract]
[Serializable]
public enum MasterLinkMarketDataModes
{
    /// <summary>Normal market-data endpoint.</summary>
    [EnumMember]
    Normal,

    /// <summary>Low-latency market-data endpoint.</summary>
    [EnumMember]
    Speed,
}

/// <summary>Taiwan stock trading sessions supported by Nova API.</summary>
[DataContract]
[Serializable]
public enum MasterLinkMarketTypes
{
    /// <summary>Infer the session from the security board.</summary>
    [EnumMember]
    Auto,

    /// <summary>Regular board-lot session.</summary>
    [EnumMember]
    Common,

    /// <summary>After-hours fixed-price session.</summary>
    [EnumMember]
    Fixing,

    /// <summary>Intraday odd-lot session.</summary>
    [EnumMember]
    IntradayOdd,

    /// <summary>After-hours odd-lot session.</summary>
    [EnumMember]
    Odd,

    /// <summary>Emerging-stock session.</summary>
    [EnumMember]
    Emg,
}

/// <summary>Native price flags supported by Nova API.</summary>
[DataContract]
[Serializable]
public enum MasterLinkPriceTypes
{
    /// <summary>Infer the flag from the standard order type.</summary>
    [EnumMember]
    Auto,

    /// <summary>Explicit limit price.</summary>
    [EnumMember]
    Limit,

    /// <summary>Market price.</summary>
    [EnumMember]
    Market,

    /// <summary>Daily upper-limit price.</summary>
    [EnumMember]
    LimitUp,

    /// <summary>Daily lower-limit price.</summary>
    [EnumMember]
    LimitDown,

    /// <summary>Reference price.</summary>
    [EnumMember]
    Reference,
}

/// <summary>Native stock financing types supported by Nova API.</summary>
[DataContract]
[Serializable]
public enum MasterLinkOrderTypes
{
    /// <summary>Cash stock order.</summary>
    [EnumMember]
    Stock,

    /// <summary>Margin purchase.</summary>
    [EnumMember]
    Margin,

    /// <summary>Short sale.</summary>
    [EnumMember]
    Short,

    /// <summary>Cash day-trade short sale.</summary>
    [EnumMember]
    DayTradeShort,
}
