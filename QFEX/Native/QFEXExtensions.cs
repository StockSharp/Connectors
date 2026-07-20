namespace StockSharp.QFEX.Native;

static class QFEXExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
	];

	public static QFEXCandleIntervals ToQFEX(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalMinutes: 1 } => QFEXCandleIntervals.OneMinute,
			{ TotalMinutes: 5 } => QFEXCandleIntervals.FiveMinutes,
			{ TotalMinutes: 15 } => QFEXCandleIntervals.FifteenMinutes,
			{ TotalHours: 1 } => QFEXCandleIntervals.OneHour,
			{ TotalHours: 4 } => QFEXCandleIntervals.FourHours,
			{ TotalDays: 1 } => QFEXCandleIntervals.OneDay,
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame),
				timeFrame, "Unsupported QFEX candle time frame."),
		};

	public static TimeSpan ToStockSharp(this QFEXCandleIntervals interval)
		=> interval switch
		{
			QFEXCandleIntervals.OneMinute => TimeSpan.FromMinutes(1),
			QFEXCandleIntervals.FiveMinutes => TimeSpan.FromMinutes(5),
			QFEXCandleIntervals.FifteenMinutes => TimeSpan.FromMinutes(15),
			QFEXCandleIntervals.OneHour => TimeSpan.FromHours(1),
			QFEXCandleIntervals.FourHours => TimeSpan.FromHours(4),
			QFEXCandleIntervals.OneDay => TimeSpan.FromDays(1),
			_ => throw new ArgumentOutOfRangeException(nameof(interval), interval,
				"Unsupported QFEX candle interval."),
		};

	public static string ToWire(this QFEXCandleIntervals interval)
		=> interval switch
		{
			QFEXCandleIntervals.OneMinute => "1MIN",
			QFEXCandleIntervals.FiveMinutes => "5MINS",
			QFEXCandleIntervals.FifteenMinutes => "15MINS",
			QFEXCandleIntervals.OneHour => "1HOUR",
			QFEXCandleIntervals.FourHours => "4HOURS",
			QFEXCandleIntervals.OneDay => "1DAY",
			_ => throw new ArgumentOutOfRangeException(nameof(interval), interval,
				"Unsupported QFEX candle interval."),
		};

	public static decimal ParseDecimal(this string value, string fieldName)
	{
		if (value.IsEmpty() || !decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"QFEX returned invalid {fieldName} '{value}'.");
		return result;
	}

	public static decimal? TryParseDecimal(this string value)
		=> !value.IsEmpty() && decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static DateTime ToQFEXTime(this string value, string fieldName)
	{
		if (value.IsEmpty() || !DateTime.TryParse(value,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result))
			throw new InvalidDataException(
				$"QFEX returned invalid {fieldName} timestamp '{value}'.");
		return result.Kind == DateTimeKind.Utc
			? result
			: DateTime.SpecifyKind(result, DateTimeKind.Utc);
	}

	public static DateTime? TryToQFEXTime(this string value)
	{
		if (value.IsEmpty() || !DateTime.TryParse(value,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result))
			return null;
		return result.Kind == DateTimeKind.Utc
			? result
			: DateTime.SpecifyKind(result, DateTimeKind.Utc);
	}

	public static DateTime ToQFEXTime(this decimal unixSeconds)
	{
		if (unixSeconds <= 0)
			throw new InvalidDataException(
				$"QFEX returned invalid Unix timestamp {unixSeconds}.");
		try
		{
			var ticks = checked((long)decimal.Truncate(unixSeconds *
				TimeSpan.TicksPerSecond));
			return DateTime.UnixEpoch.AddTicks(ticks);
		}
		catch (OverflowException error)
		{
			throw new InvalidDataException(
				$"QFEX Unix timestamp {unixSeconds} is outside the supported range.",
				error);
		}
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static string ToIso8601(this DateTime value)
		=> value.EnsureUtc().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
			CultureInfo.InvariantCulture);

	public static long ToUnixSeconds(this DateTime value)
		=> checked((long)value.EnsureUtc().ToUnix());

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol.ThrowIfEmpty(nameof(symbol)).Trim()
				.ToUpperInvariant(),
			BoardCode = BoardCodes.QFEX,
		};

	public static SecurityId ToCurrencySecurity(this string currency)
		=> new()
		{
			SecurityCode = currency.ThrowIfEmpty(nameof(currency)).Trim()
				.ToUpperInvariant(),
			BoardCode = BoardCodes.QFEX,
		};

	public static CurrencyTypes? ToCurrency(this string value)
	{
		if (value.IsEmpty())
			return null;
		return value.Trim().ToUpperInvariant() switch
		{
			"USDC" or "USDT" => CurrencyTypes.USD,
			_ => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
				? currency
				: null,
		};
	}

	public static Sides ToStockSharp(this QFEXOrderDirections side)
		=> side switch
		{
			QFEXOrderDirections.Buy => Sides.Buy,
			QFEXOrderDirections.Sell => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				"Unsupported QFEX order direction."),
		};

	public static QFEXOrderDirections ToQFEX(this Sides side)
		=> side switch
		{
			Sides.Buy => QFEXOrderDirections.Buy,
			Sides.Sell => QFEXOrderDirections.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				"Unsupported order side."),
		};

	public static OrderTypes ToStockSharp(this QFEXOrderTypes orderType)
		=> orderType switch
		{
			QFEXOrderTypes.Limit or
			QFEXOrderTypes.AddLiquidityOnly => OrderTypes.Limit,
			QFEXOrderTypes.Market => OrderTypes.Market,
			QFEXOrderTypes.TakeProfit or
			QFEXOrderTypes.StopLoss or
			QFEXOrderTypes.StopMarket => OrderTypes.Conditional,
			_ => throw new ArgumentOutOfRangeException(nameof(orderType),
				orderType, "Unsupported QFEX order type."),
		};

	public static QFEXOrderTypes ToQFEX(this OrderTypes orderType,
		bool isPostOnly)
		=> orderType switch
		{
			OrderTypes.Limit when isPostOnly =>
				QFEXOrderTypes.AddLiquidityOnly,
			OrderTypes.Limit => QFEXOrderTypes.Limit,
			OrderTypes.Market when !isPostOnly => QFEXOrderTypes.Market,
			_ => throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0)),
		};

	public static TimeInForce ToStockSharp(this QFEXTimeInForces timeInForce)
		=> timeInForce switch
		{
			QFEXTimeInForces.GoodTillCancelled => TimeInForce.PutInQueue,
			QFEXTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			QFEXTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce),
				timeInForce, "Unsupported QFEX time in force."),
		};

	public static QFEXTimeInForces ToQFEX(this TimeInForce? timeInForce,
		OrderTypes orderType)
		=> timeInForce switch
		{
			TimeInForce.CancelBalance => QFEXTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => QFEXTimeInForces.FillOrKill,
			TimeInForce.PutInQueue or null when orderType == OrderTypes.Market =>
				QFEXTimeInForces.ImmediateOrCancel,
			TimeInForce.PutInQueue or null =>
				QFEXTimeInForces.GoodTillCancelled,
			_ => throw new NotSupportedException(
				$"QFEX does not support time in force '{timeInForce}'."),
		};

	public static bool IsFailed(this QFEXOrderStatuses status)
		=> status is not (QFEXOrderStatuses.Acknowledged or
			QFEXOrderStatuses.Filled or QFEXOrderStatuses.Modified or
			QFEXOrderStatuses.Cancelled or
			QFEXOrderStatuses.CancelledSelfTradePrevention);

	public static OrderStates ToStockSharp(this QFEXOrderStatuses status)
		=> status switch
		{
			QFEXOrderStatuses.Acknowledged or
			QFEXOrderStatuses.Modified => OrderStates.Active,
			QFEXOrderStatuses.Filled or
			QFEXOrderStatuses.Cancelled or
			QFEXOrderStatuses.CancelledSelfTradePrevention => OrderStates.Done,
			_ when status.IsFailed() => OrderStates.Failed,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status,
				"Unsupported QFEX order status."),
		};

	public static QFEXBalance ToBalance(this QFEXTradeStreamItem item)
		=> new()
		{
			Id = item.Id,
			UserId = item.UserId,
			Deposit = item.Deposit,
			RealizedProfitLoss = item.RealizedProfitLoss,
			UnrealizedProfitLoss = item.UnrealizedProfitLoss,
			OrderMargin = item.OrderMargin,
			PositionMargin = item.PositionMargin,
			AvailableBalance = item.AvailableBalance,
			NetFunding = item.NetFunding,
			Fees = item.Fees,
		};

	public static QFEXPosition ToPosition(this QFEXTradeStreamItem item)
		=> new()
		{
			Id = item.Id,
			Symbol = item.Symbol,
			Position = item.Position,
			AveragePrice = item.AveragePrice,
			RealizedProfitLoss = item.RealizedProfitLoss,
			UnrealizedProfitLoss = item.UnrealizedProfitLoss,
			NetFunding = item.NetFunding,
			Leverage = item.Leverage,
			InitialMargin = item.InitialMargin,
			MaintenanceMargin = item.MaintenanceMargin,
			OpenOrders = item.OpenOrders,
			OpenQuantity = item.OpenQuantity,
			MarginAllocation = item.MarginAllocation,
		};

	public static bool IsMultipleOf(this decimal value, decimal step)
		=> step > 0 && value % step == 0;
}

sealed class QFEXAuthenticator
{
	private readonly string _publicKey;
	private readonly string _secretKey;
	private readonly string _accountId;

	public QFEXAuthenticator(string publicKey, SecureString secret,
		string accountId)
	{
		_publicKey = publicKey?.Trim();
		_secretKey = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (_publicKey.IsEmpty() != _secretKey.IsEmpty())
			throw new ArgumentException(
				"QFEX API key and secret must be configured together.");
		_accountId = accountId?.Trim();
		if (!_accountId.IsEmpty() && !Guid.TryParse(_accountId, out _))
			throw new ArgumentException(
				"QFEX account ID must be a valid UUID.", nameof(accountId));
	}

	public bool IsAvailable => !_publicKey.IsEmpty() && !_secretKey.IsEmpty();

	public string AccountId => _accountId;

	public QFEXHmacCredentials CreateCredentials()
	{
		if (!IsAvailable)
			throw new InvalidOperationException(
				"QFEX API credentials are required for private operations.");
		Span<byte> bytes = stackalloc byte[16];
		RandomNumberGenerator.Fill(bytes);
		var nonce = Convert.ToHexString(bytes).ToLowerInvariant();
		var timestamp = DateTime.UtcNow.ToUnixSeconds();
		return new()
		{
			PublicKey = _publicKey,
			Nonce = nonce,
			UnixTimestamp = timestamp,
			Signature = Sign(nonce, timestamp),
		};
	}

	public void Sign(HttpRequestMessage request)
	{
		if (request is null)
			throw new ArgumentNullException(nameof(request));
		var credentials = CreateCredentials();
		request.Headers.TryAddWithoutValidation("x-qfex-public-key",
			credentials.PublicKey);
		request.Headers.TryAddWithoutValidation("x-qfex-hmac-signature",
			credentials.Signature);
		request.Headers.TryAddWithoutValidation("x-qfex-nonce",
			credentials.Nonce);
		request.Headers.TryAddWithoutValidation("x-qfex-timestamp",
			credentials.UnixTimestamp.ToString(CultureInfo.InvariantCulture));
		if (!_accountId.IsEmpty())
			request.Headers.TryAddWithoutValidation(
				"x-qfex-requested-account-id", _accountId);
	}

	private string Sign(string nonce, long timestamp)
	{
		var payload = Encoding.UTF8.GetBytes(nonce + ":" +
			timestamp.ToString(CultureInfo.InvariantCulture));
		var key = Encoding.UTF8.GetBytes(_secretKey);
		return Convert.ToHexString(HMACSHA256.HashData(key, payload))
			.ToLowerInvariant();
	}
}
