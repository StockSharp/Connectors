namespace StockSharp.InteractiveBrokers;

static class ExtendedDataTypes
{
	public static readonly DataType Scanner = DataType.Create<ScannerResultMessage>(null).SetName(LocalizedStrings.Scanner).Immutable();
	public static readonly DataType FundamentalReport = DataType.Create<FundamentalReportMessage>(null, true).SetName(LocalizedStrings.Report).Immutable();
	public static readonly DataType OptionCalc = DataType.Create<OptionCalcMarketDataMessage>(null, true).SetName(LocalizedStrings.OptionCalc).Immutable();
	public static readonly DataType OptionParameters = DataType.Create<OptionParametersMessage>(null, true).SetName(LocalizedStrings.Options).Immutable();
	public static readonly DataType Histogram = DataType.Create<HistogramMessage>(null, true).SetName(LocalizedStrings.Histogram).Immutable();
	public static readonly DataType SoftDollarTier = DataType.Create<SoftDollarTierMessage>(null).SetName(LocalizedStrings.SoftDollarTier).Immutable();
	public static readonly DataType WshMetaData = DataType.Create<WshMetaDataMessage>(null, true).SetName(LocalizedStrings.WshMeta).Immutable();
	public static readonly DataType WshEventData = DataType.Create<WshEventDataMessage>(null, true).SetName(LocalizedStrings.WshEvent).Immutable();
}

static class ExtendedMessageTypes
{
	public const MessageTypes Scanner = (MessageTypes)(-4001);
	public const MessageTypes FundamentalReport = (MessageTypes)(-4002);
	public const MessageTypes ScannerParameters = (MessageTypes)(-4003);
	public const MessageTypes FinancialAdvise = (MessageTypes)(-4004);
	public const MessageTypes SoftDollarTier = (MessageTypes)(-4005);
	public const MessageTypes OptionParameters = (MessageTypes)(-4006);
	public const MessageTypes Histogram = (MessageTypes)(-4007);
	public const MessageTypes WshMetaData = (MessageTypes)(-4008);
	public const MessageTypes WshEventData = (MessageTypes)(-4009);
}

static class Extensions
{
	private static OrderStates ToOrderState(this OrderStatus status)
	{
		switch (status)
		{
			case OrderStatus.SentToServer:
			case OrderStatus.ReceiveByServer:
				return OrderStates.Pending;
			case OrderStatus.GateError:
				return OrderStates.Failed;
			case OrderStatus.SentToCanceled:
			case OrderStatus.Accepted:
				return OrderStates.Active;
			case OrderStatus.Cancelled:
			case OrderStatus.Matched:
				return OrderStates.Done;
			default:
				throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue);
		}
	}

	public static void FillStatus(this ExecutionMessage orderMsg, OrderStatus status)
	{
		if (orderMsg == null)
			throw new ArgumentNullException(nameof(orderMsg));

		orderMsg.OrderStatus = (int)status;
		orderMsg.OrderState = status.ToOrderState();

		if (status == OrderStatus.Matched)
			orderMsg.Balance = 0;
	}

	private static readonly PairSet<string, TimeSpan> _intervals = new(StringComparer.InvariantCultureIgnoreCase)
	{
		{ "1 secs", TimeSpan.FromSeconds(1) },
		{ "5 secs", TimeSpan.FromSeconds(5) },
		{ "15 secs", TimeSpan.FromSeconds(15) },
		{ "30 secs", TimeSpan.FromSeconds(30) },
		{ "1 min", TimeSpan.FromMinutes(1) },
		{ "2 mins", TimeSpan.FromMinutes(2) },
		{ "3 mins", TimeSpan.FromMinutes(3) },
		{ "5 mins", TimeSpan.FromMinutes(5) },
		{ "10 mins", TimeSpan.FromMinutes(10) },
		{ "15 mins", TimeSpan.FromMinutes(15) },
		{ "20 mins", TimeSpan.FromMinutes(20) },
		{ "30 mins", TimeSpan.FromMinutes(30) },
		{ "1 hour", TimeSpan.FromHours(1) },
		{ "2 hours", TimeSpan.FromHours(2) },
		{ "3 hours", TimeSpan.FromHours(3) },
		{ "4 hours", TimeSpan.FromHours(4) },
		{ "8 hours", TimeSpan.FromHours(8) },
		{ "1 day", TimeSpan.FromDays(1) },
		{ "1 week", TimeSpan.FromDays(7) },
		{ "1 month", TimeSpan.FromTicks(TimeHelper.TicksPerMonth) },
	};

	public static IEnumerable<TimeSpan> AllTimeFrames => [.. _intervals.Values];

	public static TimeSpan FromNative(this string interval)
	{
		if (_intervals.TryGetValue(interval, out var tf))
			return tf;

		throw new ArgumentOutOfRangeException(nameof(interval), interval, LocalizedStrings.InvalidValue);
	}

	public static string ToNative(this TimeSpan timeFrame)
	{
		if (_intervals.TryGetKey(timeFrame, out var interval))
			return interval;

		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	public static IDictionary<SecurityId, decimal> ToCombo(this SecurityMessage secMsg)
	{
		if (secMsg is null)
			throw new ArgumentNullException(nameof(secMsg));

		return secMsg.BasketExpression.SplitByRN().Select(l =>
		{
			var parts = l.SplitBySep("!!");
			return (parts[0].ToSecurityId(), parts[1].To<decimal>()).ToPair();
		}).ToDictionary();
	}

	public static bool IsCombo(this SecurityMessage secMsg)
	{
		if (secMsg is null)
			throw new ArgumentNullException(nameof(secMsg));

		return !secMsg.BasketExpression.IsEmpty();
	}
}