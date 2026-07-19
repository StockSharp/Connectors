namespace StockSharp.BTSE.Native;

static class BTSEExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(6),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	public static string ToResolution(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "1"
			: timeFrame == TimeSpan.FromMinutes(5) ? "5"
			: timeFrame == TimeSpan.FromMinutes(15) ? "15"
			: timeFrame == TimeSpan.FromMinutes(30) ? "30"
			: timeFrame == TimeSpan.FromHours(1) ? "60"
			: timeFrame == TimeSpan.FromHours(4) ? "240"
			: timeFrame == TimeSpan.FromHours(6) ? "360"
			: timeFrame == TimeSpan.FromDays(1) ? "1440"
			: timeFrame == TimeSpan.FromDays(7) ? "10080"
			: timeFrame == TimeSpan.FromDays(30) ? "43200"
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported BTSE candle interval.");

	public static SecurityId ToStockSharp(this string symbol, BTSESections section)
		=> new()
		{
			SecurityCode = symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant(),
			BoardCode = section.ToBoardCode(),
		};

	public static string ToBoardCode(this BTSESections section) => section switch
	{
		BTSESections.Spot => BoardCodes.Btse,
		BTSESections.Futures => BoardCodes.BtseFutures,
		_ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
	};

	public static BTSESections ToSection(this string boardCode)
	{
		if (boardCode.EqualsIgnoreCase(BoardCodes.Btse))
			return BTSESections.Spot;
		if (boardCode.EqualsIgnoreCase(BoardCodes.BtseFutures))
			return BTSESections.Futures;
		throw new InvalidOperationException($"Unsupported BTSE board code '{boardCode}'.");
	}

	public static DateTime FromMilliseconds(this long value)
		=> DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;

	public static DateTime FromSeconds(this long value)
		=> DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime;

	public static long ToMilliseconds(this DateTime value)
		=> new DateTimeOffset(value.Kind == DateTimeKind.Utc
			? value
			: value.ToUniversalTime()).ToUnixTimeMilliseconds();

	public static Sides ToStockSharpSide(this string side)
	{
		side = side?.ToUpperInvariant();
		return side is "BUY" or "MODE_BUY" or "LONG" ? Sides.Buy : Sides.Sell;
	}

	public static BTSESides ToBTSE(this Sides side)
		=> side == Sides.Buy ? BTSESides.Buy : BTSESides.Sell;

	public static OrderTypes ToStockSharpOrderType(this int orderType)
		=> orderType == 77 ? OrderTypes.Market : OrderTypes.Limit;

	public static OrderStates ToStockSharpOrderState(this int? status,
		string state = null)
	{
		if (!state.IsEmpty())
		{
			if (state.ContainsIgnoreCase("ACTIVE") && !state.ContainsIgnoreCase("INACTIVE"))
				return OrderStates.Active;
			if (state.ContainsIgnoreCase("CANCEL") || state.ContainsIgnoreCase("FILLED") ||
				state.ContainsIgnoreCase("DONE"))
				return OrderStates.Done;
		}

		return status switch
		{
			2 or 3 or 5 or 9 or 10 => OrderStates.Active,
			4 or 6 or 7 => OrderStates.Done,
			1 or 8 or 11 or 12 or 13 or 15 or 16 or 17 or 41 or 101 or 300 or 301 or
				302 or 303 or 304 or 305 => OrderStates.Failed,
			_ => OrderStates.None,
		};
	}

	public static BTSETimeInForces ToBTSE(this TimeInForce? timeInForce)
		=> timeInForce switch
		{
			TimeInForce.CancelBalance => BTSETimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => BTSETimeInForces.FillOrKill,
			_ => BTSETimeInForces.GoodTillCancelled,
		};

	public static TimeInForce? ToStockSharpTimeInForce(this string timeInForce)
		=> timeInForce?.ToUpperInvariant() switch
		{
			"IOC" => TimeInForce.CancelBalance,
			"FOK" => TimeInForce.MatchOrCancel,
			_ => null,
		};

	public static string CreateClientOrderId(long transactionId, string userOrderId)
	{
		if (!userOrderId.IsEmpty())
			return userOrderId.Length <= 64 ? userOrderId : userOrderId[..64];
		return $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}";
	}

	public static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith("ss-", StringComparison.OrdinalIgnoreCase) == true &&
			long.TryParse(clientOrderId.AsSpan(3), NumberStyles.None,
				CultureInfo.InvariantCulture, out var transactionId)
				? transactionId
				: 0;
}
