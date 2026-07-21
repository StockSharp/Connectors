namespace StockSharp.BitGo.Native;

static class BitGoExtensions
{
	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static decimal? ToBitGoDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Number,
			CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static DateTime? ToBitGoTime(this string value)
		=> DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result)
			? DateTime.SpecifyKind(result, DateTimeKind.Utc)
			: null;

	public static string ToBitGoTime(this DateTime value)
		=> value.EnsureUtc().ToString("O", CultureInfo.InvariantCulture);

	public static string ToInvariant(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static BitGoSides ToBitGo(this Sides side)
		=> side == Sides.Buy ? BitGoSides.Buy : BitGoSides.Sell;

	public static Sides ToStockSharp(this BitGoSides side)
		=> side == BitGoSides.Buy ? Sides.Buy : Sides.Sell;

	public static OrderStates ToStockSharp(this BitGoOrderStatuses status)
		=> status switch
		{
			BitGoOrderStatuses.PendingOpen or
			BitGoOrderStatuses.PendingCancel or
			BitGoOrderStatuses.Scheduled => OrderStates.Pending,
			BitGoOrderStatuses.Open => OrderStates.Active,
			BitGoOrderStatuses.Completed or
			BitGoOrderStatuses.Canceled => OrderStates.Done,
			BitGoOrderStatuses.Error => OrderStates.Failed,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status,
				"Unknown BitGo order status."),
		};

	public static OrderTypes ToStockSharp(this BitGoOrderTypes type)
		=> type switch
		{
			BitGoOrderTypes.Market => OrderTypes.Market,
			BitGoOrderTypes.Limit => OrderTypes.Limit,
			BitGoOrderTypes.Stop => OrderTypes.Conditional,
			BitGoOrderTypes.Twap or BitGoOrderTypes.SteadyPace =>
				OrderTypes.Conditional,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type,
				"Unknown BitGo order type."),
		};

	public static TimeInForce? ToStockSharp(this BitGoTimeInForces? value)
		=> value switch
		{
			BitGoTimeInForces.GoodTillCanceled or
			BitGoTimeInForces.GoodTillDate => TimeInForce.PutInQueue,
			BitGoTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			BitGoTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			null => null,
			_ => throw new ArgumentOutOfRangeException(nameof(value), value,
				"Unknown BitGo time in force."),
		};

	public static string ToBitGoWire(this BitGoTimeInForces value)
		=> value switch
		{
			BitGoTimeInForces.GoodTillCanceled => "GTC",
			BitGoTimeInForces.ImmediateOrCancel => "IOC",
			BitGoTimeInForces.FillOrKill => "FOK",
			BitGoTimeInForces.GoodTillDate => "GTD",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value,
				"Unknown BitGo time in force."),
		};

	public static string ToBitGoWire(this BitGoOrderStatuses value)
		=> value switch
		{
			BitGoOrderStatuses.PendingOpen => "pending_open",
			BitGoOrderStatuses.Open => "open",
			BitGoOrderStatuses.Completed => "completed",
			BitGoOrderStatuses.PendingCancel => "pending_cancel",
			BitGoOrderStatuses.Canceled => "canceled",
			BitGoOrderStatuses.Error => "error",
			BitGoOrderStatuses.Scheduled => "scheduled",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value,
				"Unknown BitGo order status."),
		};

	public static string ToBitGoWire(this BitGoFundingTypes value)
		=> value switch
		{
			BitGoFundingTypes.Funded => "funded",
			BitGoFundingTypes.Margin => "margin",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value,
				"Unknown BitGo funding type."),
		};

	public static SecurityId ToStockSharp(this BitGoProduct product)
		=> new()
		{
			SecurityCode = product.Name.IsEmpty() ? product.Id : product.Name,
			BoardCode = BoardCodes.BitGo,
			Native = product.GetKey(),
		};
}
