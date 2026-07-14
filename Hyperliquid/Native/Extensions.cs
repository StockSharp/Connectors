namespace StockSharp.Hyperliquid.Native;

static class Extensions
{
	public const string BoardCode = "HYPERLIQUID";

	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(3), "3m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(2), "2h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromHours(8), "8h" },
		{ TimeSpan.FromHours(12), "12h" },
		{ TimeSpan.FromDays(1), "1d" },
		{ TimeSpan.FromDays(3), "3d" },
		{ TimeSpan.FromDays(7), "1w" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "1M" },
	};

	public static string ToNative(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	public static TimeSpan ToTimeFrame(this string interval)
		=> TimeFrames.TryGetKey2(interval) ?? throw new ArgumentOutOfRangeException(nameof(interval), interval, LocalizedStrings.InvalidValue);

	public static SecurityId ToStockSharp(this string symbol)
	{
		if (symbol.IsEmpty())
			throw new ArgumentNullException(nameof(symbol));

		return new SecurityId
		{
			SecurityCode = symbol,
			BoardCode = BoardCode,
		};
	}

	public static string ToSymbol(this SecurityId securityId)
	{
		if (securityId.SecurityCode.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.WrongSecCode.Put(securityId));

		return securityId.SecurityCode;
	}

	public static decimal GetVolumeStep(this int sizeDecimals)
	{
		if (sizeDecimals <= 0)
			return 1m;

		var step = 1m;

		for (var i = 0; i < sizeDecimals; i++)
			step /= 10m;

		return step;
	}

	public static decimal? AsDecimal(this string value)
	{
		if (value.IsEmpty())
			return null;

		return value.To<decimal?>();
	}

	public static decimal? AsDecimal(this JToken token)
	{
		if (token is null)
			return null;

		return token.Type switch
		{
			JTokenType.Float or JTokenType.Integer => token.Value<decimal>(),
			JTokenType.String => ((string)token).AsDecimal(),
			_ => null,
		};
	}

	public static Sides? ToSideOrNull(this string side)
		=> side?.ToUpperInvariant() switch
		{
			"B" => Sides.Buy,
			"A" => Sides.Sell,
			_ => null,
		};

	public static Sides ToSide(this string side)
		=> side?.ToUpperInvariant() switch
		{
			"B" => Sides.Buy,
			"A" => Sides.Sell,
			_ => Sides.Buy,
		};

	public static OrderStates ToOrderState(this string status)
	{
		return status?.ToLowerInvariant() switch
		{
			"open" or "resting" or "triggered" => OrderStates.Active,
			"filled" or "canceled" or "cancelled" or "margincanceled" or "reduceonlycanceled" or "siblingfilled"
				or "vaultwithdrawalcanceled" or "openinterestcapcanceled" or "selftradecanceled" or "delistedcanceled"
				or "liquidatedcanceled" or "scheduledcancel" => OrderStates.Done,
			"rejected" => OrderStates.Failed,
			_ => OrderStates.Active,
		};
	}
}
