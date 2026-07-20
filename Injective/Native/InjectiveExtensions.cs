namespace StockSharp.Injective.Native;

static class InjectiveExtensions
{
	private const string _mainnetIndexer =
		"https://sentry.exchange.grpc-web.injective.network";
	private const string _testnetIndexer =
		"https://testnet.sentry.exchange.grpc-web.injective.network";
	private const string _mainnetGrpc =
		"https://sentry.exchange.grpc.injective.network:443";
	private const string _testnetGrpc =
		"https://testnet.sentry.exchange.grpc.injective.network:443";
	private const string _mainnetChain =
		"https://sentry.lcd.injective.network";
	private const string _testnetChain =
		"https://testnet.sentry.lcd.injective.network";
	private const string _mainnetSocket =
		"wss://sentry.tm.injective.network:443/websocket";
	private const string _testnetSocket =
		"wss://testnet.sentry.tm.injective.network:443/websocket";

	public static string IndexerEndpoint(this InjectiveEnvironments environment)
		=> environment switch
		{
			InjectiveEnvironments.Mainnet => _mainnetIndexer,
			InjectiveEnvironments.Testnet => _testnetIndexer,
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};

	public static string GrpcEndpoint(this InjectiveEnvironments environment)
		=> environment switch
		{
			InjectiveEnvironments.Mainnet => _mainnetGrpc,
			InjectiveEnvironments.Testnet => _testnetGrpc,
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};

	public static string ChainEndpoint(this InjectiveEnvironments environment)
		=> environment switch
		{
			InjectiveEnvironments.Mainnet => _mainnetChain,
			InjectiveEnvironments.Testnet => _testnetChain,
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};

	public static string ChainSocketEndpoint(
		this InjectiveEnvironments environment)
		=> environment switch
		{
			InjectiveEnvironments.Mainnet => _mainnetSocket,
			InjectiveEnvironments.Testnet => _testnetSocket,
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};

	public static string ChainId(this InjectiveEnvironments environment)
		=> environment switch
		{
			InjectiveEnvironments.Mainnet => "injective-1",
			InjectiveEnvironments.Testnet => "injective-888",
			_ => throw new ArgumentOutOfRangeException(nameof(environment),
				environment, null),
		};

	public static decimal ParseInjectiveDecimal(this string value, string name)
	{
		if (!decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var parsed))
			throw new InvalidDataException(
				$"Injective returned an invalid {name} '{value}'.");
		return parsed;
	}

	public static decimal? TryParseInjectiveDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var parsed)
			? parsed
			: null;

	public static decimal Pow10(int exponent)
	{
		if (exponent is < -28 or > 28)
			throw new ArgumentOutOfRangeException(nameof(exponent), exponent,
				"Injective decimal exponent is outside System.Decimal range.");
		var value = 1m;
		if (exponent >= 0)
			for (var i = 0; i < exponent; i++)
				value *= 10m;
		else
			for (var i = 0; i > exponent; i--)
				value /= 10m;
		return value;
	}

	public static DateTime FromInjectiveMilliseconds(this long value)
	{
		if (value <= 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		return DateTime.UnixEpoch.AddMilliseconds(value);
	}

	public static DateTime FromInjectiveSeconds(this long value)
	{
		if (value <= 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		return DateTime.UnixEpoch.AddSeconds(value);
	}

	public static long ToInjectiveMilliseconds(this DateTime value)
		=> checked((long)(value.ToUniversalTime() - DateTime.UnixEpoch)
			.TotalMilliseconds);

	public static long ToInjectiveSeconds(this DateTime value)
		=> checked((long)(value.ToUniversalTime() - DateTime.UnixEpoch)
			.TotalSeconds);

	public static InjectiveMarket ToMarket(this InjectiveSpotMarket source)
	{
		ArgumentNullException.ThrowIfNull(source);
		var baseToken = source.BaseTokenMeta ?? throw new InvalidDataException(
			"Injective spot market has no base token metadata.");
		var quoteToken = source.QuoteTokenMeta ?? throw new InvalidDataException(
			"Injective spot market has no quote token metadata.");
		var baseDecimals = ValidateDecimals(baseToken.Decimals, "base token");
		var quoteDecimals = ValidateDecimals(quoteToken.Decimals, "quote token");
		var priceStep = source.MinPriceTickSize.ParseInjectiveDecimal(
			"spot price step") * Pow10(baseDecimals - quoteDecimals);
		var volumeStep = source.MinQuantityTickSize.ParseInjectiveDecimal(
			"spot volume step") / Pow10(baseDecimals);
		return new()
		{
			MarketId = NormalizeMarketId(source.MarketId),
			Kind = InjectiveMarketKinds.Spot,
			Status = source.MarketStatus,
			Ticker = source.Ticker,
			Code = CreateCode(source.Ticker, baseToken.Symbol, quoteToken.Symbol,
				false),
			BaseSymbol = baseToken.Symbol.ThrowIfEmpty("base token symbol"),
			QuoteSymbol = quoteToken.Symbol.ThrowIfEmpty("quote token symbol"),
			BaseDenom = source.BaseDenom.ThrowIfEmpty("base denom"),
			QuoteDenom = source.QuoteDenom.ThrowIfEmpty("quote denom"),
			BaseDecimals = baseDecimals,
			QuoteDecimals = quoteDecimals,
			PriceStep = priceStep,
			VolumeStep = volumeStep,
			MinimumNotional = source.MinNotional.ParseInjectiveDecimal(
				"minimum notional") / Pow10(quoteDecimals),
		};
	}

	public static InjectiveMarket ToMarket(this InjectiveDerivativeMarket source)
	{
		ArgumentNullException.ThrowIfNull(source);
		var quoteToken = source.QuoteTokenMeta ?? throw new InvalidDataException(
			"Injective derivative market has no quote token metadata.");
		var quoteDecimals = ValidateDecimals(quoteToken.Decimals, "quote token");
		var (baseSymbol, quoteSymbol) = SplitTicker(source.Ticker,
			quoteToken.Symbol);
		return new()
		{
			MarketId = NormalizeMarketId(source.MarketId),
			Kind = InjectiveMarketKinds.Derivative,
			Status = source.MarketStatus,
			Ticker = source.Ticker,
			Code = CreateCode(source.Ticker, baseSymbol, quoteSymbol,
				source.IsPerpetual),
			BaseSymbol = baseSymbol,
			QuoteSymbol = quoteSymbol,
			QuoteDenom = source.QuoteDenom.ThrowIfEmpty("quote denom"),
			QuoteDecimals = quoteDecimals,
			PriceStep = source.MinPriceTickSize.ParseInjectiveDecimal(
				"derivative price step") / Pow10(quoteDecimals),
			VolumeStep = source.MinQuantityTickSize.ParseInjectiveDecimal(
				"derivative volume step"),
			MinimumNotional = source.MinNotional.ParseInjectiveDecimal(
				"minimum notional") / Pow10(quoteDecimals),
			InitialMarginRatio = source.InitialMarginRatio
				.TryParseInjectiveDecimal() ?? 0m,
			MaintenanceMarginRatio = source.MaintenanceMarginRatio
				.TryParseInjectiveDecimal() ?? 0m,
			IsPerpetual = source.IsPerpetual,
			ExpiryDate = source.ExpiryFuturesMarketInfo?.ExpirationTimestamp > 0
				? source.ExpiryFuturesMarketInfo.ExpirationTimestamp
					.FromInjectiveSeconds()
				: null,
		};
	}

	public static SecurityId ToInjectiveSecurityId(this InjectiveMarket market)
	{
		ArgumentNullException.ThrowIfNull(market);
		return new()
		{
			SecurityCode = market.Code,
			BoardCode = BoardCodes.Injective,
		};
	}

	public static Sides ToStockSharpSide(this string value)
		=> value?.ToLowerInvariant() switch
		{
			"buy" or "long" => Sides.Buy,
			"sell" or "short" => Sides.Sell,
			_ => throw new InvalidDataException(
				$"Injective returned unknown side '{value}'."),
		};

	public static OrderStates ToStockSharpOrderState(this string value,
		bool? isActive = null)
		=> value?.ToLowerInvariant() switch
		{
			"booked" or "partial_filled" or "partiallyfilled" or "active" =>
				OrderStates.Active,
			"filled" or "canceled" or "cancelled" or "expired" or
				"unfilled" => OrderStates.Done,
			"failed" or "rejected" => OrderStates.Failed,
			_ when isActive == true => OrderStates.Active,
			_ when isActive == false => OrderStates.Done,
			_ => OrderStates.Pending,
		};

	public static OrderTypes ToStockSharpOrderType(this InjectiveOrder order)
	{
		ArgumentNullException.ThrowIfNull(order);
		if (order.IsConditional || !order.TriggerPrice.IsEmpty() &&
			order.TriggerPrice.TryParseInjectiveDecimal() is not 0m)
			return OrderTypes.Conditional;
		return order.OrderType?.Contains("market",
			StringComparison.OrdinalIgnoreCase) == true ||
			order.ExecutionType?.Contains("market",
				StringComparison.OrdinalIgnoreCase) == true
			? OrderTypes.Market
			: OrderTypes.Limit;
	}

	public static string ToChartResolution(this TimeSpan value)
	{
		if (value <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(value));
		if (value.TotalMinutes is >= 1 and <= 1440 &&
			value.Ticks % TimeSpan.TicksPerMinute == 0)
			return checked((int)value.TotalMinutes).ToString(
				CultureInfo.InvariantCulture);
		if (value.Ticks % TimeSpan.TicksPerDay == 0 &&
			value.TotalDays % 7 == 0)
			return checked((int)(value.TotalDays / 7)).ToString(
				CultureInfo.InvariantCulture) + "W";
		if (value.Ticks % TimeSpan.TicksPerDay == 0)
			return checked((int)value.TotalDays).ToString(
				CultureInfo.InvariantCulture) + "D";
		throw new NotSupportedException(
			$"Injective does not support the {value} candle time-frame.");
	}

	private static int ValidateDecimals(int value, string name)
		=> value is >= 0 and <= 28
			? value
			: throw new InvalidDataException(
				$"Injective returned invalid {name} decimals '{value}'.");

	private static string NormalizeMarketId(string value)
	{
		value = value.ThrowIfEmpty("market ID").Trim().ToLowerInvariant();
		if (value.Length != 66 || !value.StartsWith("0x",
			StringComparison.Ordinal) || value[2..].Any(static ch =>
				!Uri.IsHexDigit(ch)))
			throw new InvalidDataException(
				$"Injective returned invalid market ID '{value}'.");
		return value;
	}

	private static string CreateCode(string ticker, string baseSymbol,
		string quoteSymbol, bool isPerpetual)
	{
		var source = ticker.IsEmpty()
			? baseSymbol + "-" + quoteSymbol + (isPerpetual ? "-PERP" : null)
			: ticker;
		var code = new string(source.Trim().ToUpperInvariant().Select(static ch =>
			char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
		while (code.Contains("--", StringComparison.Ordinal))
			code = code.Replace("--", "-", StringComparison.Ordinal);
		return code.Trim('-').ThrowIfEmpty("market code");
	}

	private static (string Base, string Quote) SplitTicker(string ticker,
		string fallbackQuote)
	{
		var normalized = ticker?.ToUpperInvariant()
			.Replace(" PERP", string.Empty, StringComparison.Ordinal)
			.Replace("-PERP", string.Empty, StringComparison.Ordinal);
		var parts = normalized?.Split(['/', '-'],
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
		return parts.Length >= 2
			? (parts[0], parts[1])
			: (normalized.ThrowIfEmpty("derivative ticker"),
				fallbackQuote.ThrowIfEmpty("quote token symbol"));
	}
}
