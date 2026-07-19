namespace StockSharp.BitFlyer.Native;

static class BitFlyerExtensions
{
	public static string NormalizeProductCode(this string value)
		=> value.ThrowIfEmpty(nameof(value)).Trim().Replace('/', '_')
			.Replace('-', '_').ToUpperInvariant();

	public static string CompactProductCode(this string value)
		=> value.NormalizeProductCode().Replace("_", string.Empty);

	public static SecurityId ToStockSharp(this string productCode)
		=> new()
		{
			SecurityCode = productCode.NormalizeProductCode(),
			BoardCode = BoardCodes.BitFlyer,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static BitFlyerSides ToBitFlyer(this Sides side)
		=> side == Sides.Buy ? BitFlyerSides.Buy : BitFlyerSides.Sell;

	public static Sides ToStockSharp(this BitFlyerSides side)
		=> side == BitFlyerSides.Buy ? Sides.Buy : Sides.Sell;

	public static Sides? ToStockSharp(this BitFlyerSides? side)
		=> side?.ToStockSharp();

	public static BitFlyerTimeInForces ToBitFlyer(this TimeInForce? value)
		=> value switch
		{
			null or TimeInForce.PutInQueue =>
				BitFlyerTimeInForces.GoodTillCanceled,
			TimeInForce.CancelBalance =>
				BitFlyerTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => BitFlyerTimeInForces.FillOrKill,
			_ => throw new ArgumentOutOfRangeException(nameof(value), value,
				LocalizedStrings.InvalidValue),
		};

	public static TimeInForce? ToStockSharp(this BitFlyerTimeInForces? value)
		=> value switch
		{
			BitFlyerTimeInForces.GoodTillCanceled => TimeInForce.PutInQueue,
			BitFlyerTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			BitFlyerTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			_ => null,
		};

	public static OrderTypes ToStockSharp(this BitFlyerChildOrderTypes value)
		=> value == BitFlyerChildOrderTypes.Market
			? OrderTypes.Market
			: OrderTypes.Limit;

	public static OrderTypes ToStockSharp(this BitFlyerConditionTypes value)
		=> value is BitFlyerConditionTypes.Limit
			? OrderTypes.Limit
			: value is BitFlyerConditionTypes.Market
				? OrderTypes.Market
				: OrderTypes.Conditional;

	public static OrderStates ToStockSharp(this BitFlyerOrderStates value)
		=> value switch
		{
			BitFlyerOrderStates.Active => OrderStates.Active,
			BitFlyerOrderStates.Completed or BitFlyerOrderStates.Canceled or
				BitFlyerOrderStates.Expired => OrderStates.Done,
			BitFlyerOrderStates.Rejected => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static SecurityStates ToStockSharp(this BitFlyerMarketStates value)
		=> value is BitFlyerMarketStates.Running
			? SecurityStates.Trading
			: SecurityStates.Stoped;

	public static string ToWire(this BitFlyerOrderStates value)
		=> value switch
		{
			BitFlyerOrderStates.Active => "ACTIVE",
			BitFlyerOrderStates.Completed => "COMPLETED",
			BitFlyerOrderStates.Canceled => "CANCELED",
			BitFlyerOrderStates.Expired => "EXPIRED",
			BitFlyerOrderStates.Rejected => "REJECTED",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value,
				LocalizedStrings.InvalidValue),
		};

	public static DateTime ToUtcDateTime(this string value, DateTime fallback)
	{
		if (!value.IsEmpty() && DateTimeOffset.TryParse(value,
			CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal |
			DateTimeStyles.AdjustToUniversal, out var timestamp))
			return timestamp.UtcDateTime;
		return fallback.Kind switch
		{
			DateTimeKind.Utc => fallback,
			DateTimeKind.Unspecified => DateTime.SpecifyKind(fallback,
				DateTimeKind.Utc),
			_ => fallback.ToUniversalTime(),
		};
	}

}
