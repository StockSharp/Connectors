namespace StockSharp.Bmll;

static class BmllExtensions
{
	public const string BoardCode = "BMLL";

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static bool TryGetTime(this BmllMarketDataRecord record,
		out DateTime result)
	{
		foreach (var nanoseconds in new[]
		{
			record.TradeTimestampNanoseconds,
			record.PublicationTimestampNanoseconds,
			record.TimestampNanoseconds,
			record.ReceiveTimestampNanoseconds,
		})
		{
			if (nanoseconds is > 0 && TryFromNanoseconds(nanoseconds.Value, out result))
				return true;
		}

		foreach (var value in new[] { record.TradeTimestamp, record.PublicationTimestamp })
		{
			if (TryParseTimestamp(value, out result))
				return true;
		}

		if (!record.TradeDate.IsEmpty() &&
			DateTime.TryParseExact(record.TradeDate, "yyyy-MM-dd",
				CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
		{
			result = DateTime.SpecifyKind(date, DateTimeKind.Utc);
			return true;
		}

		result = default;
		return false;
	}

	public static SecurityId GetSecurityId(this BmllMarketDataRecord record,
		SecurityId requested)
	{
		var code = record.ExchangeTicker.IsEmpty(record.Ticker)
			.IsEmpty(requested.SecurityCode);
		var board = record.Mic.IsEmpty(record.IsoExchangeCode)
			.IsEmpty(record.ExecutionVenue);
		if (board.IsEmpty() || board.EqualsIgnoreCase(BoardCode))
			board = requested.BoardCode.IsEmpty(BoardCode);
		return new()
		{
			SecurityCode = code,
			BoardCode = board,
			Native = record.ListingId ?? record.BmllObjectId,
		};
	}

	public static Sides? ToSide(this BmllMarketDataRecord record)
		=> record.Side switch
		{
			BmllSides.Bid => Sides.Buy,
			BmllSides.Ask => Sides.Sell,
			_ => null,
		};

	public static Sides? ToAggressorSide(this BmllMarketDataRecord record)
		=> record.AggressorSide switch
		{
			BmllAggressorSides.Buy => Sides.Buy,
			BmllAggressorSides.Sell => Sides.Sell,
			_ => null,
		};

	public static long GetSequence(this BmllMarketDataRecord record)
		=> record.BmllSequenceNo ?? record.SequenceNo ??
			record.ExchangeSequenceNo ?? record.EventNo ?? 0;

	public static string GetOrderId(this BmllMarketDataRecord record)
		=> record.OrderId.IsEmpty(record.OldOrderId).IsEmpty(record.OriginalOrderId);

	public static OrderStates GetOrderState(this BmllMarketDataRecord record)
		=> record.LobAction == BmllLobActions.Remove
			? OrderStates.Done : OrderStates.Active;

	private static bool TryFromNanoseconds(long value, out DateTime result)
	{
		try
		{
			result = DateTime.UnixEpoch.AddTicks(value / 100);
			return true;
		}
		catch (ArgumentOutOfRangeException)
		{
			result = default;
			return false;
		}
	}

	private static bool TryParseTimestamp(string value, out DateTime result)
	{
		result = default;
		if (value.IsEmpty())
			return false;
		if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var numeric))
		{
			return TryFromEpochValue(numeric, out result);
		}
		if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
			out var timestamp))
		{
			result = timestamp.UtcDateTime;
			return true;
		}

		value = value.Trim();
		if (value.Length >= 18 && value[8] == '-' && value[14] == '.')
		{
			var normalized = value[..14] + ":" + value[15..];
			var fraction = normalized.IndexOf('.', 17);
			if (fraction >= 0 && normalized.Length - fraction - 1 > 7)
				normalized = normalized[..(fraction + 8)];
			if (DateTime.TryParseExact(normalized, "yyyyMMdd-HH:mm:ss.FFFFFFF",
				CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
			{
				result = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
				return true;
			}
		}
		return false;
	}

	private static bool TryFromEpochValue(long value, out DateTime result)
	{
		try
		{
			var absolute = value == long.MinValue ? long.MaxValue : Math.Abs(value);
			var ticks = absolute switch
			{
				>= 100_000_000_000_000_000 => value / 100,
				>= 100_000_000_000_000 => checked(value * 10),
				>= 100_000_000_000 => checked(value * TimeSpan.TicksPerMillisecond),
				_ => checked(value * TimeSpan.TicksPerSecond),
			};
			result = DateTime.UnixEpoch.AddTicks(ticks);
			return true;
		}
		catch (ArgumentOutOfRangeException)
		{
			result = default;
			return false;
		}
		catch (OverflowException)
		{
			result = default;
			return false;
		}
	}
}
