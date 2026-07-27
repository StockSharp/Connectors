namespace StockSharp.EuronextWebServices;

static class EuronextExtensions
{
    private static readonly Regex _isinRegex = new(
        "^[A-Z]{2}[A-Z0-9]{9}[0-9]$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);

    private static readonly TimeZoneInfo _timeZone =
        TimeZoneInfo.FindSystemTimeZoneById(
            "Romance Standard Time");

    public static (string Isin, string Mic) GetEuronextId(
        this SecurityId securityId)
    {
        var isin = securityId.Isin
            .IsEmpty(securityId.SecurityCode)
            ?.Trim()
            .ToUpperInvariant();
        var mic = securityId.BoardCode?
            .Trim()
            .ToUpperInvariant();
        if (isin.IsEmpty() ||
            !_isinRegex.IsMatch(isin))
        {
            throw new InvalidOperationException(
                "Euronext security must contain a valid ISIN.");
        }
        if (mic.IsEmpty() ||
            mic.Length != 4 ||
            mic.Any(character =>
                !char.IsAsciiLetterOrDigit(character)))
        {
            throw new InvalidOperationException(
                "Euronext security must contain a four-character MIC.");
        }

        return (isin, mic);
    }

    public static string ToApiCode(
        this EuronextSessionQualities quality)
        => quality switch
        {
            EuronextSessionQualities.RealTime => "RT",
            EuronextSessionQualities.Delayed => "DT",
            _ => throw new ArgumentOutOfRangeException(
                nameof(quality), quality, null),
        };

    public static SecurityTypes ToSecurityType(
        this EuronextInstrument instrument)
    {
        var type = instrument.Type?.Trim().ToUpperInvariant();
        if (type == "TRACK")
            return SecurityTypes.Etf;
        if (type == "BOND")
            return SecurityTypes.Bond;
        if (type == "WARRT")
            return SecurityTypes.Warrant;

        var cfi = instrument.CfiCode?
            .Trim()
            .ToUpperInvariant();
        if (cfi?.StartsWith("CE") == true)
            return SecurityTypes.Etf;
        if (cfi?.StartsWith('C') == true)
            return SecurityTypes.Fund;
        return SecurityTypes.Stock;
    }

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(
            value,
            ignoreCase: true,
            out var currency)
                ? currency
                : null;

    public static DateTime? ToEuronextDate(this string value)
    {
        if (value.IsEmpty())
            return null;

        if (DateTime.TryParseExact(
            value,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date))
        {
            return DateTime.SpecifyKind(
                date, DateTimeKind.Utc);
        }

        if (DateTime.TryParseExact(
            value,
            "yyyyMMdd-HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var local))
        {
            return TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(
                    local, DateTimeKind.Unspecified),
                _timeZone);
        }

        return null;
    }

    public static SecurityMessage ToSecurityMessage(
        this EuronextInstrument instrument,
        long originalTransactionId)
    {
        var code = instrument.Code
            .ThrowIfEmpty(nameof(instrument.Code))
            .Trim()
            .ToUpperInvariant();
        var mic = instrument.ExchangeCode
            .ThrowIfEmpty(nameof(instrument.ExchangeCode))
            .Trim()
            .ToUpperInvariant();

        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = code,
                BoardCode = mic,
                Isin = instrument.Codification
                    .EqualsIgnoreCase("ISIN")
                        ? code
                        : null,
                Native = instrument.Id
                    .IsEmpty($"{code}|{mic}"),
            },
            Name = instrument.LongName.IsEmpty(code),
            ShortName = instrument.LongName.IsEmpty(code),
            Class = instrument.CfiCode,
            SecurityType = instrument.ToSecurityType(),
            Currency = instrument.Currency.ToCurrency(),
            IssueDate = instrument.IssueDate
                .IsEmpty(instrument.ListingDate)
                .ToEuronextDate(),
            PriceStep = instrument.TickSize,
            VolumeStep = instrument.TradingLot ?? 1,
            Multiplier = 1,
            IssueSize = instrument.NumberOfShares,
            Decimals = instrument.Accuracy,
        };
    }

    public static Level1ChangeMessage ToLevel1(
        this EuronextInstrument instrument,
        long originalTransactionId,
        SecurityId securityId)
    {
        var session = instrument.CurrentSession ??
            throw new InvalidOperationException(
                "Euronext response has no current trading session.");
        var time = session.LastUpdate.ToEuronextDate() ??
            session.DateTime.ToEuronextDate() ??
            DateTime.UtcNow;

        return new Level1ChangeMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = securityId,
            ServerTime = time,
        }
        .TryAdd(Level1Fields.LastTradePrice, session.LastPrice)
        .TryAdd(Level1Fields.LastTradeVolume, session.LastQuantity)
        .TryAdd(Level1Fields.OpenPrice, session.OpenPrice)
        .TryAdd(Level1Fields.HighBidPrice, session.HighLimit)
        .TryAdd(Level1Fields.LowBidPrice, session.LowLimit)
        .TryAdd(Level1Fields.Volume, session.TradedQuantity)
        .TryAdd(Level1Fields.TradesCount, session.TradesCount)
        .TryAdd(Level1Fields.VWAP, session.Vwap)
        .TryAdd(
            Level1Fields.MarketPriceYesterday,
            session.PreviousClose)
        .TryAdd(
            Level1Fields.BestBidPrice,
            instrument.OrderBook?.BestBidPrice)
        .TryAdd(
            Level1Fields.BestBidVolume,
            instrument.OrderBook?.BestBidQuantity)
        .TryAdd(
            Level1Fields.BestAskPrice,
            instrument.OrderBook?.BestAskPrice)
        .TryAdd(
            Level1Fields.BestAskVolume,
            instrument.OrderBook?.BestAskQuantity);
    }

    public static QuoteChangeMessage ToDepth(
        this EuronextInstrument instrument,
        long originalTransactionId,
        SecurityId securityId)
    {
        var book = instrument.OrderBook ??
            throw new InvalidOperationException(
                "Euronext response has no order book.");
        var bids = (book.Bids ?? [])
            .Where(level =>
                level is not null &&
                level.Price > 0 &&
                level.Quantity >= 0)
            .OrderByDescending(level => level.Price)
            .Take(10)
            .Select(level => new QuoteChange(
                level.Price.Value,
                level.Quantity ?? 0,
                level.OrdersCount))
            .ToArray();
        var asks = (book.Asks ?? [])
            .Where(level =>
                level is not null &&
                level.Price > 0 &&
                level.Quantity >= 0)
            .OrderBy(level => level.Price)
            .Take(10)
            .Select(level => new QuoteChange(
                level.Price.Value,
                level.Quantity ?? 0,
                level.OrdersCount))
            .ToArray();

        if (bids.Length == 0 &&
            book.BestBidPrice > 0)
        {
            bids =
            [
                new QuoteChange(
                    book.BestBidPrice.Value,
                    book.BestBidQuantity ?? 0),
            ];
        }
        if (asks.Length == 0 &&
            book.BestAskPrice > 0)
        {
            asks =
            [
                new QuoteChange(
                    book.BestAskPrice.Value,
                    book.BestAskQuantity ?? 0),
            ];
        }

        var time = (book.BestBidTime
            .IsEmpty(book.BestAskTime))
            .ToEuronextDate() ??
            DateTime.UtcNow;
        return new QuoteChangeMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = securityId,
            ServerTime = time,
            Bids = bids,
            Asks = asks,
            State = QuoteChangeStates.SnapshotComplete,
        };
    }
}
