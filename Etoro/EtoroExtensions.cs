namespace StockSharp.Etoro;

static class EtoroExtensions
{
	public const string BoardCode = "ETORO";

	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
	];

	public static EtoroCandleIntervals ToNativeInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? EtoroCandleIntervals.OneMinute :
			timeFrame == TimeSpan.FromMinutes(5) ? EtoroCandleIntervals.FiveMinutes :
			timeFrame == TimeSpan.FromMinutes(10) ? EtoroCandleIntervals.TenMinutes :
			timeFrame == TimeSpan.FromMinutes(15) ? EtoroCandleIntervals.FifteenMinutes :
			timeFrame == TimeSpan.FromMinutes(30) ? EtoroCandleIntervals.ThirtyMinutes :
			timeFrame == TimeSpan.FromHours(1) ? EtoroCandleIntervals.OneHour :
			timeFrame == TimeSpan.FromHours(4) ? EtoroCandleIntervals.FourHours :
			timeFrame == TimeSpan.FromDays(1) ? EtoroCandleIntervals.OneDay :
			timeFrame == TimeSpan.FromDays(7) ? EtoroCandleIntervals.OneWeek :
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported eToro candle interval.");

	public static string ToNative(this EtoroCandleDirections direction)
		=> direction == EtoroCandleDirections.Asc ? "asc" : "desc";

	public static string ToNative(this EtoroCandleIntervals interval)
		=> interval.ToString();

	public static SecurityId ToSecurityId(this EtoroInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.InternalSymbolFull.IsEmpty(instrument.InstrumentId.ToString(CultureInfo.InvariantCulture)),
			BoardCode = BoardCode,
			Native = instrument.InstrumentId,
		};

	public static SecurityTypes ToSecurityType(this string value)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			return SecurityTypes.Cfd;
		if (value.ContainsIgnoreCase("etf"))
			return SecurityTypes.Etf;
		if (value.ContainsIgnoreCase("stock") || value.ContainsIgnoreCase("equity"))
			return SecurityTypes.Stock;
		if (value.ContainsIgnoreCase("crypto"))
			return SecurityTypes.CryptoCurrency;
		if (value.ContainsIgnoreCase("currency") || value.ContainsIgnoreCase("forex") || value.EqualsIgnoreCase("fx"))
			return SecurityTypes.Currency;
		if (value.ContainsIgnoreCase("future"))
			return SecurityTypes.Future;
		if (value.ContainsIgnoreCase("index"))
			return SecurityTypes.Index;
		if (value.ContainsIgnoreCase("commod"))
			return SecurityTypes.Commodity;
		return SecurityTypes.Cfd;
	}

	public static OrderStates ToOrderState(this EtoroOrderStatusIds status, int errorCode)
		=> errorCode != 0 ? OrderStates.Failed : status switch
		{
			EtoroOrderStatusIds.Filled or EtoroOrderStatusIds.Canceled or EtoroOrderStatusIds.Expired or
				EtoroOrderStatusIds.CanceledPartiallyFilled => OrderStates.Done,
			EtoroOrderStatusIds.Rejected or EtoroOrderStatusIds.RejectedPartiallyFilled => OrderStates.Failed,
			EtoroOrderStatusIds.Received or EtoroOrderStatusIds.Placed or EtoroOrderStatusIds.PartiallyFilled or
				EtoroOrderStatusIds.PendingCancel or EtoroOrderStatusIds.WaitingForMarket or
				EtoroOrderStatusIds.PendingTriggeredRate => OrderStates.Active,
			_ => OrderStates.Pending,
		};

	public static Sides ToSide(this EtoroTransactionTypes transaction)
		=> transaction is EtoroTransactionTypes.Buy or EtoroTransactionTypes.BuyToCover ? Sides.Buy : Sides.Sell;
}
