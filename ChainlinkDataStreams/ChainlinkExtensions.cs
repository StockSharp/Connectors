namespace StockSharp.ChainlinkDataStreams;

static class ChainlinkExtensions
{
    public const decimal ValueScale = 1_000_000_000_000_000_000m;

    public static DateTime EnsureUtc(this DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static ChainlinkFeedInfo ParseFeed(this string value)
    {
        value = value?.Trim();
        if (value.IsEmpty() || value.Length != 66 ||
            !value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            throw new FormatException(
                "A Chainlink feed ID must contain 0x followed by 64 hexadecimal characters.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(value[2..]);
        }
        catch (FormatException error)
        {
            throw new FormatException("The Chainlink feed ID is not hexadecimal.",
                error);
        }

        var version = (ChainlinkReportSchemas)(
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0, 2)) & 0x0FFF);
        if (version is < ChainlinkReportSchemas.LegacyCrypto or
            > ChainlinkReportSchemas.BestPrices)
            throw new NotSupportedException(
                $"Chainlink report schema version {(ushort)version} is not supported.");

        var resolution = (ChainlinkTimestampResolutions)(bytes[0] >> 4);
        if (resolution is not ChainlinkTimestampResolutions.Seconds and
            not ChainlinkTimestampResolutions.Milliseconds)
            throw new NotSupportedException(
                $"Chainlink timestamp resolution {(byte)resolution} is not supported.");

        return new()
        {
            FeedId = "0x" + Convert.ToHexString(bytes).ToLowerInvariant(),
            Schema = version,
            Resolution = resolution,
        };
    }

    public static string GetSchemaName(this ChainlinkReportSchemas value)
        => value switch
        {
            ChainlinkReportSchemas.LegacyCrypto => "Legacy Crypto v1",
            ChainlinkReportSchemas.LegacyBasic => "Legacy Basic v2",
            ChainlinkReportSchemas.CryptoAdvanced => "Crypto Advanced v3",
            ChainlinkReportSchemas.LegacyMarketStatus =>
                "Legacy Market Status v4",
            ChainlinkReportSchemas.Rate => "Rate v5",
            ChainlinkReportSchemas.MultiValue => "Multi-Value v6",
            ChainlinkReportSchemas.RedemptionRate => "Redemption Rate v7",
            ChainlinkReportSchemas.RwaStandard => "RWA Standard v8",
            ChainlinkReportSchemas.SmartData => "SmartData v9",
            ChainlinkReportSchemas.TokenizedAsset => "Tokenized Asset v10",
            ChainlinkReportSchemas.RwaAdvanced => "RWA Advanced v11",
            ChainlinkReportSchemas.SmartDataProjected =>
                "SmartData Projected NAV v12",
            ChainlinkReportSchemas.BestPrices => "Best Prices v13",
            _ => "Unknown",
        };

    public static SecurityTypes ToSecurityType(
        this ChainlinkReportSchemas value)
        => value switch
        {
            ChainlinkReportSchemas.CryptoAdvanced or
            ChainlinkReportSchemas.LegacyCrypto => SecurityTypes.CryptoCurrency,
            ChainlinkReportSchemas.SmartData or
            ChainlinkReportSchemas.SmartDataProjected => SecurityTypes.Fund,
            ChainlinkReportSchemas.TokenizedAsset => SecurityTypes.Stock,
            _ => SecurityTypes.Index,
        };

    public static SecurityStates? ToSecurityState(
        this ChainlinkDecodedReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.IsRipcord is true)
            return SecurityStates.Stoped;
        if (report.IsRipcord is false)
            return SecurityStates.Trading;
        if (report.MarketStatus == ChainlinkMarketStatuses.Unknown)
            return null;

        return report.Schema == ChainlinkReportSchemas.RwaAdvanced
            ? report.MarketStatus == ChainlinkMarketStatuses.Closed
                ? SecurityStates.Stoped
                : SecurityStates.Trading
            : report.MarketStatus == ChainlinkMarketStatuses.ClosedOrPreMarket
                ? SecurityStates.Stoped
                : report.MarketStatus == ChainlinkMarketStatuses.Open
                    ? SecurityStates.Trading
                    : null;
    }

    public static DateTime ParseTimestamp(this ulong value,
        ChainlinkTimestampResolutions resolution, string name)
    {
        try
        {
            return checked((long)value).FromUnix(
                resolution == ChainlinkTimestampResolutions.Seconds);
        }
        catch (Exception error) when (error is OverflowException or
            ArgumentOutOfRangeException)
        {
            throw new InvalidDataException(
                $"Chainlink {name} is outside the UTC DateTime range.", error);
        }
    }

    public static DateTime ParseNanoseconds(this ulong value, string name)
    {
        try
        {
            var ticks = checked((long)(value / 100));
            return DateTime.UnixEpoch.AddTicks(ticks);
        }
        catch (Exception error) when (error is OverflowException or
            ArgumentOutOfRangeException)
        {
            throw new InvalidDataException(
                $"Chainlink {name} is outside the UTC DateTime range.", error);
        }
    }

    public static Uri ValidateRestEndpoint(string endpoint)
        => ValidateEndpoint(endpoint, Uri.UriSchemeHttps, "REST");

    public static Uri ValidateWebSocketEndpoint(string endpoint)
        => ValidateEndpoint(endpoint, "wss", "WebSocket");

    private static Uri ValidateEndpoint(string endpoint, string scheme,
        string name)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(scheme, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.IsEmpty() || !uri.UserInfo.IsEmpty() ||
            !uri.Query.IsEmpty() || !uri.Fragment.IsEmpty() ||
            uri.AbsolutePath is not "/")
            throw new ArgumentException(
                $"Chainlink {name} endpoint must be an absolute {scheme.ToUpperInvariant()} root URI without credentials, query, or fragment.",
                nameof(endpoint));
        return uri;
    }
}
