namespace StockSharp.Twse;

static class TwseExtensions
{
    private static readonly TimeSpan _taipeiOffset =
        TimeSpan.FromHours(8);

    public static SecurityMessage ToSecurityMessage(
        this TwseSecurityProfile profile,
        TwseDailyRow price,
        long originalTransactionId)
    {
        var code = (profile?.Code).IsEmpty(price?.Code)
            .ThrowIfEmpty(nameof(profile.Code));
        var localName = (profile?.Name)
            .IsEmpty(profile?.ShortName)
            .IsEmpty(price?.Name)
            .IsEmpty(code);

        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = code.ToTwseSecurityId(),
            Name = (profile?.EnglishName).IsEmpty(localName),
            ShortName = (profile?.ShortName)
                .IsEmpty(price?.Name)
                .IsEmpty(localName),
            Class = profile?.Class,
            SecurityType = profile?.SecurityType ??
                code.InferTwseSecurityType(),
            Currency = CurrencyTypes.TWD,
            IssueDate = profile?.ListingDate.ToTwseDateOrNull(),
            IssueSize = profile?.IssueSize.ToTwseDecimal(),
            VolumeStep = 1,
            Multiplier = 1,
        };
    }

    public static TwseDailyRecord ToRecord(
        this TwseDailyRow row,
        TwseValuationRow valuation)
    {
        var close = row.ClosingPrice.ToTwseDecimal();
        var change = row.Change.ToTwseDecimal();

        return new TwseDailyRecord
        {
            TradingDate = row.Date.ToTwseDate(),
            SecurityId = row.Code.ToTwseSecurityId(),
            OpenPrice = row.OpeningPrice.ToTwseDecimal(),
            HighPrice = row.HighestPrice.ToTwseDecimal(),
            LowPrice = row.LowestPrice.ToTwseDecimal(),
            ClosePrice = close,
            PreviousClosePrice =
                close is not null && change is not null
                    ? close - change
                    : null,
            PriceChange = change,
            Volume = row.TradeVolume.ToTwseDecimal(),
            Turnover = row.TradeValue.ToTwseDecimal(),
            TradesCount = row.Transaction.ToTwseLong(),
            PriceEarnings =
                valuation?.PriceEarnings.ToTwseDecimal(),
            DividendYield =
                valuation?.DividendYield.ToTwseDecimal(),
            PriceBook = valuation?.PriceBook.ToTwseDecimal(),
        };
    }

    public static TwseSecurityProfile[] GetAllProfiles(
        this TwseSnapshot snapshot)
    {
        var profiles = (snapshot.Profiles ?? [])
            .ToDictionary(
                profile => profile.Code,
                StringComparer.OrdinalIgnoreCase);

        foreach (var price in snapshot.Prices ?? [])
        {
            if (price.Code.IsEmpty() ||
                profiles.ContainsKey(price.Code))
            {
                continue;
            }

            profiles.Add(
                price.Code,
                new TwseSecurityProfile
                {
                    Code = price.Code,
                    Name = price.Name,
                    ShortName = price.Name,
                    SecurityType =
                        price.Code.InferTwseSecurityType(),
                });
        }

        return profiles.Values.ToArray();
    }

    public static bool Matches(
        this TwseSecurityProfile profile,
        string value,
        string name)
        => (value.IsEmpty() ||
            profile.Code.ContainsIgnoreCase(value)) &&
            (name.IsEmpty() ||
            profile.Name.ContainsIgnoreCase(name) ||
            profile.ShortName.ContainsIgnoreCase(name) ||
            profile.EnglishName.ContainsIgnoreCase(name));

    public static SecurityTypes InferTwseSecurityType(
        this string code)
        => code?.Trim().StartsWith(
            "00", StringComparison.OrdinalIgnoreCase) == true
                ? SecurityTypes.Etf
                : SecurityTypes.Stock;

    public static SecurityId ToTwseSecurityId(
        this string symbol)
        => new()
        {
            SecurityCode = symbol
                .ThrowIfEmpty(nameof(symbol))
                .Trim(),
            BoardCode = BoardCodes.Tsec,
            Native = symbol.Trim(),
        };

    public static string GetTwseSymbol(
        this SecurityId securityId)
        => (securityId.Native as string)
            .IsEmpty(securityId.SecurityCode)
            ?.Trim();

    public static DateTime ToTwseDate(this string value)
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
                $"Invalid TWSE date '{value}'.");
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
                $"Invalid TWSE date '{value}'.", ex);
        }
    }

    public static DateTime? ToTwseDateOrNull(
        this string value)
        => value.IsEmpty()
            ? null
            : value.ToTwseDate().ToTaipeiTime(TimeSpan.Zero);

    public static DateTime ToTaipeiDate(
        this DateTimeOffset value)
        => value.ToOffset(_taipeiOffset).Date;

    public static DateTime ToTaipeiDate(this DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? value.Date
            : new DateTimeOffset(value)
                .ToOffset(_taipeiOffset)
                .Date;

    public static DateTime ToTaipeiTime(
        this DateTime date,
        TimeSpan time)
        => new DateTimeOffset(
            DateTime.SpecifyKind(
                date.Date.Add(time),
                DateTimeKind.Unspecified),
            _taipeiOffset).UtcDateTime;

    public static decimal? ToTwseDecimal(
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

    public static long? ToTwseLong(this string value)
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
}
