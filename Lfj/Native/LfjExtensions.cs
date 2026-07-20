namespace StockSharp.Lfj.Native;

static class LfjExtensions
{
    public const int ChainId = 43_114;
    public const string NativeTokenAddress =
        "0x0000000000000000000000000000000000000000";
    public const string FactoryV22Address =
        "0xb43120c4745967fa9b93e79c149e66b0f2d6fe0c";
    public const string RouterV22Address =
        "0x18556da13313f3532c54711497a8fedac273220e";
    public const string WrappedAvaxAddress =
        "0xb31f66aa3c1e785363f0875a1b74e27b85fd66c7";
    public const string UsdcAddress =
        "0xb97ef9ef8734c71904d8002f8b6bc66dd9c48a6e";
    public static readonly string SwapTopic = AbiTopic(
        "Swap(address,address,uint24,bytes32,bytes32,uint24,bytes32,bytes32)");

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

    public static string NormalizeTokenSymbol(this string value,
        string address)
    {
        address = address.NormalizeAddress();
        value = value?.Trim();
        if (!value.IsEmpty() && value.Length <= 20 && value.All(static ch =>
            char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-'))
            return value.ToUpperInvariant();
        return "TOKEN-" + address[2..8].ToUpperInvariant();
    }

    public static string NormalizeTokenName(this string value,
        string fallback)
    {
        fallback = fallback.ThrowIfEmpty(nameof(fallback));
        value = value?.Trim();
        if (value.IsEmpty())
            return fallback;
        value = new string(value.Where(static ch => !char.IsControl(ch))
            .ToArray()).Trim();
        return value.IsEmpty()
            ? fallback
            : value.Truncate(128, string.Empty);
    }

    public static SecurityId ToStockSharp(this LfjMarket market)
        => new()
        {
            SecurityCode = market.SecurityCode,
            BoardCode = BoardCodes.Lfj,
        };

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

    public static DateTime ToUtcTime(this BigInteger seconds)
    {
        if (seconds < 0 || seconds > long.MaxValue)
            throw new InvalidDataException(
                $"Invalid Unix timestamp '{seconds}'.");
        try
        {
            return DateTime.UnixEpoch.AddSeconds((long)seconds);
        }
        catch (ArgumentOutOfRangeException error)
        {
            throw new InvalidDataException(
                $"Invalid Unix timestamp '{seconds}'.", error);
        }
    }

    public static long ToUnixSeconds(this DateTime value)
    {
        value = value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();
        return checked((long)Math.Floor(value.ToUnix()));
    }

    public static CurrencyTypes? ToCurrency(this string value)
    {
        value = value?.Trim();
        if (value.IsEmpty())
            return null;
        return value.ToUpperInvariant() switch
        {
            "USD" or "USDC" or "USDC.E" or "USDT" or "DAI" =>
                CurrencyTypes.USD,
            "EUR" or "EURC" => CurrencyTypes.EUR,
            "BTC" or "BTC.B" or "WBTC" => CurrencyTypes.BTC,
            _ => System.Enum.TryParse<CurrencyTypes>(value, true,
                out var currency)
                ? currency
                : null,
        };
    }

    public static string GetSwapTopic(this LfjPoolVersions version)
    {
        if (!System.Enum.IsDefined(version) || version != LfjPoolVersions.V22)
            throw new ArgumentOutOfRangeException(nameof(version), version,
                "Unsupported LFJ pool version.");
        return SwapTopic;
    }

    public static string AbiSelector(string signature)
        => new Sha3Keccack().CalculateHash(signature)[..8];

    public static string AbiTopic(string signature)
        => "0x" + new Sha3Keccack().CalculateHash(signature);

    public static string AbiWord(BigInteger value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var hex = value.ToString("x", CultureInfo.InvariantCulture);
        if (hex.Length > 64)
            throw new ArgumentOutOfRangeException(nameof(value),
                "ABI integer exceeds 256 bits.");
        return hex.PadLeft(64, '0');
    }

    public static string AbiAddress(string address)
        => address.NormalizeAddress()[2..].PadLeft(64, '0');

    public static string EncodeStaticCall(string signature,
        params string[] words)
        => "0x" + AbiSelector(signature) + string.Concat(words);

    public static BigInteger ReadAbiWord(string value, int index)
    {
        if (value.IsEmpty() || !value.StartsWith("0x",
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid ABI response.");
        var start = 2 + checked(index * 64);
        if (start < 2 || start + 64 > value.Length)
            throw new InvalidDataException("ABI response is truncated.");
        return BigInteger.Parse("0" + value.Substring(start, 64),
            NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }

    public static string ReadAbiAddress(string value, int index)
    {
        var word = ReadAbiWord(value, index);
        return ("0x" + word.ToString("x", CultureInfo.InvariantCulture)
            .PadLeft(40, '0')).NormalizeAddress();
    }

    public static (BigInteger X, BigInteger Y) ReadPackedAmounts(
        string value, int index)
    {
        var packed = ReadAbiWord(value, index);
        var mask = (BigInteger.One << 128) - 1;
        return (packed & mask, packed >> 128);
    }
}
