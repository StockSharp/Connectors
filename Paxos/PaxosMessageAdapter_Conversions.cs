namespace StockSharp.Paxos;

using StockSharp.Paxos.Native.Model;

static class PaxosConversions
{
	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static string ToPaxosTime(this DateTime value)
		=> value.EnsureUtc().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
			CultureInfo.InvariantCulture);

	public static DateTime ToPaxosTime(this string value, DateTime fallback)
		=> DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result)
				? result.EnsureUtc()
				: fallback.EnsureUtc();

	public static decimal ParsePaxosAmount(this string value)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result)
				? result
				: 0m;

	public static string ToPaxosAmount(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static PaxosSides ToPaxos(this Sides side)
		=> side switch
		{
			Sides.Buy => PaxosSides.Buy,
			Sides.Sell => PaxosSides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static Sides ToStockSharp(this PaxosSides side)
		=> side switch
		{
			PaxosSides.Buy => Sides.Buy,
			PaxosSides.Sell => Sides.Sell,
			_ => throw new InvalidDataException(
				$"Unsupported Paxos side '{side}'."),
		};

	public static PaxosTimeInForces ToPaxos(this TimeInForce? timeInForce,
		bool isMarket)
		=> timeInForce switch
		{
			null => isMarket
				? PaxosTimeInForces.ImmediateOrCancel
				: PaxosTimeInForces.GoodTillCancel,
			TimeInForce.PutInQueue => PaxosTimeInForces.GoodTillCancel,
			TimeInForce.MatchOrCancel => PaxosTimeInForces.FillOrKill,
			TimeInForce.CancelBalance => PaxosTimeInForces.ImmediateOrCancel,
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce),
				timeInForce, null),
		};

	public static TimeInForce? ToStockSharp(this PaxosTimeInForces value)
		=> value switch
		{
			PaxosTimeInForces.GoodTillCancel => TimeInForce.PutInQueue,
			PaxosTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			PaxosTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			_ => null,
		};

	public static OrderStates ToOrderState(this PaxosOrderStatuses status)
		=> status switch
		{
			PaxosOrderStatuses.PendingSubmission or
			PaxosOrderStatuses.Submitted => OrderStates.Pending,
			PaxosOrderStatuses.Open => OrderStates.Active,
			PaxosOrderStatuses.Filled or PaxosOrderStatuses.Cancelled or
			PaxosOrderStatuses.Expired => OrderStates.Done,
			PaxosOrderStatuses.Rejected => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static OrderStates ToOrderState(this PaxosTransferStatuses status)
		=> status switch
		{
			PaxosTransferStatuses.Pending => OrderStates.Active,
			PaxosTransferStatuses.Completed => OrderStates.Done,
			PaxosTransferStatuses.Failed => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static OrderStates ToOrderState(this PaxosConversionStatuses status)
		=> status switch
		{
			PaxosConversionStatuses.Created => OrderStates.Active,
			PaxosConversionStatuses.Settled or
			PaxosConversionStatuses.Cancelled => OrderStates.Done,
			PaxosConversionStatuses.Failed => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static bool IsFinal(this PaxosOrderStatuses status)
		=> status is PaxosOrderStatuses.Filled or
			PaxosOrderStatuses.Cancelled or PaxosOrderStatuses.Rejected or
			PaxosOrderStatuses.Expired;

	public static bool IsFinal(this PaxosTransferStatuses status)
		=> status is PaxosTransferStatuses.Completed or
			PaxosTransferStatuses.Failed;

	public static bool IsFinal(this PaxosConversionStatuses status)
		=> status is PaxosConversionStatuses.Settled or
			PaxosConversionStatuses.Cancelled or PaxosConversionStatuses.Failed;

	public static PaxosCandleIncrements ToPaxosIncrement(this TimeSpan value)
	{
		if (value == TimeSpan.FromMinutes(1))
			return PaxosCandleIncrements.OneMinute;
		if (value == TimeSpan.FromMinutes(5))
			return PaxosCandleIncrements.FiveMinutes;
		if (value == TimeSpan.FromMinutes(15))
			return PaxosCandleIncrements.FifteenMinutes;
		if (value == TimeSpan.FromMinutes(30))
			return PaxosCandleIncrements.ThirtyMinutes;
		if (value == TimeSpan.FromHours(1))
			return PaxosCandleIncrements.OneHour;
		if (value == TimeSpan.FromHours(2))
			return PaxosCandleIncrements.TwoHours;
		if (value == TimeSpan.FromHours(12))
			return PaxosCandleIncrements.TwelveHours;
		if (value == TimeSpan.FromDays(1))
			return PaxosCandleIncrements.OneDay;
		if (value == TimeSpan.FromDays(7))
			return PaxosCandleIncrements.OneWeek;
		if (value == TimeSpan.FromDays(14))
			return PaxosCandleIncrements.TwoWeeks;
		if (value == TimeSpan.FromDays(28))
			return PaxosCandleIncrements.FourWeeks;
		throw new NotSupportedException(
			$"Paxos does not support the {value} candle time frame.");
	}

	public static OrderTypes ToOrderType(this PaxosOrderTypes value)
		=> value switch
		{
			PaxosOrderTypes.Market => OrderTypes.Market,
			PaxosOrderTypes.Limit or PaxosOrderTypes.PostOnlyLimit =>
				OrderTypes.Limit,
			PaxosOrderTypes.StopMarket or PaxosOrderTypes.StopLimit =>
				OrderTypes.Conditional,
			_ => OrderTypes.Conditional,
		};
}

public partial class PaxosMessageAdapter
{
	private static SecurityId ToSecurityId(string market)
		=> new()
		{
			SecurityCode = market,
			BoardCode = BoardCodes.Paxos,
			Native = market,
		};

	private static SecurityId ToAssetSecurityId(string asset)
		=> new()
		{
			SecurityCode = asset,
			BoardCode = BoardCodes.Paxos,
			Native = asset,
		};

	private static CurrencyTypes? ToCurrency(string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	private static string CreateRefId(long transactionId)
		=> "ssharp-" + transactionId.ToString(CultureInfo.InvariantCulture);

	private static long ParseTransactionId(string refId)
		=> !refId.IsEmpty() && refId.StartsWith("ssharp-",
			StringComparison.OrdinalIgnoreCase) && long.TryParse(refId[7..],
				NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
				? value
				: 0;
}
