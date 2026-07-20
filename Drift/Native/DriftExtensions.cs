namespace StockSharp.Drift.Native;

static class DriftExtensions
{
	public const string VelocityProgramAddress =
		"vELoC1audYbSYVRXn1vPaV8Axoa9oU6BYmNGZZBDZ1P";
	public const decimal PricePrecision = 1_000_000m;

	public static string NormalizePublicKey(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		byte[] bytes;
		try
		{
			bytes = Encoders.Base58.DecodeData(value);
		}
		catch (Exception error) when (error is FormatException or
			ArgumentException)
		{
			throw new FormatException($"Invalid Solana public key '{value}'.",
				error);
		}
		if (bytes.Length != PublicKey.PublicKeyLength)
			throw new FormatException($"Invalid Solana public key '{value}'.");
		return new PublicKey(bytes).Key;
	}

	public static string NormalizeSignature(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		byte[] bytes;
		try
		{
			bytes = Encoders.Base58.DecodeData(value);
		}
		catch (Exception error) when (error is FormatException or
			ArgumentException)
		{
			throw new FormatException(
				$"Invalid Solana transaction signature '{value}'.", error);
		}
		if (bytes.Length != 64)
			throw new FormatException(
				$"Invalid Solana transaction signature '{value}'.");
		return value;
	}

	public static string NormalizeHttpEndpoint(this string value, string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			uri.Scheme != Uri.UriSchemeHttps || !uri.UserInfo.IsEmpty())
			throw new ArgumentException(
				"Drift HTTP endpoints must be absolute HTTPS URIs.", name);
		return value.TrimEnd('/') + "/";
	}

	public static Uri NormalizeSocketEndpoint(this string value, string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			uri.Scheme != "wss" || !uri.UserInfo.IsEmpty())
			throw new ArgumentException(
				"Drift WebSocket endpoints must be absolute WSS URIs.", name);
		return uri;
	}

	public static decimal ParseDriftDecimal(this string value, string field)
	{
		if (!decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"Drift returned invalid {field} '{value}'.");
		return result;
	}

	public static decimal? TryParseDriftDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result) ? result : null;

	public static string ToDriftWire(this decimal value)
		=> value.ToString("0.############################",
			CultureInfo.InvariantCulture);

	public static DateTime FromDriftSeconds(this long value)
	{
		if (value is < 1 or > 253402300799)
			throw new InvalidDataException(
				$"Drift returned invalid Unix timestamp '{value}'.");
		return DateTime.UnixEpoch.AddSeconds(value);
	}

	public static DateTime FromDriftMilliseconds(this long value)
	{
		if (value is < 1 or > 253402300799999)
			throw new InvalidDataException(
				$"Drift returned invalid Unix timestamp '{value}'.");
		return DateTime.UnixEpoch.AddMilliseconds(value);
	}

	public static long ToDriftSeconds(this DateTime value)
		=> checked((long)(value.ToUniversalTime() - DateTime.UnixEpoch)
			.TotalSeconds);

	public static SecurityId ToStockSharp(this DriftMarket market)
		=> new()
		{
			SecurityCode = market.Symbol,
			BoardCode = BoardCodes.Drift,
		};

	public static SecurityId ToDriftSecurityId(this string symbol)
		=> new()
		{
			SecurityCode = symbol,
			BoardCode = BoardCodes.Drift,
		};

	public static SecurityTypes ToStockSharp(this DriftMarketTypes type)
		=> type == DriftMarketTypes.Perpetual
			? SecurityTypes.Future
			: SecurityTypes.CryptoCurrency;

	public static CurrencyTypes? ToDriftCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static Sides ToStockSharpDirection(this string value)
		=> value.EqualsIgnoreCase("long")
			? Sides.Buy
			: value.EqualsIgnoreCase("short")
				? Sides.Sell
				: throw new InvalidDataException(
					$"Drift returned unknown order direction '{value}'.");

	public static DriftOrderDirections ToDrift(this Sides side)
		=> side == Sides.Buy
			? DriftOrderDirections.Long
			: DriftOrderDirections.Short;

	public static DriftApiOrderTypes ToDrift(this OrderTypes type)
		=> type switch
		{
			OrderTypes.Market => DriftApiOrderTypes.Market,
			OrderTypes.Limit => DriftApiOrderTypes.Limit,
			_ => throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(type, 0)),
		};

	public static OrderTypes ToStockSharpOrderType(this string value)
		=> value.EqualsIgnoreCase("market")
			? OrderTypes.Market
			: OrderTypes.Limit;

	public static OrderStates ToStockSharpOrderState(this string value)
		=> value?.ToLowerInvariant() switch
		{
			"open" => OrderStates.Active,
			"filled" => OrderStates.Done,
			"canceled" or "cancelled" or "expired" => OrderStates.Done,
			"failed" or "rejected" => OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static string ToDriftResolution(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			_ when timeFrame == TimeSpan.FromMinutes(1) => "1",
			_ when timeFrame == TimeSpan.FromMinutes(5) => "5",
			_ when timeFrame == TimeSpan.FromMinutes(15) => "15",
			_ when timeFrame == TimeSpan.FromHours(1) => "60",
			_ when timeFrame == TimeSpan.FromHours(4) => "240",
			_ when timeFrame == TimeSpan.FromDays(1) => "D",
			_ when timeFrame == TimeSpan.FromDays(7) => "W",
			_ when timeFrame >= TimeSpan.FromDays(28) &&
				timeFrame <= TimeSpan.FromDays(31) => "M",
			_ => throw new NotSupportedException(
				$"Drift does not support the {timeFrame} candle interval."),
		};

	public static decimal GetVolumeStep(this DriftMarket market)
	{
		if (market.Precision is < 0 or > 18)
			throw new InvalidDataException(
				$"Drift market '{market.Symbol}' has invalid precision " +
				$"'{market.Precision}'.");
		var step = 1m;
		for (var index = 0; index < market.Precision; index++)
			step /= 10m;
		return market.Limits?.Amount?.Minimum is decimal minimum && minimum > step
			? minimum
			: step;
	}

	public static decimal FromDlobPrice(this string value)
		=> value.ParseDriftDecimal("DLOB price") / PricePrecision;

	public static decimal FromDlobSize(this string value, int precision)
	{
		var scale = 1m;
		for (var index = 0; index < precision; index++)
			scale *= 10m;
		return value.ParseDriftDecimal("DLOB size") / scale;
	}

	public static decimal GetTradePrice(this DriftTrade trade)
	{
		var volume = trade.BaseAssetAmountFilled.ParseDriftDecimal(
			"trade base amount");
		if (volume <= 0)
			throw new InvalidDataException(
				"Drift returned a trade with zero base amount.");
		return trade.QuoteAssetAmountFilled.ParseDriftDecimal(
			"trade quote amount") / volume;
	}
}
