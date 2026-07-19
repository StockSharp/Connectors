namespace StockSharp.Bitso.Native;

static class BitsoExtensions
{
	public static string NormalizeBook(this string book)
		=> book.ThrowIfEmpty(nameof(book)).Trim()
			.Replace('/', '_').Replace('-', '_').ToLowerInvariant();

	public static SecurityId ToStockSharp(this string book)
		=> new()
		{
			SecurityCode = book.ThrowIfEmpty(nameof(book)).ToUpperInvariant(),
			BoardCode = BoardCodes.Bitso,
		};

	public static (string major, string minor) SplitBook(this string book)
	{
		book = book.NormalizeBook();
		var separator = book.IndexOf('_');
		if (separator <= 0 || separator + 1 >= book.Length)
			throw new FormatException($"Bitso book '{book}' is not in major_minor format.");
		return (book[..separator].ToUpperInvariant(),
			book[(separator + 1)..].ToUpperInvariant());
	}

	public static DateTime FromMilliseconds(this long value)
		=> DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;

	public static long ToMilliseconds(this DateTime value)
	{
		var utc = value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
			_ => value.ToUniversalTime(),
		};
		return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
	}

	public static DateTime ToUtcDateTime(this string value, DateTime fallback)
		=> DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
			out var timestamp)
			? timestamp.UtcDateTime
			: fallback;

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static Sides ToStockSharp(this BitsoSides side)
		=> side == BitsoSides.Buy ? Sides.Buy : Sides.Sell;

	public static BitsoSides ToBitso(this Sides side)
		=> side == Sides.Buy ? BitsoSides.Buy : BitsoSides.Sell;

	public static OrderTypes ToStockSharp(this BitsoOrderTypes type,
		bool isConditional)
		=> isConditional
			? OrderTypes.Conditional
			: type == BitsoOrderTypes.Market ? OrderTypes.Market : OrderTypes.Limit;

	public static OrderStates ToStockSharp(this BitsoOrderStatuses status)
		=> status switch
		{
			BitsoOrderStatuses.Queued or BitsoOrderStatuses.Open or
				BitsoOrderStatuses.PartiallyFilled => OrderStates.Active,
			BitsoOrderStatuses.Completed or BitsoOrderStatuses.Cancelled =>
				OrderStates.Done,
			_ => OrderStates.None,
		};

	public static BitsoTimeInForces ToBitso(this TimeInForce? timeInForce,
		bool isPostOnly)
		=> isPostOnly
			? BitsoTimeInForces.PostOnly
			: timeInForce switch
			{
				TimeInForce.CancelBalance => BitsoTimeInForces.ImmediateOrCancel,
				TimeInForce.MatchOrCancel => BitsoTimeInForces.FillOrKill,
				_ => BitsoTimeInForces.GoodTillCancelled,
			};

	public static TimeInForce? ToStockSharp(this BitsoTimeInForces? timeInForce)
		=> timeInForce switch
		{
			BitsoTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			BitsoTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			_ => null,
		};

	public static string CreateOriginId(long transactionId, string userOrderId)
	{
		var source = userOrderId.IsEmpty()
			? $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}"
			: userOrderId.Trim();
		var value = new string(source.Where(static character =>
			character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or
				'_' or '-').ToArray());
		if (value.IsEmpty())
			value = $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}";
		return value.Length <= 40 ? value : value[..40];
	}

	public static long ParseTransactionId(string originId)
		=> originId?.StartsWith("ss-", StringComparison.OrdinalIgnoreCase) == true &&
			long.TryParse(originId.AsSpan(3), NumberStyles.None,
				CultureInfo.InvariantCulture, out var transactionId)
				? transactionId
				: 0;
}
