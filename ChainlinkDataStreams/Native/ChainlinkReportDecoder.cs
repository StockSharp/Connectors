namespace StockSharp.ChainlinkDataStreams.Native;

static class ChainlinkReportDecoder
{
    private const int _wordSize = 32;
    private const int _outerHeadSize = 7 * _wordSize;
    private const int _maximumReportLength = 8 * 1024 * 1024;
    private static readonly BigInteger _valueScale =
        new(1_000_000_000_000_000_000L);
    private static readonly BigInteger _minimumInt192 =
        -(BigInteger.One << 191);
    private static readonly BigInteger _maximumInt192 =
        (BigInteger.One << 191) - 1;

    public static ChainlinkDecodedReport Decode(
        ChainlinkReportEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var outer = ParseHex(envelope.FullReport, "fullReport");
        var blob = ExtractReportBlob(outer);
        if (blob.Length < _wordSize)
            throw new InvalidDataException("Chainlink report body is empty.");

        var feedId = "0x" + Convert.ToHexString(blob[.._wordSize])
            .ToLowerInvariant();
        var feed = feedId.ParseFeed();
        var expectedLength = GetSlotCount(feed.Schema) * _wordSize;
        if (blob.Length != expectedLength)
            throw new InvalidDataException(
                $"Chainlink schema {(ushort)feed.Schema} report has {blob.Length} bytes instead of {expectedLength}.");

        if (!envelope.FeedId.IsEmpty() &&
            !envelope.FeedId.ParseFeed().FeedId.EqualsIgnoreCase(feed.FeedId))
            throw new InvalidDataException(
                "Chainlink report feed ID differs from its envelope.");

        DateTime observationTime;
        DateTime? validFromTime = null;
        DateTime? expiresAt = null;
        DateTime? valueTime = null;
        decimal? primaryPrice = null;
        decimal? lastTradePrice = null;
        decimal? bestBidPrice = null;
        decimal? bestBidVolume = null;
        decimal? bestAskPrice = null;
        decimal? bestAskVolume = null;
        var marketStatus = ChainlinkMarketStatuses.Unknown;
        bool? isRipcord = null;

        if (feed.Schema == ChainlinkReportSchemas.LegacyCrypto)
        {
            observationTime = ReadUInt64(blob, 1, "observationsTimestamp")
                .ParseTimestamp(feed.Resolution, "observationsTimestamp");
            primaryPrice = ReadScaledInt192(blob, 2, "benchmarkPrice");
            bestBidPrice = ReadScaledInt192(blob, 3, "bid");
            bestAskPrice = ReadScaledInt192(blob, 4, "ask");
            _ = ReadUInt64(blob, 5, "currentBlockNum");
            _ = ReadUInt64(blob, 7, "validFromBlockNum");
            _ = ReadUInt64(blob, 8, "currentBlockTimestamp");
        }
        else
        {
            validFromTime = ReadUInt64(blob, 1, "validFromTimestamp")
                .ParseTimestamp(feed.Resolution, "validFromTimestamp");
            observationTime = ReadUInt64(blob, 2, "observationsTimestamp")
                .ParseTimestamp(feed.Resolution, "observationsTimestamp");
            expiresAt = ReadUInt64(blob, 5, "expiresAt")
                .ParseTimestamp(feed.Resolution, "expiresAt");

            switch (feed.Schema)
            {
                case ChainlinkReportSchemas.LegacyBasic:
                    primaryPrice = ReadScaledInt192(blob, 6,
                        "benchmarkPrice");
                    break;
                case ChainlinkReportSchemas.CryptoAdvanced:
                    primaryPrice = ReadScaledInt192(blob, 6,
                        "benchmarkPrice");
                    bestBidPrice = ReadScaledInt192(blob, 7, "bid");
                    bestAskPrice = ReadScaledInt192(blob, 8, "ask");
                    break;
                case ChainlinkReportSchemas.LegacyMarketStatus:
                    primaryPrice = ReadScaledInt192(blob, 6,
                        "benchmarkPrice");
                    marketStatus = ReadMarketStatus(blob, 7,
                        ChainlinkMarketStatuses.Open);
                    break;
                case ChainlinkReportSchemas.Rate:
                    primaryPrice = ReadScaledInt192(blob, 6, "rate");
                    valueTime = ReadUInt64(blob, 7, "timestamp")
                        .ParseTimestamp(ChainlinkTimestampResolutions.Seconds,
                            "timestamp");
                    _ = ReadUInt32(blob, 8, "duration");
                    break;
                case ChainlinkReportSchemas.MultiValue:
                    primaryPrice = ReadScaledInt192(blob, 6, "price");
                    for (var index = 7; index <= 10; index++)
                        _ = ReadInt192(blob, index, "price" + (index - 5));
                    break;
                case ChainlinkReportSchemas.RedemptionRate:
                    primaryPrice = ReadScaledInt192(blob, 6, "exchangeRate");
                    break;
                case ChainlinkReportSchemas.RwaStandard:
                    valueTime = ReadUInt64(blob, 6, "lastUpdateTimestamp")
                        .ParseNanoseconds("lastUpdateTimestamp");
                    primaryPrice = ReadScaledInt192(blob, 7, "midPrice");
                    marketStatus = ReadMarketStatus(blob, 8,
                        ChainlinkMarketStatuses.Open);
                    break;
                case ChainlinkReportSchemas.SmartData:
                    primaryPrice = ReadScaledInt192(blob, 6, "navPerShare");
                    valueTime = ReadUInt64(blob, 7, "navDate")
                        .ParseNanoseconds("navDate");
                    _ = ReadInt192(blob, 8, "aum");
                    isRipcord = ReadRipcord(blob, 9);
                    break;
                case ChainlinkReportSchemas.TokenizedAsset:
                    valueTime = ReadUInt64(blob, 6, "lastUpdateTimestamp")
                        .ParseNanoseconds("lastUpdateTimestamp");
                    primaryPrice = ReadScaledInt192(blob, 7, "price");
                    lastTradePrice = primaryPrice;
                    marketStatus = ReadMarketStatus(blob, 8,
                        ChainlinkMarketStatuses.Open);
                    _ = ReadInt192(blob, 9, "currentMultiplier");
                    _ = ReadInt192(blob, 10, "newMultiplier");
                    _ = ReadUInt64(blob, 11, "activationDateTime");
                    _ = ReadInt192(blob, 12, "tokenizedPrice");
                    break;
                case ChainlinkReportSchemas.RwaAdvanced:
                    primaryPrice = ReadScaledInt192(blob, 6, "mid");
                    valueTime = ReadUInt64(blob, 7, "lastSeenTimestampNs")
                        .ParseNanoseconds("lastSeenTimestampNs");
                    bestBidPrice = ReadScaledInt192(blob, 8, "bid");
                    bestBidVolume = ReadScaledInt192(blob, 9, "bidVolume");
                    bestAskPrice = ReadScaledInt192(blob, 10, "ask");
                    bestAskVolume = ReadScaledInt192(blob, 11, "askVolume");
                    lastTradePrice = ReadOptionalScaledInt192(blob, 12,
                        "lastTradedPrice");
                    marketStatus = ReadMarketStatus(blob, 13,
                        ChainlinkMarketStatuses.Closed);
                    break;
                case ChainlinkReportSchemas.SmartDataProjected:
                    primaryPrice = ReadScaledInt192(blob, 6, "navPerShare");
                    _ = ReadInt192(blob, 7, "nextNavPerShare");
                    valueTime = ReadUInt64(blob, 8, "navDate")
                        .ParseNanoseconds("navDate");
                    isRipcord = ReadRipcord(blob, 9);
                    break;
                case ChainlinkReportSchemas.BestPrices:
                    bestAskPrice = ReadScaledInt192(blob, 6, "bestAsk");
                    bestBidPrice = ReadScaledInt192(blob, 7, "bestBid");
                    bestAskVolume = ReadUInt64(blob, 8, "askVolume");
                    bestBidVolume = ReadUInt64(blob, 9, "bidVolume");
                    lastTradePrice = ReadOptionalScaledInt192(blob, 10,
                        "lastTradedPrice");
                    primaryPrice = lastTradePrice;
                    break;
                default:
                    throw new NotSupportedException(
                        $"Chainlink report schema {(ushort)feed.Schema} is not supported.");
            }
        }

        ValidateEnvelopeTimestamp(envelope.ObservationsTimestamp,
            envelope.ObservationsTimestampMilliseconds, observationTime,
            "observationsTimestamp");
        if (validFromTime is DateTime valid)
            ValidateEnvelopeTimestamp(envelope.ValidFromTimestamp,
                envelope.ValidFromTimestampMilliseconds, valid,
                "validFromTimestamp");

        return new()
        {
            FeedId = feed.FeedId,
            Schema = feed.Schema,
            ObservationTime = observationTime,
            ValidFromTime = validFromTime,
            ExpiresAt = expiresAt,
            ValueTime = valueTime,
            PrimaryPrice = primaryPrice,
            LastTradePrice = lastTradePrice,
            BestBidPrice = bestBidPrice,
            BestBidVolume = bestBidVolume,
            BestAskPrice = bestAskPrice,
            BestAskVolume = bestAskVolume,
            MarketStatus = marketStatus,
            IsRipcord = isRipcord,
            UpdateKey = Convert.ToHexString(SHA256.HashData(outer))
                .ToLowerInvariant(),
        };
    }

    private static ReadOnlySpan<byte> ExtractReportBlob(ReadOnlySpan<byte> outer)
    {
        if (outer.Length < _outerHeadSize || outer.Length % _wordSize != 0)
            throw new InvalidDataException(
                "Chainlink fullReport has an invalid ABI envelope length.");
        var offset = ReadAbiInt(outer, 3 * _wordSize, "reportBlob offset");
        if (offset < _outerHeadSize || offset % _wordSize != 0 ||
            offset > outer.Length - _wordSize)
            throw new InvalidDataException(
                "Chainlink fullReport has an invalid reportBlob offset.");
        var length = ReadAbiInt(outer, offset, "reportBlob length");
        var start = checked(offset + _wordSize);
        if (length <= 0 || length > _maximumReportLength ||
            start > outer.Length - length)
            throw new InvalidDataException(
                "Chainlink fullReport has an invalid reportBlob length.");
        var paddedLength = checked((length + _wordSize - 1) / _wordSize *
            _wordSize);
        if (start > outer.Length - paddedLength ||
            outer.Slice(start + length, paddedLength - length)
                .ContainsAnyExcept((byte)0))
            throw new InvalidDataException(
                "Chainlink fullReport has invalid reportBlob padding.");
        return outer.Slice(start, length);
    }

    private static byte[] ParseHex(string value, string name)
    {
        value = value?.Trim();
        if (value.IsEmpty() || !value.StartsWith("0x",
            StringComparison.OrdinalIgnoreCase) || value.Length % 2 != 0 ||
            value.Length > _maximumReportLength * 2 + 2)
            throw new InvalidDataException($"Chainlink {name} is not valid hex data.");
        try
        {
            return Convert.FromHexString(value[2..]);
        }
        catch (FormatException error)
        {
            throw new InvalidDataException(
                $"Chainlink {name} is not valid hex data.", error);
        }
    }

    private static int GetSlotCount(ChainlinkReportSchemas schema)
        => schema switch
        {
            ChainlinkReportSchemas.LegacyCrypto => 9,
            ChainlinkReportSchemas.LegacyBasic => 7,
            ChainlinkReportSchemas.CryptoAdvanced => 9,
            ChainlinkReportSchemas.LegacyMarketStatus => 8,
            ChainlinkReportSchemas.Rate => 9,
            ChainlinkReportSchemas.MultiValue => 11,
            ChainlinkReportSchemas.RedemptionRate => 7,
            ChainlinkReportSchemas.RwaStandard => 9,
            ChainlinkReportSchemas.SmartData => 10,
            ChainlinkReportSchemas.TokenizedAsset => 13,
            ChainlinkReportSchemas.RwaAdvanced => 14,
            ChainlinkReportSchemas.SmartDataProjected => 10,
            ChainlinkReportSchemas.BestPrices => 11,
            _ => throw new NotSupportedException(
                $"Chainlink report schema {(ushort)schema} is not supported."),
        };

    private static int ReadAbiInt(ReadOnlySpan<byte> value, int offset,
        string name)
    {
        if (offset < 0 || offset > value.Length - _wordSize)
            throw new InvalidDataException($"Chainlink {name} is missing.");
        var word = value.Slice(offset, _wordSize);
        if (word[..24].ContainsAnyExcept((byte)0))
            throw new InvalidDataException($"Chainlink {name} is too large.");
        var number = BinaryPrimitives.ReadUInt64BigEndian(word[24..]);
        if (number > int.MaxValue)
            throw new InvalidDataException($"Chainlink {name} is too large.");
        return (int)number;
    }

    private static ReadOnlySpan<byte> ReadWord(ReadOnlySpan<byte> value, int slot,
        string name)
    {
        var offset = checked(slot * _wordSize);
        if (offset < 0 || offset > value.Length - _wordSize)
            throw new InvalidDataException($"Chainlink {name} is missing.");
        return value.Slice(offset, _wordSize);
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> value, int slot,
        string name)
    {
        var word = ReadWord(value, slot, name);
        if (word[..24].ContainsAnyExcept((byte)0))
            throw new InvalidDataException($"Chainlink {name} exceeds uint64.");
        return BinaryPrimitives.ReadUInt64BigEndian(word[24..]);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> value, int slot,
        string name)
    {
        var number = ReadUInt64(value, slot, name);
        if (number > uint.MaxValue)
            throw new InvalidDataException($"Chainlink {name} exceeds uint32.");
        return (uint)number;
    }

    private static BigInteger ReadInt192(ReadOnlySpan<byte> value, int slot,
        string name)
    {
        var number = new BigInteger(ReadWord(value, slot, name), false, true);
        if (number < _minimumInt192 || number > _maximumInt192)
            throw new InvalidDataException($"Chainlink {name} exceeds int192.");
        return number;
    }

    private static decimal ReadScaledInt192(ReadOnlySpan<byte> value, int slot,
        string name)
        => Scale(ReadInt192(value, slot, name), name);

    private static decimal? ReadOptionalScaledInt192(ReadOnlySpan<byte> value,
        int slot, string name)
    {
        var number = ReadInt192(value, slot, name);
        return number.IsZero ? null : Scale(number, name);
    }

    private static decimal Scale(BigInteger value, string name)
    {
        var integer = BigInteger.DivRem(value, _valueScale, out var remainder);
        try
        {
            return checked((decimal)integer +
                (decimal)remainder / ChainlinkExtensions.ValueScale);
        }
        catch (OverflowException error)
        {
            throw new InvalidDataException(
                $"Chainlink {name} exceeds the decimal range.", error);
        }
    }

    private static ChainlinkMarketStatuses ReadMarketStatus(
        ReadOnlySpan<byte> value, int slot, ChainlinkMarketStatuses maximum)
    {
        var status = ReadUInt32(value, slot, "marketStatus");
        if (status > (uint)(int)maximum)
            throw new InvalidDataException(
                $"Chainlink marketStatus {status} is invalid for this schema.");
        return (ChainlinkMarketStatuses)(int)status;
    }

    private static bool ReadRipcord(ReadOnlySpan<byte> value, int slot)
    {
        var ripcord = ReadUInt32(value, slot, "ripcord");
        return ripcord switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException(
                $"Chainlink ripcord value {ripcord} is invalid."),
        };
    }

    private static void ValidateEnvelopeTimestamp(long seconds,
        long milliseconds, DateTime actual, string name)
    {
        if (seconds < 0 || milliseconds < 0)
            throw new InvalidDataException($"Chainlink envelope {name} is negative.");
        DateTime? expected = milliseconds > 0
            ? ((ulong)milliseconds).ParseTimestamp(
                ChainlinkTimestampResolutions.Milliseconds, name)
            : seconds > 0
                ? ((ulong)seconds).ParseTimestamp(
                    ChainlinkTimestampResolutions.Seconds, name)
                : null;
        if (expected is DateTime value && value != actual)
            throw new InvalidDataException(
                $"Chainlink envelope {name} differs from the signed report.");
    }
}
