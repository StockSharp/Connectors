namespace StockSharp.ChainlinkDataStreams;

/// <summary>Chainlink Data Streams report schema versions.</summary>
public enum ChainlinkReportSchemas
{
    /// <summary>Unknown or unsupported schema.</summary>
    Unknown = 0,

    /// <summary>Legacy crypto schema.</summary>
    LegacyCrypto = 1,

    /// <summary>Legacy basic price schema.</summary>
    LegacyBasic = 2,

    /// <summary>Crypto Advanced schema.</summary>
    CryptoAdvanced = 3,

    /// <summary>Legacy market-status schema.</summary>
    LegacyMarketStatus = 4,

    /// <summary>Rate schema.</summary>
    Rate = 5,

    /// <summary>Multi-value schema.</summary>
    MultiValue = 6,

    /// <summary>Redemption rate schema.</summary>
    RedemptionRate = 7,

    /// <summary>RWA Standard schema.</summary>
    RwaStandard = 8,

    /// <summary>SmartData schema.</summary>
    SmartData = 9,

    /// <summary>Tokenized asset schema.</summary>
    TokenizedAsset = 10,

    /// <summary>RWA Advanced schema.</summary>
    RwaAdvanced = 11,

    /// <summary>SmartData projected NAV schema.</summary>
    SmartDataProjected = 12,

    /// <summary>Best-price schema.</summary>
    BestPrices = 13,
}

/// <summary>Timestamp resolutions encoded in a Chainlink feed ID.</summary>
public enum ChainlinkTimestampResolutions : byte
{
    /// <summary>Seconds.</summary>
    Seconds = 0,

    /// <summary>Milliseconds.</summary>
    Milliseconds = 1,
}

/// <summary>Market status values used by Chainlink report schemas.</summary>
public enum ChainlinkMarketStatuses
{
    /// <summary>Status is unknown.</summary>
    Unknown = 0,

    /// <summary>Closed for schemas v4, v8 and v10; pre-market for v11.</summary>
    ClosedOrPreMarket = 1,

    /// <summary>Open or regular trading session.</summary>
    Open = 2,

    /// <summary>Post-market session in schema v11.</summary>
    PostMarket = 3,

    /// <summary>Overnight session in schema v11.</summary>
    Overnight = 4,

    /// <summary>Closed session in schema v11.</summary>
    Closed = 5,
}
