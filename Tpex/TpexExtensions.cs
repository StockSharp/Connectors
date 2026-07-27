namespace StockSharp.Tpex;

static class TpexExtensions
{
    public const string BoardCode = "TPEX";

    private static readonly TimeSpan _taipeiOffset =
        TimeSpan.FromHours(8);

    public static bool IncludesMainboard(
        this TpexMarkets market)
        => market switch
        {
            TpexMarkets.Mainboard or TpexMarkets.All => true,
            TpexMarkets.Emerging => false,
            _ => throw new ArgumentOutOfRangeException(
                nameof(market), market, null),
        };

    public static bool IncludesEmerging(
        this TpexMarkets market)
        => market switch
        {
            TpexMarkets.Emerging or TpexMarkets.All => true,
            TpexMarkets.Mainboard => false,
            _ => throw new ArgumentOutOfRangeException(
                nameof(market), market, null),
        };

    public static TpexSecurityProfile[] GetAllProfiles(
        this TpexSnapshot snapshot,
        bool includeListedDerivatives)
    {
        var profiles = new Dictionary<
            string,
            TpexSecurityProfile>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var profile in snapshot.MainboardProfiles ?? [])
        {
            if (!profile.Code.IsEmpty())
                profiles.TryAdd(profile.Code, profile);
        }

        foreach (var price in snapshot.MainboardPrices ?? [])
        {
            if (price.Code.IsEmpty() ||
                profiles.ContainsKey(price.Code) ||
                (!includeListedDerivatives &&
                    !price.Code.StartsWith(
                        "00", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            profiles.Add(
                price.Code,
                new TpexSecurityProfile
                {
                    Code = price.Code,
                    Name = price.Name,
                    ShortName = price.Name,
                    IssueSize = price.IssueSize,
                    SecurityType =
                        price.Code.InferTpexSecurityType(),
                });
        }

        foreach (var profile in snapshot.EmergingProfiles ?? [])
        {
            if (!profile.Code.IsEmpty())
                profiles.TryAdd(profile.Code, profile);
        }

        foreach (var price in snapshot.EmergingPrices ?? [])
        {
            if (price.Code.IsEmpty() ||
                profiles.ContainsKey(price.Code))
            {
                continue;
            }

            profiles.Add(
                price.Code,
                new TpexSecurityProfile
                {
                    Code = price.Code,
                    Name = price.Name,
                    ShortName = price.Name,
                    IsEmerging = true,
                    SecurityType = SecurityTypes.Stock,
                });
        }

        return profiles.Values.ToArray();
    }

    public static SecurityMessage ToSecurityMessage(
        this TpexSecurityProfile profile,
        long originalTransactionId)
    {
        var code = profile.Code
            .ThrowIfEmpty(nameof(profile.Code));
        var localName = profile.Name
            .IsEmpty(profile.ShortName)
            .IsEmpty(code);
        var englishName = profile.EnglishName?.Trim();
        var market = profile.IsEmerging
            ? "Emerging"
            : "Mainboard";

        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = code.ToTpexSecurityId(),
            Name = englishName
                .IsEmpty(localName)
                .Trim(),
            ShortName = profile.ShortName
                .IsEmpty(localName)
                .Trim(),
            Class = profile.IndustryCode.IsEmpty(market),
            SecurityType = profile.SecurityType,
            Currency = CurrencyTypes.TWD,
            IssueDate = profile.ListingDate.ToTpexDateOrNull(),
            IssueSize = profile.IssueSize.ToTpexDecimal(),
            VolumeStep = 1,
            Multiplier = 1,
        };
    }

    public static bool Matches(
        this TpexSecurityProfile profile,
        string value,
        string name)
        => (value.IsEmpty() ||
            profile.Code.ContainsIgnoreCase(value)) &&
            (name.IsEmpty() ||
            profile.Name.ContainsIgnoreCase(name) ||
            profile.ShortName.ContainsIgnoreCase(name) ||
            profile.EnglishName.ContainsIgnoreCase(name));

    public static SecurityTypes InferTpexSecurityType(
        this string code)
    {
        code = code?.Trim();
        if (code.IsEmpty())
            return SecurityTypes.Stock;
        if (code.StartsWith(
            "00", StringComparison.OrdinalIgnoreCase))
        {
            return SecurityTypes.Etf;
        }
        if (code.Length > 4)
            return SecurityTypes.Warrant;

        return SecurityTypes.Stock;
    }

    public static TpexDailyRecord ToRecord(
        this TpexMainboardRow row,
        TpexValuationRow valuation)
    {
        var close = row.Close.ToTpexDecimal();
        var change = row.Change.ToTpexDecimal();
        var date = row.Date.ToTpexDate();

        return new TpexDailyRecord
        {
            TradingDate = date,
            ServerTime = date.ToTaipeiTime(
                new TimeSpan(13, 30, 0)),
            SecurityId = row.Code.ToTpexSecurityId(),
            OpenPrice = row.Open.ToTpexDecimal(),
            HighPrice = row.High.ToTpexDecimal(),
            LowPrice = row.Low.ToTpexDecimal(),
            ClosePrice = close,
            LastTradePrice = close,
            AveragePrice = row.Average.ToTpexDecimal(),
            PreviousPrice =
                close is not null && change is not null
                    ? close - change
                    : null,
            PriceChange = change,
            Volume = row.Volume.ToTpexDecimal(),
            Turnover = row.Turnover.ToTpexDecimal(),
            TradesCount = row.TradesCount.ToTpexLong(),
            BestBidPrice = row.BestBidPrice.ToTpexDecimal(),
            BestAskPrice = row.BestAskPrice.ToTpexDecimal(),
            IssueSize = row.IssueSize.ToTpexDecimal(),
            PriceEarnings =
                valuation?.PriceEarnings.ToTpexDecimal(),
            DividendYield =
                valuation?.DividendYield.ToTpexDecimal(),
            PriceBook = valuation?.PriceBook.ToTpexDecimal(),
        };
    }

    public static TpexDailyRecord ToRecord(
        this TpexEmergingRow row)
    {
        var date = row.Date.ToTpexDate();
        var average = row.Average.ToTpexDecimal();
        var volume = row.Volume.ToTpexDecimal();

        return new TpexDailyRecord
        {
            IsEmerging = true,
            TradingDate = date,
            ServerTime = date.ToTaipeiTime(row.Time),
            SecurityId = row.Code.ToTpexSecurityId(),
            HighPrice = row.High.ToTpexDecimal(),
            LowPrice = row.Low.ToTpexDecimal(),
            LastTradePrice = row.LastTradePrice.ToTpexDecimal(),
            AveragePrice = average,
            PreviousPrice =
                row.PreviousAveragePrice.ToTpexDecimal(),
            Volume = volume,
            Turnover =
                average is not null && volume is not null
                    ? average * volume
                    : null,
            BestBidPrice = row.BestBidPrice.ToTpexDecimal(),
            BestBidVolume = row.BestBidVolume.ToTpexDecimal(),
            BestAskPrice = row.BestAskPrice.ToTpexDecimal(),
            BestAskVolume = row.BestAskVolume.ToTpexDecimal(),
        };
    }

    public static TpexDailyRecord ToRecord(
        this TpexHistoryRow row,
        string symbol)
    {
        var date = row.Date.ToTpexDate();
        if (!row.IsEmerging)
        {
            var close = row.Close.ToTpexDecimal();
            var change = row.Change.ToTpexDecimal();

            return new TpexDailyRecord
            {
                TradingDate = date,
                ServerTime = date.ToTaipeiTime(
                    new TimeSpan(13, 30, 0)),
                SecurityId = symbol.ToTpexSecurityId(),
                OpenPrice = row.Open.ToTpexDecimal(),
                HighPrice = row.High.ToTpexDecimal(),
                LowPrice = row.Low.ToTpexDecimal(),
                ClosePrice = close,
                LastTradePrice = close,
                PreviousPrice =
                    close is not null && change is not null
                        ? close - change
                        : null,
                PriceChange = change,
                Volume = Multiply(
                    row.Volume.ToTpexDecimal(),
                    row.VolumeMultiplier),
                Turnover = Multiply(
                    row.Turnover.ToTpexDecimal(),
                    row.TurnoverMultiplier),
                TradesCount = row.TradesCount.ToTpexLong(),
            };
        }

        var primaryVolume =
            row.Volume.ToTpexDecimal() ?? 0;
        var secondaryVolume =
            row.SecondaryVolume.ToTpexDecimal() ?? 0;
        var primaryTurnover =
            row.Turnover.ToTpexDecimal() ?? 0;
        var secondaryTurnover =
            row.SecondaryTurnover.ToTpexDecimal() ?? 0;
        var volume = primaryVolume + secondaryVolume;
        var turnover = primaryTurnover + secondaryTurnover;
        var primaryAverage = row.Close.ToTpexDecimal();
        var secondaryAverage =
            row.SecondaryAverage.ToTpexDecimal();
        var average = volume > 0
            ? turnover / volume
            : primaryAverage ?? secondaryAverage;

        return new TpexDailyRecord
        {
            IsEmerging = true,
            TradingDate = date,
            ServerTime = date.ToTaipeiTime(
                new TimeSpan(15, 0, 0)),
            SecurityId = symbol.ToTpexSecurityId(),
            HighPrice = Max(
                row.High.ToTpexDecimal(),
                row.SecondaryHigh.ToTpexDecimal()),
            LowPrice = MinPositive(
                row.Low.ToTpexDecimal(),
                row.SecondaryLow.ToTpexDecimal()),
            LastTradePrice = average,
            AveragePrice = average,
            Volume = volume,
            Turnover = turnover,
            TradesCount = SumLong(
                row.TradesCount.ToTpexLong(),
                row.SecondaryTradesCount.ToTpexLong()),
        };
    }

    public static SecurityId ToTpexSecurityId(
        this string symbol)
        => new()
        {
            SecurityCode = symbol
                .ThrowIfEmpty(nameof(symbol))
                .Trim(),
            BoardCode = BoardCode,
            Native = symbol.Trim(),
        };

    public static string GetTpexSymbol(
        this SecurityId securityId)
        => (securityId.Native as string)
            .IsEmpty(securityId.SecurityCode)
            ?.Trim();

    public static DateTime ToTpexDate(this string value)
    {
        var digits = new string(
            (value ?? string.Empty)
                .Where(char.IsDigit)
                .ToArray());
        int year;
        int month;
        int day;

        if (digits.Length == 7)
        {
            year = int.Parse(
                digits[..3], CultureInfo.InvariantCulture) + 1911;
            month = int.Parse(
                digits.Substring(3, 2),
                CultureInfo.InvariantCulture);
            day = int.Parse(
                digits.Substring(5, 2),
                CultureInfo.InvariantCulture);
        }
        else if (digits.Length == 8)
        {
            year = int.Parse(
                digits[..4], CultureInfo.InvariantCulture);
            month = int.Parse(
                digits.Substring(4, 2),
                CultureInfo.InvariantCulture);
            day = int.Parse(
                digits.Substring(6, 2),
                CultureInfo.InvariantCulture);
        }
        else
        {
            throw new FormatException(
                $"Invalid TPEx date '{value}'.");
        }

        try
        {
            return new DateTime(
                year, month, day, 0, 0, 0,
                DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new FormatException(
                $"Invalid TPEx date '{value}'.", ex);
        }
    }

    public static DateTime? ToTpexDateOrNull(
        this string value)
        => value.IsEmpty()
            ? null
            : value.ToTpexDate().ToTaipeiTime(TimeSpan.Zero);

    public static DateTime ToTaipeiDate(
        this DateTimeOffset value)
        => value.ToOffset(_taipeiOffset).Date;

    public static DateTime ToTaipeiDate(this DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? value.Date
            : new DateTimeOffset(value)
                .ToOffset(_taipeiOffset)
                .Date;

    public static DateTime TaipeiToday()
        => DateTimeOffset.UtcNow.ToOffset(_taipeiOffset).Date;

    public static DateTime ToTaipeiTime(
        this DateTime date,
        TimeSpan time)
        => new DateTimeOffset(
            DateTime.SpecifyKind(
                date.Date.Add(time),
                DateTimeKind.Unspecified),
            _taipeiOffset).UtcDateTime;

    public static DateTime ToTaipeiTime(
        this DateTime date,
        string time)
    {
        var digits = new string(
            (time ?? string.Empty)
                .Where(char.IsDigit)
                .ToArray());
        if (digits.Length != 6 ||
            !int.TryParse(
                digits[..2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var hour) ||
            !int.TryParse(
                digits.Substring(2, 2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var minute) ||
            !int.TryParse(
                digits.Substring(4, 2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var second) ||
            hour > 23 || minute > 59 || second > 59)
        {
            return date.ToTaipeiTime(
                new TimeSpan(15, 0, 0));
        }

        return date.ToTaipeiTime(
            new TimeSpan(hour, minute, second));
    }

    public static decimal? ToTpexDecimal(
        this string value)
    {
        value = value?.Trim();
        if (value.IsEmpty() ||
            value is "-" or "--" or "---" or "N/A")
        {
            return null;
        }

        var negative =
            value.StartsWith('(') && value.EndsWith(')');
        value = value
            .Trim('(', ')')
            .Replace(",", string.Empty)
            .Replace("%", string.Empty);

        if (!decimal.TryParse(
            value,
            NumberStyles.Number |
            NumberStyles.AllowLeadingSign |
            NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out var result))
        {
            return null;
        }

        return negative ? -result : result;
    }

    public static long? ToTpexLong(this string value)
    {
        value = value?.Trim().Replace(",", string.Empty);
        return long.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : null;
    }

    private static decimal? Max(
        decimal? first,
        decimal? second)
        => first is null
            ? second
            : second is null
                ? first
                : Math.Max(first.Value, second.Value);

    private static decimal? MinPositive(
        decimal? first,
        decimal? second)
    {
        if (first is null or <= 0)
            return second is > 0 ? second : null;
        if (second is null or <= 0)
            return first;
        return Math.Min(first.Value, second.Value);
    }

    private static long? SumLong(
        long? first,
        long? second)
        => first is null && second is null
            ? null
            : (first ?? 0) + (second ?? 0);

    private static decimal? Multiply(
        decimal? value,
        int multiplier)
        => value is null
            ? null
            : value * multiplier;
}
