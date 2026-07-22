namespace StockSharp.VeloData;

static class VeloDataExtensions
{
    public static readonly TimeSpan[] TimeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
    ];

    public static string ToWire(this VeloDataMarketTypes value)
        => value switch
        {
            VeloDataMarketTypes.Futures => "futures",
            VeloDataMarketTypes.Options => "options",
            VeloDataMarketTypes.Spot => "spot",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value,
                "Unknown Velo Data market type cannot be serialized."),
        };

    public static string ToWire(this VeloDataColumns value)
        => value switch
        {
            VeloDataColumns.Time => "time",
            VeloDataColumns.Exchange => "exchange",
            VeloDataColumns.Coin => "coin",
            VeloDataColumns.Product => "product",
            VeloDataColumns.Begin => "begin",
            VeloDataColumns.Depth => "depth",
            VeloDataColumns.OpenPrice => "open_price",
            VeloDataColumns.HighPrice => "high_price",
            VeloDataColumns.LowPrice => "low_price",
            VeloDataColumns.ClosePrice => "close_price",
            VeloDataColumns.CoinVolume => "coin_volume",
            VeloDataColumns.TotalTrades => "total_trades",
            VeloDataColumns.CoinOpenInterestClose =>
                "coin_open_interest_close",
            VeloDataColumns.DvolOpen => "dvol_open",
            VeloDataColumns.DvolHigh => "dvol_high",
            VeloDataColumns.DvolLow => "dvol_low",
            VeloDataColumns.DvolClose => "dvol_close",
            VeloDataColumns.IndexPrice => "index_price",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value,
                "Unknown Velo Data column cannot be serialized."),
        };

    public static VeloDataColumns ToVeloColumn(this string value)
        => value?.Trim() switch
        {
            "time" => VeloDataColumns.Time,
            "exchange" => VeloDataColumns.Exchange,
            "coin" => VeloDataColumns.Coin,
            "product" => VeloDataColumns.Product,
            "begin" => VeloDataColumns.Begin,
            "depth" => VeloDataColumns.Depth,
            "open_price" => VeloDataColumns.OpenPrice,
            "high_price" => VeloDataColumns.HighPrice,
            "low_price" => VeloDataColumns.LowPrice,
            "close_price" => VeloDataColumns.ClosePrice,
            "coin_volume" => VeloDataColumns.CoinVolume,
            "total_trades" => VeloDataColumns.TotalTrades,
            "coin_open_interest_close" =>
                VeloDataColumns.CoinOpenInterestClose,
            "dvol_open" => VeloDataColumns.DvolOpen,
            "dvol_high" => VeloDataColumns.DvolHigh,
            "dvol_low" => VeloDataColumns.DvolLow,
            "dvol_close" => VeloDataColumns.DvolClose,
            "index_price" => VeloDataColumns.IndexPrice,
            _ => VeloDataColumns.Unknown,
        };

    public static int ToResolutionMinutes(this TimeSpan value)
    {
        if (value <= TimeSpan.Zero || value.Ticks % TimeSpan.TicksPerMinute != 0 ||
            value.TotalMinutes > int.MaxValue)
            throw new NotSupportedException(
                $"Velo Data requires a positive whole-minute resolution, not {value}.");
        return checked((int)value.TotalMinutes);
    }

    public static string NormalizeVeloIdentifier(string value, string name)
    {
        value = value.ThrowIfEmpty(name).Trim();
        if (value.Length > 512 || value.Any(character =>
            char.IsControl(character) || character is ',' or '?' or '#' or '&' or
            '/' or '\\'))
            throw new ArgumentException(
                "Velo Data identifier contains unsupported characters.", name);
        return value;
    }

    public static DateTime EnsureUtc(this DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static long ToVeloMilliseconds(this DateTime value)
        => checked((value.EnsureUtc() - DateTime.UnixEpoch).Ticks /
            TimeSpan.TicksPerMillisecond);

    public static DateTime FromVeloMilliseconds(this long value)
    {
        try
        {
            return DateTime.UnixEpoch.AddMilliseconds(value);
        }
        catch (ArgumentOutOfRangeException error)
        {
            throw new InvalidDataException(
                "Velo Data returned an invalid Unix timestamp.", error);
        }
    }
}
