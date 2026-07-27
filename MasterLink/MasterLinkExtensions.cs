namespace StockSharp.MasterLink;

internal static class MasterLinkExtensions
{
    private static readonly TimeZoneInfo _taipeiTimeZone =
        CreateTaipeiTimeZone();

    public static string ToNativeKey(this MasterLinkSecurity security)
        => string.Join(
            '|',
            security.Exchange.IsEmpty("TWSE"),
            security.Market.IsEmpty("TSE"),
            security.Symbol,
            security.IsOddLot ? "1" : "0");

    public static MasterLinkSecurity ParseMasterLinkSecurity(
        this SecurityId securityId)
    {
        if (securityId.Native is string native && !native.IsEmpty())
        {
            var parts = native.Split('|');
            if (parts.Length == 4 && !parts[2].IsEmpty())
            {
                return new()
                {
                    Exchange = parts[0],
                    Market = parts[1],
                    Symbol = parts[2],
                    IsOddLot = parts[3] == "1",
                    Type = "EQUITY",
                    SecurityType = "EQUITY",
                    TradingCurrency = "TWD",
                };
            }
        }

        var board = securityId.BoardCode?.ToUpperInvariant();
        var isOdd = board is "TWSEODD" or "TPEXODD";
        var isEmerging = board == "TWEMERGING";
        var isTpex = board is "TPEX" or "TPEXODD";
        return new()
        {
            Exchange = isTpex || isEmerging ? "TPEx" : "TWSE",
            Market = isEmerging ? "ESB" : isTpex ? "OTC" : "TSE",
            Symbol = securityId.SecurityCode.ThrowIfEmpty(
                nameof(securityId.SecurityCode)),
            IsOddLot = isOdd,
            Type = "EQUITY",
            SecurityType = "EQUITY",
            TradingCurrency = "TWD",
            BoardLot = isOdd ? 1 : 1000,
        };
    }

    public static SecurityId ToSecurityId(
        this MasterLinkSecurity security)
        => new()
        {
            SecurityCode = security.Symbol,
            BoardCode = security.ToBoardCode(),
            Native = security.ToNativeKey(),
        };

    public static string ToBoardCode(this MasterLinkSecurity security)
    {
        var market = security.Market?.ToUpperInvariant();
        var board = market is "ESB" or "PSB" ||
            security.Exchange.EqualsIgnoreCase("EMG")
                ? "TWEMERGING"
                : security.Exchange.EqualsIgnoreCase("TPEX") ||
                    security.Exchange.EqualsIgnoreCase("TPEx") ||
                    market == "OTC"
                        ? "TPEX"
                        : "TWSE";
        return security.IsOddLot && board != "TWEMERGING"
            ? $"{board}ODD"
            : board;
    }

    public static string ToBoardCode(
        string market,
        string marketType = null)
    {
        var board = market?.ToUpperInvariant() switch
        {
            "O" or "OTC" => "TPEX",
            "R" or "ESB" or "PSB" => "TWEMERGING",
            _ => "TWSE",
        };
        return marketType is not null &&
            (marketType.EqualsIgnoreCase("Odd") ||
                marketType.EqualsIgnoreCase("IntradayOdd")) &&
            board != "TWEMERGING"
                ? $"{board}ODD"
                : board;
    }

    public static SecurityTypes ToSecurityType(
        this MasterLinkSecurity security)
        => security.SecurityType.IsEmpty(security.Type)
            .ToUpperInvariant() switch
        {
            "INDEX" => SecurityTypes.Index,
            "WARRANT" => SecurityTypes.Warrant,
            "ETF" => SecurityTypes.Etf,
            _ => SecurityTypes.Stock,
        };

    public static SecurityMessage ToSecurityMessage(
        this MasterLinkSecurity security,
        long transactionId)
        => new()
        {
            OriginalTransactionId = transactionId,
            SecurityId = security.ToSecurityId(),
            SecurityType = security.ToSecurityType(),
            Name = security.NameEn.IsEmpty(security.Name),
            ShortName = security.Symbol,
            Class = security.Industry.IsEmpty(security.Market),
            Currency = CurrencyTypes.TWD,
            PriceStep = GetPriceStep(
                security.ReferencePrice ??
                    security.PreviousClose),
            VolumeStep = 1,
            Multiplier = security.BoardLot is > 0
                ? security.BoardLot
                : security.IsOddLot ? 1 : 1000,
            ExpiryDate = ParseDate(security.MaturityDate),
        };

    public static bool IsOddLotBoard(this SecurityId securityId)
        => securityId.BoardCode?.EndsWith(
            "ODD", StringComparison.OrdinalIgnoreCase) == true;

    public static string ToNativeTimeFrame(this TimeSpan timeFrame)
        => timeFrame.TotalMinutes switch
        {
            1 => "1",
            3 => "3",
            5 => "5",
            10 => "10",
            15 => "15",
            30 => "30",
            60 => "60",
            1440 => "D",
            10080 => "W",
            43200 => "M",
            _ => throw new ArgumentOutOfRangeException(
                nameof(timeFrame),
                timeFrame,
                "Nova API supports 1, 3, 5, 10, 15, 30, 60 minute, daily, weekly, and monthly candles."),
        };

    public static DateTime? ToMasterLinkTime(this long? microseconds)
    {
        if (microseconds is not > 0)
            return null;
        try
        {
            return DateTime.UnixEpoch.AddTicks(
                checked(microseconds.Value * 10));
        }
        catch (Exception error) when (
            error is ArgumentOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    public static DateTime? ParseMasterLinkTime(this string value)
    {
        if (value.IsEmpty())
            return null;
        if (DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces |
                DateTimeStyles.AssumeUniversal,
            out var offset))
        {
            return offset.UtcDateTime;
        }
        if (DateTime.TryParseExact(
            value,
            [
                "yyyyMMdd",
                "yyyy-MM-dd",
                "yyyy/MM/dd",
                "yyyyMMddHHmmss",
                "yyyyMMddHHmmssfff",
                "yyyyMMddHHmmssffff",
            ],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var local))
        {
            return TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(
                    local, DateTimeKind.Unspecified),
                _taipeiTimeZone);
        }
        return null;
    }

    public static DateTime? ParseMasterLinkTradeTime(
        string date,
        string time)
    {
        if (date.IsEmpty())
            return null;
        var digits = new string(
            (time ?? string.Empty).Where(char.IsDigit).ToArray());
        digits = digits.PadRight(9, '0');
        if (digits.Length > 9)
            digits = digits[..9];
        var value = $"{date.Trim()} {digits}";
        if (!DateTime.TryParseExact(
            value,
            [
                "yyyyMMdd HHmmssfff",
                "yyyy-MM-dd HHmmssfff",
                "yyyy/MM/dd HHmmssfff",
            ],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var local))
        {
            return date.ParseMasterLinkTime();
        }
        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            _taipeiTimeZone);
    }

    public static decimal? ToNullableDecimal(this string value)
    {
        if (value.IsEmpty())
            return null;
        var normalized = value.Trim().TrimEnd('%').Replace(
            ",", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(
            normalized,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : null;
    }

    public static string ToNative(
        this MasterLinkMarketTypes value,
        SecurityId securityId)
        => value switch
        {
            MasterLinkMarketTypes.Common => "Common",
            MasterLinkMarketTypes.Fixing => "Fixing",
            MasterLinkMarketTypes.IntradayOdd => "IntradayOdd",
            MasterLinkMarketTypes.Odd => "Odd",
            MasterLinkMarketTypes.Emg => "Emg",
            _ when securityId.BoardCode.EqualsIgnoreCase(
                "TWEMERGING") => "Emg",
            _ when securityId.IsOddLotBoard() => "IntradayOdd",
            _ => "Common",
        };

    public static string ToNative(
        this MasterLinkPriceTypes value,
        OrderTypes orderType)
        => value switch
        {
            MasterLinkPriceTypes.Limit => "Limit",
            MasterLinkPriceTypes.Market => "Market",
            MasterLinkPriceTypes.LimitUp => "LimitUp",
            MasterLinkPriceTypes.LimitDown => "LimitDown",
            MasterLinkPriceTypes.Reference => "Reference",
            _ => orderType == OrderTypes.Market
                ? "Market"
                : "Limit",
        };

    public static string ToNative(
        this MasterLinkOrderTypes value)
        => value.ToString();

    public static string ToNative(this TimeInForce value)
        => value switch
        {
            TimeInForce.CancelBalance => "IOC",
            TimeInForce.MatchOrCancel => "FOK",
            _ => "ROD",
        };

    public static Sides ToSide(this string value)
        => value.EqualsIgnoreCase("Sell") ||
            value.EqualsIgnoreCase("S")
                ? Sides.Sell
                : Sides.Buy;

    public static OrderTypes ToOrderType(this string value)
        => value.EqualsIgnoreCase("Market")
            ? OrderTypes.Market
            : OrderTypes.Limit;

    public static TimeInForce ToTimeInForce(this string value)
        => value?.ToUpperInvariant() switch
        {
            "IOC" => TimeInForce.CancelBalance,
            "FOK" => TimeInForce.MatchOrCancel,
            _ => TimeInForce.PutInQueue,
        };

    public static OrderStates ToOrderState(
        this MasterLinkOrderRecord order)
    {
        if (!order.ErrCode.IsEmpty() &&
            !order.ErrCode.EqualsIgnoreCase("000000"))
        {
            return OrderStates.Failed;
        }
        var balance =
            order.OrgQty - order.FilledQty - order.CelQty;
        if (balance <= 0)
            return OrderStates.Done;
        return order.CanCancel
            ? OrderStates.Active
            : OrderStates.Pending;
    }

    public static MasterLinkOrderCondition ToCondition(
        this MasterLinkOrderRecord order)
    {
        Enum.TryParse<MasterLinkMarketTypes>(
            order.MarketType, true, out var marketType);
        Enum.TryParse<MasterLinkPriceTypes>(
            order.PriceType, true, out var priceType);
        Enum.TryParse<MasterLinkOrderTypes>(
            order.OrderType, true, out var orderType);
        return new()
        {
            MarketType = marketType == default
                ? MasterLinkMarketTypes.Common
                : marketType,
            PriceType = priceType == default &&
                !order.PriceType.EqualsIgnoreCase("Auto")
                    ? MasterLinkPriceTypes.Limit
                    : priceType,
            OrderType = orderType,
        };
    }

    public static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private static decimal GetPriceStep(decimal? price)
        => price switch
        {
            null or <= 0 => 0.01m,
            < 10 => 0.01m,
            < 50 => 0.05m,
            < 100 => 0.1m,
            < 500 => 0.5m,
            < 1000 => 1m,
            _ => 5m,
        };

    private static DateTime? ParseDate(string value)
        => value.ParseMasterLinkTime()?.Date;

    private static TimeZoneInfo CreateTaipeiTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                "Taipei Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(
                    "Asia/Taipei");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.CreateCustomTimeZone(
                    "Taipei",
                    TimeSpan.FromHours(8),
                    "Taipei",
                    "Taipei");
            }
        }
    }
}
