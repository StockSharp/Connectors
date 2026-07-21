namespace StockSharp.ZeroHash.Native;

static class ZeroHashExtensions
{
	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static CurrencyTypes? ToCurrencyType(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static SecurityId ToStockSharp(this ZeroHashInstrument instrument)
		=> new()
		{
			SecurityCode = instrument?.Symbol,
			BoardCode = BoardCodes.ZeroHash,
			Native = instrument?.Symbol,
		};

	public static Sides ToStockSharp(this ZeroHashSides side)
		=> side switch
		{
			ZeroHashSides.Buy => Sides.Buy,
			ZeroHashSides.Sell => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static ZeroHashSides ToZeroHash(this Sides side)
		=> side switch
		{
			Sides.Buy => ZeroHashSides.Buy,
			Sides.Sell => ZeroHashSides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static OrderTypes ToStockSharp(this ZeroHashOrderTypes type)
		=> type switch
		{
			ZeroHashOrderTypes.MarketToLimit => OrderTypes.Market,
			ZeroHashOrderTypes.Limit => OrderTypes.Limit,
			ZeroHashOrderTypes.Stop or ZeroHashOrderTypes.StopLimit =>
				OrderTypes.Conditional,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
		};

	public static OrderStates ToStockSharp(this ZeroHashOrderStates state)
		=> state switch
		{
			ZeroHashOrderStates.New or ZeroHashOrderStates.PartiallyFilled =>
				OrderStates.Active,
			ZeroHashOrderStates.Filled or ZeroHashOrderStates.Canceled or
			ZeroHashOrderStates.Replaced or ZeroHashOrderStates.Expired =>
				OrderStates.Done,
			ZeroHashOrderStates.Rejected => OrderStates.Failed,
			ZeroHashOrderStates.PendingNew or
			ZeroHashOrderStates.PendingReplace or
			ZeroHashOrderStates.PendingCancel or
			ZeroHashOrderStates.PendingRisk => OrderStates.Pending,
			_ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
		};

	public static TimeInForce? ToStockSharp(
		this ZeroHashTimeInForces? timeInForce)
		=> timeInForce switch
		{
			ZeroHashTimeInForces.GoodTillCanceled or
			ZeroHashTimeInForces.GoodTillTime => TimeInForce.PutInQueue,
			ZeroHashTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			ZeroHashTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			null => null,
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce),
				timeInForce, null),
		};

	public static decimal GetPriceScale(this ZeroHashInstrument instrument)
		=> ParsePositiveScale(instrument?.PriceScale, "price");

	public static decimal GetQuantityScale(this ZeroHashInstrument instrument)
		=> ParsePositiveScale(instrument?.FractionalQuantityScale, "quantity");

	public static string ScaleValue(decimal value, decimal scale,
		string fieldName)
	{
		if (value <= 0)
			throw new ArgumentOutOfRangeException(fieldName, value,
				fieldName + " must be positive.");
		if (scale <= 0)
			throw new ArgumentOutOfRangeException(nameof(scale));
		var scaled = decimal.Round(value * scale, 0,
			MidpointRounding.AwayFromZero);
		if (scaled <= 0)
			throw new ArgumentOutOfRangeException(fieldName, value,
				fieldName + " is below the Zero Hash scale increment.");
		return scaled.ToString("0", CultureInfo.InvariantCulture);
	}

	public static decimal? UnscaleValue(string value, decimal scale)
	{
		if (value.IsEmpty() || scale <= 0 || !decimal.TryParse(value,
			NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
			return null;
		return parsed / scale;
	}

	public static decimal? ToDecimalInvariant(this string value)
		=> decimal.TryParse(value, NumberStyles.Number,
			CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

	public static DateTime? TryParseZeroHashTime(this string value)
	{
		if (value.IsEmpty())
			return null;
		value = TrimFraction(value.Trim());
		return DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var parsed)
				? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
				: null;
	}

	public static string ToZeroHashTime(this DateTime value)
		=> value.EnsureUtc().ToString("O", CultureInfo.InvariantCulture);

	private static decimal ParsePositiveScale(string value, string name)
	{
		if (!decimal.TryParse(value, NumberStyles.Number,
			CultureInfo.InvariantCulture, out var scale) || scale <= 0)
			throw new InvalidDataException(
				"Zero Hash returned an invalid " + name + " scale '" + value + "'.");
		return scale;
	}

	private static string TrimFraction(string value)
	{
		var dot = value.IndexOf('.');
		if (dot < 0)
			return value;
		var end = value.IndexOfAny(['Z', 'z', '+', '-'], dot + 1);
		if (end < 0)
			end = value.Length;
		var count = end - dot - 1;
		return count <= 7 ? value : value.Remove(dot + 8, count - 7);
	}
}
