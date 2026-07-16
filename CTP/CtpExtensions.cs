namespace StockSharp.Ctp;

internal static class CtpExtensions
{
	private static readonly TimeSpan _chinaOffset = TimeSpan.FromHours(8);

	public static SecurityId ToSecurityId(this string instrumentId, string exchangeId)
		=> new()
		{
			SecurityCode = instrumentId,
			BoardCode = exchangeId?.ToUpperInvariant() ?? string.Empty,
		};

	public static SecurityTypes? ToSecurityType(this int productClass)
		=> productClass switch
		{
			(int)CtpProductClasses.Futures or (int)CtpProductClasses.Combination or (int)CtpProductClasses.Efp or (int)CtpProductClasses.Tas => SecurityTypes.Future,
			(int)CtpProductClasses.Options or (int)CtpProductClasses.SpotOption => SecurityTypes.Option,
			(int)CtpProductClasses.Spot => SecurityTypes.Commodity,
			_ => null,
		};

	public static OptionTypes? ToOptionType(this int optionType)
		=> optionType switch
		{
			(int)CtpOptionTypes.Call => OptionTypes.Call,
			(int)CtpOptionTypes.Put => OptionTypes.Put,
			_ => null,
		};

	public static DateTime? ToCtpDate(this string value)
		=> DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
			? DateTime.SpecifyKind(date, DateTimeKind.Unspecified)
			: null;

	public static DateTime ToCtpTime(this string date, string time, int milliseconds = 0)
	{
		if (!DateTime.TryParseExact($"{date} {time}", "yyyyMMdd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
			return DateTime.UtcNow;
		local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified).AddMilliseconds(Math.Clamp(milliseconds, 0, 999));
		return new DateTimeOffset(local, _chinaOffset).UtcDateTime;
	}

	public static DateTime ToCtpTime(this CtpNativeDepth depth)
		=> (depth.ActionDay.IsEmpty() ? depth.TradingDay : depth.ActionDay).ToCtpTime(depth.UpdateTime, depth.UpdateMillisec);

	public static OrderStates ToOrderState(this CtpNativeOrder order)
	{
		if (order.SubmitStatus is (int)CtpOrderSubmitStatuses.InsertRejected or (int)CtpOrderSubmitStatuses.CancelRejected or (int)CtpOrderSubmitStatuses.ModifyRejected)
			return OrderStates.Failed;
		return order.OrderStatus switch
		{
			(int)CtpOrderStatuses.AllTraded or (int)CtpOrderStatuses.PartTradedNotQueueing or (int)CtpOrderStatuses.NoTradeNotQueueing or (int)CtpOrderStatuses.Canceled => OrderStates.Done,
			(int)CtpOrderStatuses.PartTradedQueueing or (int)CtpOrderStatuses.NoTradeQueueing or (int)CtpOrderStatuses.NotTouched or (int)CtpOrderStatuses.Touched => OrderStates.Active,
			_ => OrderStates.Pending,
		};
	}

	public static Sides ToSide(this int direction)
		=> direction == (int)CtpDirections.Sell ? Sides.Sell : Sides.Buy;

	public static CtpOrderCondition ToCondition(this CtpNativeOrder order)
		=> new()
		{
			PriceType = Enum.IsDefined((CtpOrderPriceTypes)order.PriceType) ? (CtpOrderPriceTypes)order.PriceType : null,
			Offset = Enum.IsDefined((CtpOffsetFlags)order.OffsetFlag) ? (CtpOffsetFlags)order.OffsetFlag : CtpOffsetFlags.Open,
			Hedge = Enum.IsDefined((CtpHedgeFlags)order.HedgeFlag) ? (CtpHedgeFlags)order.HedgeFlag : CtpHedgeFlags.Speculation,
			TimeCondition = Enum.IsDefined((CtpTimeConditions)order.TimeCondition) ? (CtpTimeConditions)order.TimeCondition : null,
			VolumeCondition = Enum.IsDefined((CtpVolumeConditions)order.VolumeCondition) ? (CtpVolumeConditions)order.VolumeCondition : null,
			ContingentCondition = Enum.IsDefined((CtpContingentConditions)order.ContingentCondition) ? (CtpContingentConditions)order.ContingentCondition : null,
			StopPrice = order.StopPrice > 0 ? (decimal)order.StopPrice : null,
		};

	public static Exception ToException(this CtpNativeError error, string operation)
	{
		var details = new[]
		{
			error.InstrumentId.IsEmpty() ? null : $"instrument {error.InstrumentId}",
			error.OrderRef.IsEmpty() ? null : $"order ref {error.OrderRef}",
			error.RequestId == 0 ? null : $"request {error.RequestId}",
		}.Where(static value => value != null).Join(", ");
		return new InvalidOperationException($"CTP {operation} error {error.Id}{(details.IsEmpty() ? string.Empty : $" ({details})")}: {error.Message}");
	}

	public static string GetKey(string instrumentId)
		=> instrumentId?.ToUpperInvariant() ?? string.Empty;
}
