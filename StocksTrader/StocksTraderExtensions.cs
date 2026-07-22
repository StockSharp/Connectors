namespace StockSharp.StocksTrader;

static class StocksTraderExtensions
{
	public const string BoardCode = BoardCodes.StocksTrader;

	public static SecurityId ToSecurityId(this string ticker)
		=> new()
		{
			SecurityCode = ticker.ThrowIfEmpty(nameof(ticker)),
			BoardCode = BoardCode,
			Native = ticker,
		};

	public static SecurityTypes? ToSecurityType(this StocksTraderInstrument instrument)
	{
		if (instrument is null)
			throw new ArgumentNullException(nameof(instrument));

		if (instrument.Units?.Contains("share", StringComparison.OrdinalIgnoreCase) == true)
			return SecurityTypes.Stock;
		if (instrument.Ticker?.Length == 6 &&
			instrument.Ticker.All(char.IsLetter) &&
			instrument.Description?.Contains('/') == true)
			return SecurityTypes.Currency;
		return null;
	}

	public static SecurityMessage ToSecurityMessage(this StocksTraderInstrument instrument,
		long originalTransactionId)
	{
		if (instrument is null)
			throw new ArgumentNullException(nameof(instrument));

		var securityType = instrument.ToSecurityType();
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = instrument.Ticker.ToSecurityId(),
			SecurityType = securityType,
			Name = instrument.Description.IsEmpty(instrument.Ticker),
			ShortName = instrument.Ticker,
			Class = instrument.Units,
			Currency = securityType == SecurityTypes.Currency
				? instrument.Ticker[3..].ToCurrency()
				: null,
			PriceStep = Positive(instrument.MinTick),
			VolumeStep = Positive(instrument.VolumeStep),
			MinVolume = Positive(instrument.MinVolume),
			MaxVolume = Positive(instrument.MaxVolume),
			Multiplier = Positive(instrument.ContractSize),
			Decimals = instrument.MinTick > 0
				? instrument.MinTick.GetCachedDecimals()
				: null,
		};
	}

	public static Sides ToSide(this string value)
		=> value.EqualsIgnoreCase("sell") ? Sides.Sell : Sides.Buy;

	public static string ToNative(this Sides side)
		=> side == Sides.Sell ? "sell" : "buy";

	public static OrderTypes ToOrderType(this string value)
		=> value?.ToLowerInvariant() switch
		{
			"market" or "stop_out" => OrderTypes.Market,
			"stop" or "stop_loss" or "take_profit" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static string ToNative(this OrderTypes orderType)
		=> orderType switch
		{
			OrderTypes.Market => "market",
			OrderTypes.Limit => "limit",
			OrderTypes.Conditional => "stop",
			_ => throw new NotSupportedException(
				$"StocksTrader does not support {orderType} orders."),
		};

	public static OrderStates ToOrderState(this string value)
		=> value?.ToLowerInvariant() switch
		{
			"active" => OrderStates.Active,
			"filled" or "canceled" => OrderStates.Done,
			"rejected" => OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static DateTime FromStocksTraderEpoch(this long? value)
		=> value is > 0 ? value.Value.FromUnix() : DateTime.UtcNow;

	public static long? ToNumericId(this string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var id) && id > 0 ? id : null;

	public static KeyValuePair<string, string>[] ToForm(
		this StocksTraderOrderRequest request)
	{
		if (request is null)
			throw new ArgumentNullException(nameof(request));

		var form = new List<KeyValuePair<string, string>>();
		form.AddIfValue("ticker", request.Ticker);
		form.AddIfValue("volume", request.Volume);
		form.AddIfValue("side", request.Side);
		form.AddIfValue("type", request.Type);
		form.AddIfValue("price", request.Price);
		form.AddIfValue("expiration", request.Expiration);
		form.AddIfValue("stop_loss", request.StopLoss);
		form.AddIfValue("take_profit", request.TakeProfit);
		return [.. form];
	}

	public static KeyValuePair<string, string>[] ToForm(
		this StocksTraderModifyOrderRequest request)
	{
		if (request is null)
			throw new ArgumentNullException(nameof(request));

		var form = new List<KeyValuePair<string, string>>();
		form.AddIfValue("volume", request.Volume);
		form.AddIfValue("price", request.Price);
		form.AddIfValue("expiration", request.Expiration);
		form.AddIfValue("stop_loss", request.StopLoss);
		form.AddIfValue("take_profit", request.TakeProfit);
		return [.. form];
	}

	public static KeyValuePair<string, string>[] ToForm(
		this StocksTraderModifyDealRequest request)
	{
		if (request is null)
			throw new ArgumentNullException(nameof(request));

		var form = new List<KeyValuePair<string, string>>();
		form.AddIfValue("stop_loss", request.StopLoss);
		form.AddIfValue("take_profit", request.TakeProfit);
		return [.. form];
	}

	private static decimal? Positive(decimal value) => value > 0 ? value : null;

	private static void AddIfValue(this ICollection<KeyValuePair<string, string>> form,
		string name, string value)
	{
		if (!value.IsEmpty())
			form.Add(new(name, value));
	}

	private static void AddIfValue<T>(this ICollection<KeyValuePair<string, string>> form,
		string name, T? value)
		where T : struct, IFormattable
	{
		if (value is not null)
			form.Add(new(name, value.Value.ToString(null, CultureInfo.InvariantCulture)));
	}
}
