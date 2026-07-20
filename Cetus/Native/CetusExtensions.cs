namespace StockSharp.Cetus.Native;

static class CetusExtensions
{
	public const string SuiCoinType =
		"0x0000000000000000000000000000000000000000000000000000000000000002" +
		"::sui::SUI";
	public const string UsdcCoinType =
		"0xdba34672e30cb065b1f93e3ab55318768fd6fef66c15942c9f7cb846e2f900e7" +
		"::usdc::USDC";
	public const string ClmmPackage =
		"0x1eabed72c53feb3805120a081dc15963c204dc8d091542592abaf7a35689b2fb";
	public const string IntegrationPackage =
		"0xfbb32ac0fa89a3cb0c56c745b688c6d2a53ac8e43447119ad822763997ffb9c3";
	public const string GlobalConfig =
		"0xdaa46292632c3c4d8f31f23ea0f9b36a28ff3677e9684980e4438403a67a3d8f";
	public const string Clock =
		"0x0000000000000000000000000000000000000000000000000000000000000006";
	public const string SwapEventType = ClmmPackage + "::pool::SwapEvent";
	public const string MinimumSqrtPrice = "4295048016";
	public const string MaximumSqrtPrice = "79226673515401279992447579055";

	private const string _base58Alphabet =
		"123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
	private static readonly Regex _addressPattern = new(
		@"0x[0-9a-fA-F]+", RegexOptions.Compiled |
		RegexOptions.CultureInvariant);

	public static string NormalizeSuiAddress(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException(
				$"Invalid Sui address '{value}'.", nameof(value));
		var hex = value[2..];
		if (hex.Length is < 1 or > 64 || hex.Any(static ch =>
			!Uri.IsHexDigit(ch)))
			throw new ArgumentException(
				$"Invalid Sui address '{value}'.", nameof(value));
		return "0x" + hex.ToLowerInvariant().PadLeft(64, '0');
	}

	public static string NormalizeCoinType(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		value = new string(value.Where(static ch => !char.IsWhiteSpace(ch))
			.ToArray());
		if (!value.Contains("::", StringComparison.Ordinal))
			throw new ArgumentException(
				$"Invalid Sui coin type '{value}'.", nameof(value));
		value = _addressPattern.Replace(value, static match =>
			match.Value.NormalizeSuiAddress());
		if (value.Any(static ch => !(char.IsLetterOrDigit(ch) ||
			ch is 'x' or ':' or '_' or '<' or '>' or ',')))
			throw new ArgumentException(
				$"Invalid Sui coin type '{value}'.", nameof(value));
		return value;
	}

	public static (string CoinA, string CoinB) ParsePoolCoinTypes(
		this string objectType)
	{
		objectType = objectType.ThrowIfEmpty(nameof(objectType));
		var prefix = ClmmPackage + "::pool::Pool<";
		if (!objectType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
			!objectType.EndsWith('>'))
			throw new InvalidDataException(
				$"Object type '{objectType}' is not a Cetus CLMM pool.");
		var body = objectType[prefix.Length..^1];
		var depth = 0;
		var separator = -1;
		for (var index = 0; index < body.Length; index++)
		{
			switch (body[index])
			{
				case '<':
					depth++;
					break;
				case '>':
					if (--depth < 0)
						throw new InvalidDataException(
							$"Malformed Cetus pool type '{objectType}'.");
					break;
				case ',' when depth == 0:
					if (separator >= 0)
						throw new InvalidDataException(
							$"Malformed Cetus pool type '{objectType}'.");
					separator = index;
					break;
			}
		}
		if (depth != 0 || separator <= 0 || separator == body.Length - 1)
			throw new InvalidDataException(
				$"Malformed Cetus pool type '{objectType}'.");
		return (body[..separator].NormalizeCoinType(),
			body[(separator + 1)..].NormalizeCoinType());
	}

	public static string NormalizeTokenSymbol(this string value,
		string coinType)
	{
		value = value?.Trim();
		if (!value.IsEmpty() && value.Length <= 20 && value.All(static ch =>
			char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-'))
			return value.ToUpperInvariant();
		var address = coinType.NormalizeCoinType()[2..66];
		return "TOKEN-" + address[..6].ToUpperInvariant();
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

	public static string NormalizeSecurityCode(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
		if (value.Length > 64 || value.Any(static ch =>
			!(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')))
			throw new ArgumentException(
				$"Invalid Cetus security code '{value}'.", nameof(value));
		return value;
	}

	public static SecurityId ToStockSharp(this CetusMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.Cetus,
		};

	public static ulong ToBaseUnits(this decimal value, int decimals)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		if (decimals is < 0 or > 28)
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
		if (digits.IsEmpty())
			return 0;
		var amount = BigInteger.Parse(digits, NumberStyles.Integer,
			CultureInfo.InvariantCulture);
		if (amount > ulong.MaxValue)
			throw new OverflowException("Sui coin amount exceeds u64.");
		return (ulong)amount;
	}

	public static decimal FromBaseUnits(this ulong value, int decimals)
	{
		if (decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		var result = (decimal)value;
		for (var index = 0; index < decimals; index++)
			result /= 10m;
		return result;
	}

	public static ulong ApplyMinimumSlippage(this ulong value,
		int basisPoints)
	{
		if (basisPoints is < 0 or >= 10_000)
			throw new ArgumentOutOfRangeException(nameof(basisPoints));
		return (ulong)((BigInteger)value * (10_000 - basisPoints) / 10_000);
	}

	public static ulong ApplyMaximumSlippage(this ulong value,
		int basisPoints)
	{
		if (basisPoints is < 0 or >= 10_000)
			throw new ArgumentOutOfRangeException(nameof(basisPoints));
		var protectedValue = ((BigInteger)value * (10_000 + basisPoints) +
			9_999) / 10_000;
		if (protectedValue > ulong.MaxValue)
			throw new OverflowException("Protected Sui amount exceeds u64.");
		return (ulong)protectedValue;
	}

	public static DateTime ToUtc(this Timestamp value, DateTime fallback)
	{
		if (value is null)
			return fallback.Kind == DateTimeKind.Utc
				? fallback
				: fallback.ToUniversalTime();
		return DateTime.SpecifyKind(value.ToDateTime(), DateTimeKind.Utc);
	}

	public static string NormalizeTransactionDigest(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Length is < 32 or > 64 || value.Any(ch =>
			_base58Alphabet.IndexOf(ch) < 0))
			throw new InvalidDataException(
				$"Invalid Sui transaction digest '{value}'.");
		return value;
	}

	public static CetusSwapEvent ReadSwapEvent(this Event item,
		string transactionDigest, int eventIndex, DateTime time)
	{
		ArgumentNullException.ThrowIfNull(item);
		if (!item.EventType.EqualsIgnoreCase(SwapEventType))
			return null;
		var bytes = item.Contents?.Value?.ToByteArray();
		if (bytes is not { Length: 153 })
			throw new InvalidDataException(
				"Cetus SwapEvent BCS payload has an unexpected length.");
		var offset = 0;
		var isAToB = bytes[offset++] switch
		{
			0 => false,
			1 => true,
			_ => throw new InvalidDataException(
				"Cetus SwapEvent contains an invalid boolean."),
		};
		var pool = ReadAddress(bytes, ref offset);
		var partner = ReadAddress(bytes, ref offset);
		var input = ReadUInt64(bytes, ref offset);
		var output = ReadUInt64(bytes, ref offset);
		var reference = ReadUInt64(bytes, ref offset);
		var fee = ReadUInt64(bytes, ref offset);
		var vaultA = ReadUInt64(bytes, ref offset);
		var vaultB = ReadUInt64(bytes, ref offset);
		var before = ReadUInt128(bytes, ref offset);
		var after = ReadUInt128(bytes, ref offset);
		var steps = ReadUInt64(bytes, ref offset);
		if (offset != bytes.Length || input == 0 || output == 0)
			throw new InvalidDataException(
				"Cetus SwapEvent contains invalid swap amounts.");
		return new()
		{
			TransactionDigest = transactionDigest.IsEmpty()
				? null
				: transactionDigest.NormalizeTransactionDigest(),
			EventIndex = eventIndex,
			Time = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime(),
			PoolId = pool,
			PartnerId = partner,
			IsAToB = isAToB,
			InputAmount = input,
			OutputAmount = output,
			ReferenceAmount = reference,
			FeeAmount = fee,
			VaultAAmount = vaultA,
			VaultBAmount = vaultB,
			BeforeSqrtPrice = before,
			AfterSqrtPrice = after,
			Steps = steps,
		};
	}

	public static CurrencyTypes? ToCurrency(this string value)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			return null;
		return value.ToUpperInvariant() switch
		{
			"USD" or "USDC" or "USDT" or "DAI" => CurrencyTypes.USD,
			"EUR" or "EURC" => CurrencyTypes.EUR,
			"BTC" or "WBTC" => CurrencyTypes.BTC,
			_ => System.Enum.TryParse<CurrencyTypes>(value, true,
				out var currency)
				? currency
				: null,
		};
	}

	private static string ReadAddress(byte[] value, ref int offset)
	{
		var result = "0x" + Convert.ToHexString(value, offset, 32)
			.ToLowerInvariant();
		offset += 32;
		return result;
	}

	private static ulong ReadUInt64(byte[] value, ref int offset)
	{
		var result = BinaryPrimitives.ReadUInt64LittleEndian(
			value.AsSpan(offset, sizeof(ulong)));
		offset += sizeof(ulong);
		return result;
	}

	private static BigInteger ReadUInt128(byte[] value, ref int offset)
	{
		var result = new BigInteger(value.AsSpan(offset, 16), true, false);
		offset += 16;
		return result;
	}
}
