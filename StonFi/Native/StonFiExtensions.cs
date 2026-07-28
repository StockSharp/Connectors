namespace StockSharp.StonFi.Native;

static class StonFiExtensions
{
	public const string NativeAssetAddress =
		"EQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAM9c";
	public const int MaximumEventBlockRange = 1000;

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

	public static string GetSymbol(this StonAssetInfo asset)
		=> (asset?.Meta?.Symbol ?? asset?.Symbol).ThrowIfEmpty(
			nameof(asset)).Trim();

	public static string GetName(this StonAssetInfo asset)
		=> (asset?.Meta?.DisplayName ?? asset?.DisplayName ??
			asset?.GetSymbol()).ThrowIfEmpty(nameof(asset)).Trim();

	public static int GetDecimals(this StonAssetInfo asset)
	{
		ArgumentNullException.ThrowIfNull(asset);
		var decimals = asset.Meta?.Decimals ?? asset.Decimals ??
			throw new InvalidDataException(
				$"STON.fi asset '{asset.Address}' has no decimals.");
		if (decimals is < 0 or > 28)
			throw new InvalidDataException(
				$"STON.fi asset '{asset.Address}' has invalid decimals " +
					$"'{decimals}'.");
		return decimals;
	}

	public static bool IsNative(this StonAssetInfo asset)
		=> asset is not null &&
			(asset.Kind.EqualsIgnoreCase("Ton") ||
				asset.Address.IsNativeAsset());

	public static bool IsNativeAsset(this string address)
		=> !address.IsEmpty() && address.NormalizeTonAddress()
			.EqualsIgnoreCase(NativeAssetAddress);

	public static string NormalizeTonAddress(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		Address address;
		try
		{
			address = new(value);
		}
		catch (Exception error)
		{
			throw new ArgumentException(
				$"Invalid TON address '{value}'.", nameof(value), error);
		}
		if (address.IsTestOnly())
			throw new ArgumentException(
				$"Testnet TON address '{value}' cannot be used with STON.fi " +
					"mainnet.", nameof(value));
		return address.ToString(TonAddressType.Base64,
			new AddressStringifyOptions(true, false, true,
				address.GetWorkchain()));
	}

	public static bool SameTonAddress(this string left, string right)
		=> !left.IsEmpty() && !right.IsEmpty() &&
			left.NormalizeTonAddress().EqualsIgnoreCase(
				right.NormalizeTonAddress());

	public static SecurityId ToStockSharp(this StonMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.StonFi,
		};

	public static BigInteger ParseInteger(this string value,
		string fieldName = null)
	{
		value = value.ThrowIfEmpty(fieldName ?? nameof(value)).Trim();
		if (!BigInteger.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"STON.fi field '{fieldName ?? "value"}' is not an integer.");
		return result;
	}

	public static decimal ParseDecimal(this string value,
		string fieldName = null)
	{
		value = value.ThrowIfEmpty(fieldName ?? nameof(value)).Trim();
		if (!decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"STON.fi field '{fieldName ?? "value"}' is not a decimal.");
		return result;
	}

	public static decimal? TryParseDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result)
				? result
				: null;

	public static BigInteger ToBaseUnits(this decimal value, int decimals)
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
		return digits.IsEmpty()
			? BigInteger.Zero
			: BigInteger.Parse(digits, NumberStyles.Integer,
				CultureInfo.InvariantCulture);
	}

	public static decimal FromBaseUnits(this BigInteger value, int decimals)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		if (decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		var digits = value.ToString(CultureInfo.InvariantCulture);
		if (decimals > 0)
		{
			digits = digits.PadLeft(decimals + 1, '0');
			digits = digits.Insert(digits.Length - decimals, ".");
		}
		if (!decimal.TryParse(digits, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result))
			throw new OverflowException(
				"TON token amount exceeds the supported decimal range.");
		return result;
	}

	public static decimal? GetStep(int decimals)
	{
		if (decimals is < 0 or > 28)
			return null;
		var result = 1m;
		for (var index = 0; index < decimals; index++)
			result /= 10m;
		return result;
	}

	public static CurrencyTypes? ToCurrency(this string value)
		=> System.Enum.TryParse<CurrencyTypes>(value, true,
			out var currency)
				? currency
				: null;

	public static DateTime ToUtcTime(this long seconds)
	{
		if (seconds < 0)
			throw new InvalidDataException(
				$"Invalid Unix timestamp '{seconds}'.");
		return seconds.FromUnix();
	}

	public static StonTrade ToTrade(this StonEvent item)
	{
		ArgumentNullException.ThrowIfNull(item);
		if (!item.EventType.EqualsIgnoreCase("swap") ||
			item.Block is null || item.Block.Timestamp <= 0 ||
			item.TransactionId.IsEmpty() || item.PoolAddress.IsEmpty())
			return null;

		var amount0In = item.Amount0In.TryParseDecimal() ?? 0m;
		var amount0Out = item.Amount0Out.TryParseDecimal() ?? 0m;
		var amount1In = item.Amount1In.TryParseDecimal() ?? 0m;
		var amount1Out = item.Amount1Out.TryParseDecimal() ?? 0m;

		decimal volume;
		decimal turnover;
		Sides side;
		if (amount0In > 0 && amount1Out > 0)
		{
			volume = amount0In;
			turnover = amount1Out;
			side = Sides.Sell;
		}
		else if (amount1In > 0 && amount0Out > 0)
		{
			volume = amount0Out;
			turnover = amount1In;
			side = Sides.Buy;
		}
		else
			return null;

		var price = turnover / volume;
		if (price <= 0 || volume <= 0)
			return null;
		return new()
		{
			Id = $"{item.TransactionId}:{item.EventIndex}",
			Time = item.Block.Timestamp.ToUtcTime(),
			Price = price,
			Volume = volume,
			Turnover = turnover,
			Side = side,
			Maker = item.Maker,
			BlockNumber = item.Block.Number,
		};
	}

	public static StonCandle[] AggregateTrades(
		IEnumerable<StonTrade> trades, TimeSpan timeFrame)
	{
		ArgumentNullException.ThrowIfNull(trades);
		if (timeFrame <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(timeFrame));
		return
		[
			.. trades.Where(static trade => trade is not null)
				.OrderBy(static trade => trade.Time)
				.GroupBy(trade => new DateTime(
					trade.Time.ToUniversalTime().Ticks /
						timeFrame.Ticks * timeFrame.Ticks,
					DateTimeKind.Utc))
				.Select(group =>
				{
					var values = group.ToArray();
					return new StonCandle
					{
						OpenTime = group.Key,
						Open = values[0].Price,
						High = values.Max(static trade => trade.Price),
						Low = values.Min(static trade => trade.Price),
						Close = values[^1].Price,
						Volume = values.Sum(static trade => trade.Volume),
						Turnover = values.Sum(
							static trade => trade.Turnover),
						TradeCount = values.Length,
					};
				})
		];
	}

	public static string CreateSecurityCode(StonAssetInfo asset0,
		StonAssetInfo asset1)
		=> $"{asset0.GetSymbol()}/{asset1.GetSymbol()}";

	public static string GetPairKey(this StonPoolInfo pool)
	{
		var first = pool.Token0Address.NormalizeTonAddress();
		var second = pool.Token1Address.NormalizeTonAddress();
		return string.Compare(first, second,
			StringComparison.OrdinalIgnoreCase) <= 0
				? first + ":" + second
				: second + ":" + first;
	}
}
