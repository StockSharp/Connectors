namespace StockSharp.Schwab;

static class SchwabExtensions
{
	public static OrderStates ToOrderState(this SchwabOrderStatuses? status)
		=> status switch
		{
			SchwabOrderStatuses.AwaitingParentOrder or SchwabOrderStatuses.AwaitingCondition or SchwabOrderStatuses.AwaitingStopCondition or SchwabOrderStatuses.AwaitingManualReview or SchwabOrderStatuses.Accepted or SchwabOrderStatuses.PendingActivation or SchwabOrderStatuses.PendingCancel or SchwabOrderStatuses.PendingReplace => OrderStates.Pending,
			SchwabOrderStatuses.Queued or SchwabOrderStatuses.Working or SchwabOrderStatuses.New => OrderStates.Active,
			SchwabOrderStatuses.Filled or SchwabOrderStatuses.Canceled or SchwabOrderStatuses.Expired or SchwabOrderStatuses.Replaced => OrderStates.Done,
			_ => OrderStates.Failed,
		};

	public static (string Type, int Value) ToSchwabFrequency(this TimeSpan timeFrame)
	{
		if (timeFrame == TimeSpan.FromDays(1))
			return ("daily", 1);

		var minutes = timeFrame.TotalMinutes;
		if (minutes is 1 or 5 or 10 or 15 or 30)
			return ("minute", (int)minutes);

		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	public static SchwabOrderRequest CreateOrderRequest(string symbol, Sides side, OrderTypes orderType, decimal volume, decimal price, TimeInForce? timeInForce)
	{
		if (symbol.IsEmpty())
			throw new ArgumentNullException(nameof(symbol));

		return new()
		{
			OrderType = orderType switch
			{
				OrderTypes.Market => SchwabOrderTypes.Market,
				OrderTypes.Limit => SchwabOrderTypes.Limit,
				_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, LocalizedStrings.InvalidValue),
			},
			Session = SchwabSessions.Normal,
			Duration = timeInForce switch
			{
				null => SchwabDurations.Day,
				TimeInForce.PutInQueue => SchwabDurations.GoodTillCancel,
				TimeInForce.MatchOrCancel => SchwabDurations.FillOrKill,
				TimeInForce.CancelBalance => SchwabDurations.ImmediateOrCancel,
				_ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, LocalizedStrings.InvalidValue),
			},
			StrategyType = SchwabOrderStrategies.Single,
			Price = orderType == OrderTypes.Limit ? price : null,
			Legs = [new()
			{
				Instruction = side == Sides.Buy ? SchwabInstructions.Buy : SchwabInstructions.Sell,
				Quantity = volume,
				Instrument = new() { Symbol = symbol, AssetType = SchwabAssetTypes.Equity },
			}],
		};
	}

	public static DateTime ToUtcDateTime(this DateTime? value)
	{
		if (value is null)
			return DateTime.UtcNow;

		return value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime();
	}
}
