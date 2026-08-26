namespace StockSharp.Alpaca;

static class AlpacaExtensions
{
	public static string ToNative(this Sides side)
		=> side switch
		{
			Sides.Buy => "buy",
			Sides.Sell => "sell",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides? ToSide(this string side)
		=> side?.ToLowerInvariant() switch
		{
			"" or "-" => null,
			"b" or "buy" or "long" => Sides.Buy,
			"s" or "sell" or "short" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static string GetOrderType(this OrderRegisterMessage msg, AlpacaOrderCondition condition)
		=> msg.CheckOnNull(nameof(msg)).OrderType switch
		{
			null or OrderTypes.Limit => "limit",
			OrderTypes.Market => "market",
			OrderTypes.Conditional => condition?.Trail is not null ? "trailing_stop" : (msg.Price == default ? "stop" : "stop_limit"),
			_ => throw new ArgumentOutOfRangeException(nameof(msg), msg.OrderType, LocalizedStrings.InvalidValue),
		};

	public static OrderTypes? ToOrderType(this string type)
		=> type switch
		{
			null or "" => null,
			"limit" => OrderTypes.Limit,
			"market" => OrderTypes.Market,
			"trailing_stop" or "stop" or "stop_limit" => OrderTypes.Conditional,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};

	public static string GetTif(this OrderRegisterMessage msg)
	{
		return msg.TimeInForce switch
		{
			// TODO
			null or TimeInForce.PutInQueue => msg.TillDate is null ? "gtc" : "day",
			TimeInForce.CancelBalance => "ioc",
			TimeInForce.MatchOrCancel => "fok",
			_ => throw new ArgumentOutOfRangeException(nameof(msg), msg.TimeInForce, LocalizedStrings.InvalidValue),
		};
	}

	public static TimeInForce? ToTif(this string tif, out DateTime? till)
	{
		till = null;

		switch (tif)
		{
			case null:
			case "":
				return null;

			case "gtc":
			case "opg":
			case "cls":
				return TimeInForce.PutInQueue;

			case "day":
			{
				till = Messages.Extensions.Today;
				return TimeInForce.PutInQueue;
			}

			case "ioc":
				return TimeInForce.CancelBalance;

			case "fok":
				return TimeInForce.MatchOrCancel;

			default:
				throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue);
		}
	}

	private static readonly PairSet<string, AlpacaOrderClasses> _orderClasses = new(StringComparer.InvariantCultureIgnoreCase)
	{
		{ "simple", AlpacaOrderClasses.Simple },
		{ "bracket", AlpacaOrderClasses.Bracket },
		{ "oco", AlpacaOrderClasses.OneCancelsOther },
		{ "oto", AlpacaOrderClasses.OneTriggersOther },
	};

	public static string ToNative(this AlpacaOrderClasses orderClass)
		=> _orderClasses[orderClass];

	public static AlpacaOrderClasses ToOrderClass(this string orderClass)
		=> _orderClasses[orderClass];

	public static PairSet<TimeSpan, string> TimeFrames { get; } = new PairSet<TimeSpan, string>(EqualityComparer<TimeSpan>.Default, StringComparer.InvariantCultureIgnoreCase)
	{
		{ TimeSpan.FromMinutes(1), "1Min" },
		{ TimeSpan.FromMinutes(5), "5Min" },
		{ TimeSpan.FromMinutes(15), "15Min" },
		{ TimeSpan.FromMinutes(30), "30Min" },
		{ TimeSpan.FromHours(1), "1Hour" },
		{ TimeSpan.FromHours(4), "4Hour" },
		{ TimeSpan.FromDays(1), "1Day" },
		{ TimeSpan.FromDays(7), "1Week" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "1Month" },
	};

	public static string ToNative(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	public static TimeSpan ToTimeFrame(this string name)
		=> TimeFrames.TryGetKey2(name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);

	// https://docs.alpaca.markets/docs/orders-at-alpaca#order-lifecycle
	public static OrderStates ToOrderState(this string status)
		=> status?.ToLowerInvariant() switch
		{
			"accepted" or "new" or "partial_fill" or "partially_filled" or
			"done_for_day" or "pending_cancel" or "pending_replace" or
			"pending_new" or "accepted_for_bidding" or "calculated" or
			"stopped" or "suspended"
				=> OrderStates.Active,

			"filled" or "canceled" or "expired" or "replaced" => OrderStates.Done,
			"rejected" => OrderStates.Failed,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};

	public static bool IsCrypto(this Asset asset)
		=> asset.CheckOnNull(nameof(asset)).Class.EqualsIgnoreCase("crypto");

	public static SecurityId ToSecId(this Asset asset)
		=> new() { SecurityCode = asset.Symbol, BoardCode = asset.Exchange?.ToUpperInvariant() };

	/// <summary>
	/// The underlying an option symbol was written for.
	/// </summary>
	/// <remarks>
	/// A listed option is named for its underlying followed by the expiry, the right and the strike -
	/// NVDA260828C00050000 - so the underlying is everything before the first digit. A plain ticker is
	/// already its own underlying and comes back unchanged, which is what lets a caller name either.
	/// </remarks>
	public static string ToUnderlyingCode(this string symbol)
	{
		if (symbol.IsEmpty())
			return symbol;

		var end = 0;

		while (end < symbol.Length && !char.IsDigit(symbol[end]))
			end++;

		return end == 0 ? symbol : symbol[..end];
	}

	public static string ToNative(this OptionTypes type)
		=> type switch
		{
			OptionTypes.Call => "call",
			OptionTypes.Put => "put",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};

	public static OptionTypes ToOptionType(this string type)
		=> type?.ToLowerInvariant() switch
		{
			"call" or "c" => OptionTypes.Call,
			"put" or "p" => OptionTypes.Put,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};

	public static OptionStyles? ToOptionStyle(this string style)
		=> style?.ToLowerInvariant() switch
		{
			null or "" => null,
			"american" => OptionStyles.American,
			"european" => OptionStyles.European,
			_ => throw new ArgumentOutOfRangeException(nameof(style), style, LocalizedStrings.InvalidValue),
		};

	// A listed option is quoted on the consolidated tape rather than on any single exchange, and the
	// contract Alpaca returns names no exchange at all.
	public static SecurityId ToSecId(this OptionContract contract)
		=> new()
		{
			SecurityCode = contract.CheckOnNull(nameof(contract)).Symbol,
			BoardCode = BoardCodes.Opra,
		};

	public static SecurityMessage ToSecurityMessage(this OptionContract contract)
	{
		if (contract is null)
			throw new ArgumentNullException(nameof(contract));

		return new()
		{
			SecurityId = contract.ToSecId(),
			SecurityType = SecurityTypes.Option,
			Name = contract.Name,
			OptionType = contract.Type.ToOptionType(),
			OptionStyle = contract.Style.ToOptionStyle(),
			Strike = contract.StrikePrice,
			ExpiryDate = contract.ExpirationDate.UtcKind(),
			Multiplier = contract.Multiplier,

			// The symbol of the underlying is all the contract carries; which exchange lists it is not in
			// the payload, and guessing a board here would put a wrong one on every name that is not
			// listed where the guess assumed.
			UnderlyingSecurityId = new() { SecurityCode = contract.UnderlyingSymbol },
		};
	}
}