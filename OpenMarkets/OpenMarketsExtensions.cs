namespace StockSharp.OpenMarkets;

static class OpenMarketsExtensions
{
	public const string DefaultExchange = BoardCodes.Asx;
	public const string DefaultDataSource = "TM";

	private static readonly TimeZoneInfo _exchangeTimeZone = GetExchangeTimeZone();

	public static string ToNativeSecurity(this SecurityId securityId, string dataSource,
		string defaultExchange)
	{
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId));
		var exchange = securityId.BoardCode.IsEmpty(defaultExchange).ThrowIfEmpty(nameof(defaultExchange));
		return $"{code}.{exchange}@{dataSource.ThrowIfEmpty(nameof(dataSource))}";
	}

	public static SecurityId ToSecurityId(this string code, string exchange)
		=> new()
		{
			SecurityCode = code,
			BoardCode = exchange.IsEmpty(DefaultExchange),
		};

	public static SecurityTypes? ToSecurityType(this string value)
		=> value?.Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant() switch
		{
			"equities" or "equity" or "ordinary" or "preference" or "stock" => SecurityTypes.Stock,
			"etf" or "exchangetradedfund" => SecurityTypes.Etf,
			"fund" or "managedfund" => SecurityTypes.Fund,
			"option" or "options" or "eto" => SecurityTypes.Option,
			"future" or "futures" => SecurityTypes.Future,
			"warrant" or "warrants" => SecurityTypes.Warrant,
			"bond" or "fixedinterest" => SecurityTypes.Bond,
			"index" => SecurityTypes.Index,
			_ => null,
		};

	public static Sides ToSide(this string value)
		=> value?.EqualsIgnoreCase("Buy") == true || value?.EqualsIgnoreCase("B") == true
			? Sides.Buy
			: Sides.Sell;

	public static string ToNativeSide(this Sides side, bool isShort)
		=> side == Sides.Buy ? "Buy" : isShort ? "Short" : "Sell";

	public static OrderTypes ToOrderType(this string value)
		=> value?.ContainsIgnoreCase("Market") == true ? OrderTypes.Market : OrderTypes.Limit;

	public static string ToPricingInstruction(this OrderTypes orderType)
		=> orderType == OrderTypes.Market ? "Market" : "Limit";

	public static OrderStates ToOrderState(this OpenMarketsOrder order)
		=> ToOrderState(order?.OrderState, order?.ActionStatus, order?.LastAction,
			order?.DoneVolumeTotal, order?.OrderVolume);

	public static OrderStates ToOrderState(this OpenMarketsStreamOrder order)
		=> ToOrderState(order?.OrderState, order?.ActionStatus, order?.LastAction,
			order?.DoneVolumeTotal, order?.OrderVolume);

	private static OrderStates ToOrderState(string state, string actionStatus, string lastAction,
		decimal? doneVolume, decimal? orderVolume)
	{
		if (actionStatus?.EqualsIgnoreCase("FAILED") == true ||
			actionStatus?.EqualsIgnoreCase("DENIED") == true)
			return OrderStates.Failed;
		if (state?.EqualsIgnoreCase("ACTIVE") == true)
			return OrderStates.Active;
		if (state?.EqualsIgnoreCase("INACTIVE") == true ||
			lastAction?.EqualsIgnoreCase("CANCEL") == true ||
			lastAction?.EqualsIgnoreCase("PURGE") == true ||
			(orderVolume > 0 && doneVolume >= orderVolume))
			return OrderStates.Done;
		return OrderStates.Pending;
	}

	public static string ToLifetime(this TimeInForce? timeInForce, DateTime? tillDate)
	{
		if (tillDate != null)
			return "Date";
		return timeInForce switch
		{
			TimeInForce.CancelBalance => "FillAndKill",
			TimeInForce.MatchOrCancel => "FillOrKill",
			_ => "EndOfDay",
		};
	}

	public static TimeInForce ToTimeInForce(this string lifetime)
		=> lifetime?.Replace(" ", string.Empty).ToLowerInvariant() switch
		{
			"fillandkill" => TimeInForce.CancelBalance,
			"fillorkill" => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static decimal NormalizeMultiplier(this decimal? multiplier, decimal fallback)
		=> multiplier is > 0 ? multiplier.Value : fallback;

	public static decimal? FromNativePrice(this decimal? price, decimal multiplier)
		=> price == null ? null : price.Value * multiplier;

	public static decimal ToNativePrice(this decimal price, decimal multiplier)
		=> price / multiplier;

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static DateTime ToExchangeUtc(this DateTime value)
		=> value.Kind == DateTimeKind.Unspecified
			? TimeZoneInfo.ConvertTimeToUtc(value, _exchangeTimeZone)
			: value.ToUniversalTime();

	public static DateTime ToExchangeTime(this DateTime value)
		=> TimeZoneInfo.ConvertTimeFromUtc(value.ToUtc(), _exchangeTimeZone);

	private static TimeZoneInfo GetExchangeTimeZone()
	{
		foreach (var id in new[] { "AUS Eastern Standard Time", "Australia/Sydney" })
		{
			try
			{
				return TimeZoneInfo.FindSystemTimeZoneById(id);
			}
			catch (TimeZoneNotFoundException)
			{
			}
			catch (InvalidTimeZoneException)
			{
			}
		}
		return TimeZoneInfo.Utc;
	}
}
