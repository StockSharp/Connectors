namespace StockSharp.FalconX.Native;

static class FalconXExtensions
{
	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static string GetKey(this FalconXTokenPair pair)
	{
		ArgumentNullException.ThrowIfNull(pair);
		return pair.BaseToken.ThrowIfEmpty(nameof(pair.BaseToken)).Trim()
			.ToUpperInvariant() + "/" +
			pair.QuoteToken.ThrowIfEmpty(nameof(pair.QuoteToken)).Trim()
			.ToUpperInvariant();
	}

	public static SecurityId ToStockSharp(this FalconXTokenPair pair)
		=> new()
		{
			SecurityCode = pair.GetKey(),
			BoardCode = BoardCodes.FalconX,
			Native = pair.GetKey(),
		};

	public static FalconXTokenPair ParseFalconXPair(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var parts = value.Split(['/', '-', '_', ':'],
			StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length != 2)
			throw new FormatException(
				$"FalconX security code '{value}' must contain base and quote tokens.");
		return new()
		{
			BaseToken = parts[0].ToUpperInvariant(),
			QuoteToken = parts[1].ToUpperInvariant(),
		};
	}

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static FalconXSides ToFalconX(this Sides side)
		=> side switch
		{
			Sides.Buy => FalconXSides.Buy,
			Sides.Sell => FalconXSides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static Sides ToStockSharp(this FalconXSides side)
		=> side switch
		{
			FalconXSides.Buy => Sides.Buy,
			FalconXSides.Sell => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static OrderTypes ToStockSharp(this FalconXOrderTypes type)
		=> type == FalconXOrderTypes.Market ? OrderTypes.Market : OrderTypes.Limit;

	public static TimeInForce? ToStockSharp(this FalconXTimeInForces? value)
		=> value switch
		{
			FalconXTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			FalconXTimeInForces.GoodTillCanceled or
				FalconXTimeInForces.GoodTillExpiry => TimeInForce.PutInQueue,
			_ => null,
		};

	public static OrderStates ToStockSharp(this FalconXOrderStatuses status)
		=> status switch
		{
			FalconXOrderStatuses.Created or
				FalconXOrderStatuses.Open or
				FalconXOrderStatuses.OnHold or
				FalconXOrderStatuses.PartiallyFilled => OrderStates.Active,
			FalconXOrderStatuses.Rejected or
				FalconXOrderStatuses.Failure or
				FalconXOrderStatuses.PartiallyFilledAndRejected =>
				OrderStates.Failed,
			_ => OrderStates.Done,
		};

	public static DateTime? TryParseFalconXTime(this string value)
	{
		if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var time))
			return null;
		return DateTime.SpecifyKind(time, DateTimeKind.Utc);
	}

	public static DateTime FromFalconXMilliseconds(this long value)
	{
		try
		{
			return DateTime.UnixEpoch.AddMilliseconds(value);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				$"FalconX returned an invalid Unix timestamp '{value}'.", error);
		}
	}

	public static string ToFalconXTime(this DateTime value)
		=> value.EnsureUtc().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
			CultureInfo.InvariantCulture);

	public static string GetMessage(this FalconXApiError error)
	{
		if (error is null)
			return null;
		var details = error.Details.GetMessage();
		var message = !error.Reason.IsEmpty() ? error.Reason :
			!error.Message.IsEmpty() ? error.Message : details;
		var code = !error.Code.IsEmpty() ? error.Code : error.ErrorCode;
		return code.IsEmpty() ? message : message.IsEmpty() ? code :
			code + ": " + message;
	}
}
