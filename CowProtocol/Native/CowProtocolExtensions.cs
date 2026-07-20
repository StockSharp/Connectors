namespace StockSharp.CowProtocol.Native;

static class CowProtocolExtensions
{
    public const string NativeTokenAddress =
        "0x0000000000000000000000000000000000000000";
    public const string ProbeAddress =
        "0x0000000000000000000000000000000000000001";
    public const string SettlementAddress =
        "0x9008d19f58aabd9ed0d60971565aa8510560ab41";
    public const string VaultRelayerAddress =
        "0xc92e8bdf79f0507f65a392b0ab4667716bfe0110";
    public const string EmptyAppData = "{}";

    public static readonly string EmptyAppDataHash = "0x" +
        new Sha3Keccack().CalculateHash(Encoding.UTF8.GetBytes(EmptyAppData))
            .ToHex();
    public static readonly string TradeTopic = AbiTopic(
        "Trade(address,address,address,uint256,uint256,uint256,bytes)");

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

    public static string GetApiEndpoint(this CowProtocolChains chain)
        => chain switch
        {
            CowProtocolChains.Ethereum => "https://api.cow.fi/mainnet",
            CowProtocolChains.Gnosis => "https://api.cow.fi/xdai",
            CowProtocolChains.Arbitrum => "https://api.cow.fi/arbitrum_one",
            CowProtocolChains.Base => "https://api.cow.fi/base",
            CowProtocolChains.Avalanche => "https://api.cow.fi/avalanche",
            CowProtocolChains.Polygon => "https://api.cow.fi/polygon",
            CowProtocolChains.Bnb => "https://api.cow.fi/bnb",
            _ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
                "Unsupported CoW Protocol chain."),
        };

    public static string GetRpcEndpoint(this CowProtocolChains chain)
        => chain switch
        {
            CowProtocolChains.Ethereum =>
                "https://ethereum-rpc.publicnode.com",
            CowProtocolChains.Gnosis => "https://rpc.gnosischain.com",
            CowProtocolChains.Arbitrum => "https://arb1.arbitrum.io/rpc",
            CowProtocolChains.Base => "https://mainnet.base.org",
            CowProtocolChains.Avalanche =>
                "https://api.avax.network/ext/bc/C/rpc",
            CowProtocolChains.Polygon =>
                "https://polygon-bor-rpc.publicnode.com",
            CowProtocolChains.Bnb =>
                "https://bsc-dataseed.binance.org",
            _ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
                "Unsupported CoW Protocol chain."),
        };

    public static string GetDefaultMarkets(this CowProtocolChains chain)
        => chain switch
        {
            CowProtocolChains.Ethereum =>
                "0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2|" +
                "0xA0b86991c6218b36c1d19d4a2e9eb0ce3606eb48|" +
                "WETH-USDC",
            CowProtocolChains.Gnosis =>
                "0x6a023ccd1ff6f2045c3309768ead9e68f978f6e1|" +
                "0xe91d153e0b41518a2ce8dd3d7944fa863463a97d|" +
                "WETH-WXDAI",
            CowProtocolChains.Arbitrum =>
                "0x82af49447d8a07e3bd95bd0d56f35241523fbab1|" +
                "0xaf88d065e77c8cc2239327c5edb3a432268e5831|" +
                "WETH-USDC",
            CowProtocolChains.Base =>
                "0x4200000000000000000000000000000000000006|" +
                "0x833589fcd6edb6e08f4c7c32d4f71b54bda02913|" +
                "WETH-USDC",
            CowProtocolChains.Avalanche =>
                "0xb31f66aa3c1e785363f0875a1b74e27b85fd66c7|" +
                "0xb97ef9ef8734c71904d8002f8b6bc66dd9c48a6e|" +
                "WAVAX-USDC",
            CowProtocolChains.Polygon =>
                "0x7ceB23fD6bC0adD59E62ac25578270cFf1b9f619|" +
                "0x3c499c542cef5e3811e1192ce70d8cc03d5c3359|" +
                "WETH-USDC",
            CowProtocolChains.Bnb =>
                "0xbb4CdB9CBd36B01bD1cBaEBF2De08d9173bc095c|" +
                "0x55d398326f99059fF775485246999027B3197955|" +
                "WBNB-USDT",
            _ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
                "Unsupported CoW Protocol chain."),
        };

    public static string GetNativeSymbol(this CowProtocolChains chain)
        => chain switch
        {
            CowProtocolChains.Ethereum or CowProtocolChains.Arbitrum or
                CowProtocolChains.Base => "ETH",
            CowProtocolChains.Gnosis => "XDAI",
            CowProtocolChains.Avalanche => "AVAX",
            CowProtocolChains.Polygon => "POL",
            CowProtocolChains.Bnb => "BNB",
            _ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
                "Unsupported CoW Protocol chain."),
        };

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

    public static SecurityId ToStockSharp(this CowProtocolMarket market)
        => new()
        {
            SecurityCode = market.SecurityCode,
            BoardCode = BoardCodes.CowProtocol,
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

    public static DateTime ParseApiTime(this string value, string name)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var result))
            throw new InvalidDataException(
                $"CoW Protocol returned invalid {name} '{value}'.");
        return DateTime.SpecifyKind(result, DateTimeKind.Utc);
    }

    public static CurrencyTypes? ToCurrency(this string value)
    {
        value = value?.Trim();
        if (value.IsEmpty())
            return null;
        return value.ToUpperInvariant() switch
        {
            "USD" or "USDC" or "USDT" or "DAI" or "WXDAI" =>
                CurrencyTypes.USD,
            "EUR" or "EURC" => CurrencyTypes.EUR,
            "BTC" or "WBTC" => CurrencyTypes.BTC,
            _ => System.Enum.TryParse<CurrencyTypes>(value, true,
                out var currency)
                ? currency
                : null,
        };
    }

    public static string NormalizeOrderUid(this string value)
        => NormalizeHex(value, 56, "order UID");

    public static string NormalizeSignature(this string value)
        => NormalizeHex(value, 65, "EIP-712 signature");

    public static string NormalizeHash(this string value)
        => NormalizeHex(value, 32, "transaction hash");

    public static string NormalizeBytes32(this string value)
        => NormalizeHex(value, 32, "bytes32 value");

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

    public static byte[] ReadAbiBytes(string value, int index)
    {
        var offset = ReadAbiWord(value, index);
        if (offset < 0 || offset > int.MaxValue)
            throw new InvalidDataException("ABI byte-array offset is invalid.");
        var lengthIndex = checked((int)offset / 32);
        if (offset % 32 != 0)
            throw new InvalidDataException("ABI byte-array offset is unaligned.");
        var length = ReadAbiWord(value, lengthIndex);
        if (length < 0 || length > int.MaxValue)
            throw new InvalidDataException("ABI byte-array length is invalid.");
        var start = checked(2 + ((int)offset + 32) * 2);
        var characters = checked((int)length * 2);
        if (start < 2 || start + characters > value.Length)
            throw new InvalidDataException("ABI byte-array value is truncated.");
        return value.Substring(start, characters).HexToByteArray();
    }

    private static string NormalizeHex(string value, int bytes, string name)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim();
        if (value.Length != 2 + bytes * 2 || !value.StartsWith("0x",
            StringComparison.OrdinalIgnoreCase) || value.Skip(2).Any(
                static ch => !Uri.IsHexDigit(ch)))
            throw new InvalidDataException($"Invalid {name} '{value}'.");
        return "0x" + value[2..].ToLowerInvariant();
    }
}
