namespace StockSharp.Uniswap.Native;

static class UniswapExtensions
{
    public const string NativeTokenAddress =
        "0x0000000000000000000000000000000000000000";

    public static readonly TimeSpan[] TimeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4),
        TimeSpan.FromDays(1),
    ];

    public static string NormalizeAddress(this string address)
    {
        address = address.ThrowIfEmpty(nameof(address)).Trim();
        if (address.Length != 42 || !address.StartsWith("0x",
            StringComparison.OrdinalIgnoreCase) || address.Skip(2).Any(
            static ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException(
                $"Invalid EVM address '{address}'.", nameof(address));
        return "0x" + address[2..].ToLowerInvariant();
    }

    public static bool IsNativeToken(this string address)
        => !address.IsEmpty() && address.NormalizeAddress()
            .EqualsIgnoreCase(NativeTokenAddress);

    public static string ToBoardCode(this UniswapChains chain)
        => BoardCodes.Uniswap;

    public static SecurityId ToStockSharp(this UniswapMarket market)
        => new()
        {
            SecurityCode = market.SecurityCode,
            BoardCode = BoardCodes.Uniswap,
        };

    public static decimal? ToDecimalInvariant(this string value)
        => decimal.TryParse(value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    public static BigInteger ParseInteger(this string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hex = value[2..];
            if (hex.IsEmpty())
                return BigInteger.Zero;
            return BigInteger.Parse("0" + hex,
                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }
        return BigInteger.Parse(value, NumberStyles.Integer,
            CultureInfo.InvariantCulture);
    }

    public static string ToRpcHex(this BigInteger value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value,
                "JSON-RPC quantities cannot be negative.");
        return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
    }

    public static BigInteger ToBaseUnits(this decimal value, int decimals)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        if (decimals is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(decimals));
        var text = value.ToString("0.############################",
            CultureInfo.InvariantCulture);
        var separator = text.IndexOf('.');
        var whole = separator < 0 ? text : text[..separator];
        var fraction = separator < 0 ? string.Empty : text[(separator + 1)..];
        if (fraction.Length > decimals)
        {
            if (fraction[decimals..].Any(static ch => ch != '0'))
                throw new InvalidOperationException(
                    $"Value '{value}' has more than {decimals} decimals.");
            fraction = fraction[..decimals];
        }
        fraction = fraction.PadRight(decimals, '0');
        var digits = (whole + fraction).TrimStart('0');
        return digits.IsEmpty() ? BigInteger.Zero : BigInteger.Parse(digits,
            NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    public static decimal FromBaseUnits(this BigInteger value, int decimals)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var digits = value.ToString(CultureInfo.InvariantCulture);
        if (decimals > 0)
        {
            digits = digits.PadLeft(decimals + 1, '0');
            digits = digits.Insert(digits.Length - decimals, ".");
        }
        if (!decimal.TryParse(digits, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var result))
            throw new OverflowException(
                "Token amount exceeds the supported decimal range.");
        return result;
    }

    public static DateTime ToUtcTime(this string seconds)
    {
        if (!long.TryParse(seconds, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new InvalidDataException(
                $"Invalid Unix timestamp '{seconds}'.");
        return DateTime.UnixEpoch.AddSeconds(value);
    }

    public static long ToUnixSeconds(this DateTime value)
        => (long)Math.Floor((value.ToUniversalTime() -
            DateTime.UnixEpoch).TotalSeconds);

    public static string NativeSymbol(this UniswapChains chain)
        => chain switch
        {
            UniswapChains.BnbSmartChain => "BNB",
            UniswapChains.Polygon => "POL",
            UniswapChains.Monad => "MON",
            UniswapChains.MonadTestnet => "MON",
            UniswapChains.Avalanche => "AVAX",
            UniswapChains.Celo => "CELO",
            UniswapChains.Arc => "USDC",
            UniswapChains.Tempo => "USD",
            _ => "ETH",
        };

    public static bool HasNativeToken(this UniswapChains chain)
        => chain != UniswapChains.Tempo;

    public static int NativeDecimals(this UniswapChains chain)
        => chain == UniswapChains.Arc ? 6 : 18;

    public static string ToWire(this UniswapRouterVersions version)
        => version switch
        {
            UniswapRouterVersions.Version2_0 => "2.0",
            UniswapRouterVersions.Version2_1_1 => "2.1.1",
            UniswapRouterVersions.Version2_2_0 => "2.2.0",
            _ => throw new ArgumentOutOfRangeException(nameof(version),
                version, null),
        };

    public static CurrencyTypes? ToCurrency(this string value)
        => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
            ? currency
            : null;
}
