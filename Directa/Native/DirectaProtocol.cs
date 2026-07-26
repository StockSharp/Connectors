namespace StockSharp.Directa.Native;

static class DirectaProtocol
{
    public static readonly TimeSpan[] TimeFrames =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(3),
        TimeSpan.FromMinutes(4),
        TimeSpan.FromDays(1),
    ];

    public static string[] Split(string line)
        => line?.Split(';')
            .Select(value => value.Trim())
            .ToArray() ?? [];

    public static string FormatDecimal(decimal value)
        => value.ToString(
            "0.############################",
            CultureInfo.InvariantCulture);

    public static string NormalizeTicker(string ticker)
        => ValidateToken(ticker, nameof(ticker));

    public static decimal? ToDecimal(string value)
    {
        if (value.IsEmpty())
            return null;

        value = value.Trim();
        var comma = value.IndexOf(',');
        if (comma > 0 && value.Contains('.'))
            value = value[..comma];
        else
            value = value.Replace(',', '.');

        return decimal.TryParse(
            value,
            NumberStyles.Number |
                NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var result)
                ? result : null;
    }

    public static decimal? ToQuantity(string value)
    {
        if (value.IsEmpty())
            return null;

        value = value.Trim();
        var length = 0;
        while (length < value.Length)
        {
            var ch = value[length];
            if (char.IsDigit(ch) ||
                ch is '+' or '-' or '.' or ',')
            {
                length++;
                continue;
            }
            break;
        }

        return length == 0
            ? null : ToDecimal(value[..length]);
    }

    public static string CreateOrderCommand(
        string orderId, string ticker, Sides side,
        OrderTypes orderType, decimal quantity,
        decimal price, decimal? triggerPrice)
    {
        orderId = ValidateToken(
            orderId, nameof(orderId));
        ticker = ValidateToken(ticker, nameof(ticker));
        var prefix = side == Sides.Buy ? "ACQ" : "VEN";
        string command;
        switch (orderType)
        {
            case OrderTypes.Market:
                command = prefix + "MARKET";
                return $"{command} {orderId},{ticker}," +
                    FormatDecimal(quantity);
            case OrderTypes.Limit:
                command = side == Sides.Buy
                    ? "ACQAZ" : "VENAZ";
                return $"{command} {orderId},{ticker}," +
                    $"{FormatDecimal(quantity)}," +
                    FormatDecimal(price);
            case OrderTypes.Conditional:
                if (triggerPrice is not > 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(triggerPrice), triggerPrice,
                        "A positive Directa trigger price is required.");
                }
                if (price > 0)
                {
                    command = prefix + "STOPLIMIT";
                    return $"{command} {orderId},{ticker}," +
                        $"{FormatDecimal(quantity)}," +
                        $"{FormatDecimal(price)}," +
                        FormatDecimal(triggerPrice.Value);
                }
                command = prefix + "STOP";
                return $"{command} {orderId},{ticker}," +
                    $"{FormatDecimal(quantity)}," +
                    FormatDecimal(triggerPrice.Value);
            default:
                throw new NotSupportedException(
                    $"Directa does not support {orderType} orders.");
        }
    }

    public static string CreateReplaceCommand(
        string orderId, decimal price,
        decimal? triggerPrice)
    {
        orderId = ValidateToken(
            orderId, nameof(orderId));
        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(price), price,
                "A positive replacement price is required.");
        }

        var command = $"MODORD {orderId}," +
            FormatDecimal(price);
        if (triggerPrice is > 0)
        {
            command += "," +
                FormatDecimal(triggerPrice.Value);
        }
        return command;
    }

    public static DirectaRegistry ParseRegistry(
        string line, TimeZoneInfo timeZone)
    {
        var parts = Require(line, "ANAG", 8);
        return new()
        {
            Ticker = parts[1],
            Time = ParseToday(parts[2], timeZone),
            Isin = parts[3],
            Name = parts[4],
            ReferencePrice = ToDecimal(parts[5]),
            OpenPrice = ToDecimal(parts[6]),
            Float = ToDecimal(parts[7]),
        };
    }

    public static DirectaPrice ParsePrice(
        string line, TimeZoneInfo timeZone)
    {
        var parts = Split(line);
        if (parts.Length >= 4 &&
            parts[0].EqualsIgnoreCase("PRICE_AUCT"))
        {
            return new()
            {
                Ticker = parts[1],
                Time = ParseToday(parts[2], timeZone),
                Price = RequiredDecimal(parts[3], "price"),
                IsAuction = true,
            };
        }

        parts = Require(line, "PRICE", 9);
        return new()
        {
            Ticker = parts[1],
            Time = ParseToday(parts[2], timeZone),
            Price = RequiredDecimal(parts[3], "price"),
            Volume = ToDecimal(parts[4]),
            TradeId = ToLong(parts[5]),
            ExchangeTradeId = ToLong(parts[6]),
            LowPrice = ToDecimal(parts[7]),
            HighPrice = ToDecimal(parts[8]),
        };
    }

    public static DirectaBidAsk ParseBidAsk(
        string line, TimeZoneInfo timeZone)
    {
        var parts = Require(line, "BIDASK", 9);
        return new()
        {
            Ticker = parts[1],
            Time = ParseToday(parts[2], timeZone),
            BidVolume = ToDecimal(parts[3]),
            BidOrders = ToInt(parts[4]),
            BidPrice = ToDecimal(parts[5]),
            AskVolume = ToDecimal(parts[6]),
            AskOrders = ToInt(parts[7]),
            AskPrice = ToDecimal(parts[8]),
        };
    }

    public static DirectaBookSlice ParseBook(
        string line, TimeZoneInfo timeZone)
    {
        var parts = Split(line);
        if (parts.Length < 33 ||
            !parts[0].StartsWith(
                "BOOK_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Invalid Directa order-book record.");
        }

        var endLevel = int.TryParse(
            parts[0].AsSpan(5),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsedLevel)
                ? parsedLevel : 0;
        if (endLevel is not (5 or 10 or 15 or 20))
        {
            throw new InvalidDataException(
                $"Unsupported Directa book slice '{parts[0]}'.");
        }

        var firstLevel = endLevel - 4;
        var levels = new List<DirectaBookLevel>(10);
        for (var i = 0; i < 10; i++)
        {
            var offset = 3 + i * 3;
            var volume = ToDecimal(parts[offset]);
            var orders = ToInt(parts[offset + 1]);
            var price = ToDecimal(parts[offset + 2]);
            if (price is not > 0 || volume is not >= 0)
                continue;
            levels.Add(new()
            {
                Level = firstLevel + i % 5,
                Side = i < 5 ? Sides.Buy : Sides.Sell,
                Price = price.Value,
                Volume = volume.Value,
                Orders = orders,
            });
        }

        return new()
        {
            Ticker = parts[1],
            Time = ParseToday(parts[2], timeZone),
            FirstLevel = firstLevel,
            Levels = levels.ToArray(),
        };
    }

    public static DirectaAccount ParseAccount(
        string line, TimeZoneInfo timeZone)
    {
        var parts = Require(line, "INFOACCOUNT", 6);
        return new()
        {
            Time = ParseToday(parts[1], timeZone),
            Account = parts[2],
            Liquidity = ToDecimal(parts[3]),
            Gain = ToDecimal(parts[4]),
            OpenProfitLoss = ToDecimal(parts[5]),
        };
    }

    public static DirectaAvailability ParseAvailability(
        string line, TimeZoneInfo timeZone)
    {
        var parts = Require(line, "AVAILABILITY", 7);
        return new()
        {
            Time = ParseToday(parts[1], timeZone),
            Stocks = ToDecimal(parts[2]),
            StocksWithLeverage = ToDecimal(parts[3]),
            Derivatives = ToDecimal(parts[4]),
            DerivativesWithLeverage =
                ToDecimal(parts[5]),
            TotalLiquidity = ToDecimal(parts[6]),
        };
    }

    public static DirectaPosition ParsePosition(
        string line, TimeZoneInfo timeZone)
    {
        var parts = Require(line, "STOCK", 8);
        return new()
        {
            Ticker = parts[1],
            Time = ParseToday(parts[2], timeZone),
            Quantity = ToQuantity(parts[3]),
            DirectaQuantity = ToQuantity(parts[4]),
            TradingQuantity = ToQuantity(parts[5]),
            AveragePrice = ToDecimal(parts[6]),
            Gain = ToDecimal(parts[7]),
        };
    }

    public static DirectaOrder ParseOrder(
        string line, TimeZoneInfo timeZone)
    {
        var parts = Require(line, "ORDER", 9);
        return new()
        {
            Ticker = parts[1],
            Time = ParseToday(parts[2], timeZone),
            OrderId = parts[3],
            Operation = parts[4],
            LimitPrice = ToDecimal(parts[5]),
            TriggerPrice = ToDecimal(parts[6]),
            Quantity = ToQuantity(parts[7]),
            Status = ToInt(parts[8]) ?? 0,
            AveragePrice = parts.Length > 9
                ? ToDecimal(parts[9]) : null,
            ExecutionPrice = parts.Length > 10
                ? ToDecimal(parts[10]) : null,
            MarketQuantity = parts.Length > 11
                ? ToQuantity(parts[11]) : null,
            DirectaId = parts.Length > 12
                ? parts[12] : null,
        };
    }

    public static DirectaTradeResult ParseTradeResult(
        string line)
    {
        var parts = Split(line);
        if (parts.Length < 7 ||
            parts[0] is not (
                "TRADOK" or "TRADERR" or
                "TRADCONFIRM"))
        {
            throw new InvalidDataException(
                "Invalid Directa trading result record.");
        }

        var hasPriceExecution = parts.Length >= 12;
        var sourceCommand = parts.LastOrDefault();
        if (!LooksLikeCommand(sourceCommand))
            sourceCommand = null;

        return new()
        {
            MessageType = parts[0],
            Ticker = parts[1],
            OrderId = parts[2],
            Code = ToInt(parts[3]) ?? 0,
            Operation = parts[4],
            RequestedQuantity = ToQuantity(parts[5]),
            EntryPrice = ToDecimal(parts[6]),
            Error = parts.Length > 7 &&
                parts[0] != "TRADOK"
                    ? parts[7] : null,
            ExecutionPrice = hasPriceExecution
                ? ToDecimal(parts[8]) : null,
            ExecutedQuantity = hasPriceExecution
                ? ToQuantity(parts[9]) : null,
            RemainingQuantity = hasPriceExecution
                ? ToQuantity(parts[10]) : null,
            DirectaId = hasPriceExecution
                ? parts[11] : null,
            SourceCommand = sourceCommand,
        };
    }

    public static DirectaHistoricalTick ParseTick(
        string line, TimeZoneInfo timeZone)
    {
        var parts = Require(line, "TBT", 6);
        return new()
        {
            Ticker = parts[1],
            Time = ParseDateTime(
                parts[2], parts[3], timeZone),
            Price = RequiredDecimal(parts[4], "price"),
            ProgressiveVolume = ToLong(parts[5]) ?? 0,
        };
    }

    public static DirectaCandle ParseCandle(
        string line, TimeZoneInfo timeZone)
    {
        var parts = Require(line, "CANDLE", 9);
        return new()
        {
            Ticker = parts[1],
            Time = ParseDateTime(
                parts[2], parts[3], timeZone),
            Open = RequiredDecimal(parts[4], "open"),
            Low = RequiredDecimal(parts[5], "low"),
            High = RequiredDecimal(parts[6], "high"),
            Close = RequiredDecimal(parts[7], "close"),
            Volume =
                RequiredDecimal(parts[8], "volume"),
        };
    }

    public static SecurityTypes InferSecurityType(
        string table)
    {
        table ??= string.Empty;
        if (table.ContainsIgnoreCase("OPTION"))
            return SecurityTypes.Option;
        if (table.ContainsIgnoreCase("FIB") ||
            table.ContainsIgnoreCase("FUT") ||
            table.ContainsIgnoreCase("EUREX") ||
            table.ContainsIgnoreCase("CME") ||
            table.ContainsIgnoreCase("LIFFE"))
            return SecurityTypes.Future;
        if (table.ContainsIgnoreCase("MOT") ||
            table.ContainsIgnoreCase("BOND"))
            return SecurityTypes.Bond;
        if (table.ContainsIgnoreCase("FOREX") ||
            table.ContainsIgnoreCase("FX"))
            return SecurityTypes.Currency;
        if (table.ContainsIgnoreCase("INDEX"))
            return SecurityTypes.Index;
        return SecurityTypes.Stock;
    }

    public static Sides ToSide(string operation)
        => operation?.StartsWith(
            "ACQ", StringComparison.OrdinalIgnoreCase) == true
                ? Sides.Buy : Sides.Sell;

    public static OrderTypes ToOrderType(
        string operation)
    {
        if (operation.ContainsIgnoreCase("STOP"))
            return OrderTypes.Conditional;
        if (operation.ContainsIgnoreCase("MARKET"))
            return OrderTypes.Market;
        return OrderTypes.Limit;
    }

    public static OrderStates ToOrderState(int status)
        => status switch
        {
            2000 or 2002 or 2006 => OrderStates.Active,
            2005 => OrderStates.Pending,
            2003 or 2004 => OrderStates.Done,
            2001 => OrderStates.Failed,
            _ => OrderStates.Pending,
        };

    public static string GetError(int code)
        => code switch
        {
            1000 => "Maximum datafeed subscription count reached.",
            1001 => "Security is already subscribed.",
            1002 => "The subscription list is empty.",
            1003 => "Unknown Darwin command.",
            1004 => "Darwin did not execute the command.",
            1005 => "Security is not subscribed.",
            1007 => "Unknown security.",
            1008 => "Requested market data is unavailable.",
            1009 => "Incomplete trading command.",
            1010 => "Invalid trading command.",
            1011 => "Trading service is unavailable.",
            1012 => "Order request was rejected.",
            1013 => "Invalid historical request parameters.",
            1015 => "Historical intraday range is invalid.",
            1016 => "Historical day count or range is invalid.",
            1018 => "The stock list is empty.",
            1019 => "The order list is empty.",
            1020 => "Duplicate order identifier.",
            1021 => "Order state is invalid for this operation.",
            1024 => "Directa trading push is disconnected.",
            1030 => "The security market is unavailable.",
            1031 => "The Directa trading contact is inactive.",
            _ => $"Darwin error {code}.",
        };

    public static SecurityId ToSecurityId(
        string ticker, string isin = null)
        => ticker.IsEmpty()
            ? default
            : new()
            {
                SecurityCode = ticker.Trim(),
                BoardCode = "DIRECTA",
                Native = ticker.Trim(),
                Isin = isin,
            };

    public static string ToTicker(
        this SecurityId securityId)
        => (securityId.Native as string)
            .IsEmpty(securityId.SecurityCode)
            .ThrowIfEmpty(nameof(securityId));

    public static TimeZoneInfo ResolveTimeZone(
        string id)
    {
        id = id.ThrowIfEmpty(nameof(id));
        var candidates = new List<string> { id };
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(
            id, out var windows))
            candidates.Add(windows);
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(
            id, out var iana))
            candidates.Add(iana);
        candidates.Add("Europe/Rome");
        candidates.Add("W. Europe Standard Time");

        foreach (var candidate in
            candidates.Distinct(
                StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(
                    candidate);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }
        throw new TimeZoneNotFoundException(
            $"Cannot resolve Directa time zone '{id}'.");
    }

    public static string ToHistoryTimestamp(
        DateTime value, TimeZoneInfo timeZone)
        => TimeZoneInfo.ConvertTimeFromUtc(
                value.ToUniversalTime(), timeZone)
            .ToString(
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture);

    public static int ToCandleSeconds(
        this TimeSpan timeFrame)
    {
        if (!TimeFrames.Contains(timeFrame))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeFrame), timeFrame,
                "Directa does not support this candle time frame.");
        }
        return checked((int)timeFrame.TotalSeconds);
    }

    private static string[] Require(
        string line, string type, int count)
    {
        var parts = Split(line);
        if (parts.Length < count ||
            !parts[0].EqualsIgnoreCase(type))
        {
            throw new InvalidDataException(
                $"Invalid Directa {type} record.");
        }
        return parts;
    }

    private static DateTime ParseToday(
        string value, TimeZoneInfo timeZone)
    {
        if (!TimeSpan.TryParseExact(
            value,
            ["hh\\:mm\\:ss", "h\\:mm\\:ss"],
            CultureInfo.InvariantCulture,
            out var time))
        {
            throw new InvalidDataException(
                $"Invalid Directa time '{value}'.");
        }

        var now = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, timeZone);
        var local = now.Date + time;
        if (local > now.AddHours(12))
            local = local.AddDays(-1);
        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(
                local, DateTimeKind.Unspecified),
            timeZone);
    }

    private static DateTime ParseDateTime(
        string date, string time,
        TimeZoneInfo timeZone)
    {
        if (!DateTime.TryParseExact(
            $"{date} {time}",
            ["yyyyMMdd HH:mm:ss", "yyyyMMdd H:mm:ss"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var local))
        {
            throw new InvalidDataException(
                $"Invalid Directa timestamp '{date} {time}'.");
        }
        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(
                local, DateTimeKind.Unspecified),
            timeZone);
    }

    private static decimal RequiredDecimal(
        string value, string name)
        => ToDecimal(value) ??
            throw new InvalidDataException(
                $"Invalid Directa {name} '{value}'.");

    private static int? ToInt(string value)
        => int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result)
                ? result : null;

    private static long? ToLong(string value)
        => long.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result)
                ? result : null;

    private static string ValidateToken(
        string value, string name)
    {
        value = value.ThrowIfEmpty(name).Trim();
        if (value.IndexOfAny(
            [',', ';', '\r', '\n', ' ']) >= 0)
        {
            throw new ArgumentException(
                $"Directa {name} contains a protocol delimiter.",
                name);
        }
        return value;
    }

    private static bool LooksLikeCommand(string value)
        => value?.StartsWith(
            "ACQ", StringComparison.OrdinalIgnoreCase) == true ||
            value?.StartsWith(
                "VEN", StringComparison.OrdinalIgnoreCase) == true ||
            value?.StartsWith(
                "REV", StringComparison.OrdinalIgnoreCase) == true ||
            value?.StartsWith(
                "MOD", StringComparison.OrdinalIgnoreCase) == true ||
            value?.StartsWith(
                "CONF", StringComparison.OrdinalIgnoreCase) == true;
}
