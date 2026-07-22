namespace StockSharp.Deriv.Native;

static class DerivExtensions
{
	private static readonly int[] _granularities =
	[
		60, 120, 180, 300, 600, 900, 1800, 3600, 7200, 14400, 28800, 86400,
	];

	public static readonly TimeSpan[] TimeFrames =
		[.. _granularities.Select(static seconds => TimeSpan.FromSeconds(seconds))];

	public static int ToDerivGranularity(this TimeSpan timeFrame)
	{
		var seconds = timeFrame.TotalSeconds;
		if (seconds != Math.Truncate(seconds) ||
			!_granularities.Contains(checked((int)seconds)))
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Deriv supports only its documented candle granularities.");
		return (int)seconds;
	}

	public static string ToNative<T>(this T value)
		where T : struct, Enum
	{
		var field = typeof(T).GetField(value.ToString());
		return field?.GetCustomAttribute<EnumMemberAttribute>()?.Value
			?? throw new ArgumentOutOfRangeException(nameof(value), value,
				$"No Deriv wire value is defined for {typeof(T).Name}.");
	}

	public static SecurityTypes ToSecurityType(this DerivActiveSymbol symbol)
	{
		if (symbol is null)
			throw new ArgumentNullException(nameof(symbol));

		return symbol.Market?.ToLowerInvariant() switch
		{
			"forex" => SecurityTypes.Currency,
			"cryptocurrency" => SecurityTypes.CryptoCurrency,
			"commodities" => SecurityTypes.Commodity,
			"indices" => SecurityTypes.Index,
			"synthetic_index" when symbol.SymbolType.EqualsIgnoreCase("commodity_basket")
				=> SecurityTypes.Commodity,
			"synthetic_index" when symbol.SymbolType.EqualsIgnoreCase("forex_basket")
				=> SecurityTypes.Currency,
			_ => SecurityTypes.Index,
		};
	}

	public static SecurityId ToSecurityId(this string symbol)
		=> new()
		{
			SecurityCode = symbol.ThrowIfEmpty(nameof(symbol)),
			BoardCode = BoardCodes.Deriv,
			Native = symbol,
		};

	public static DateTime FromDerivEpoch(this long value)
		=> value > 0
			? value.FromUnix()
			: DateTime.UtcNow;

	public static long ToDerivEpoch(this DateTime value)
		=> checked((long)value.ToUniversalTime().ToUnix());

	public static bool IsDownContract(this string contractType)
	{
		contractType = contractType?.ToUpperInvariant();
		return contractType is "LOWER" or "MULTDOWN" or "ASIAND" or "DIGITUNDER" or
			"PUT" or "PUTE" or "RESETPUT" or "RUNLOW" or "TICKLOW" or
			"VANILLALONGPUT" or "TURBOSSHORT";
	}

	public static JObject CreateProposalRequest(this DerivOrderCondition condition,
		string symbol, decimal amount)
	{
		if (condition is null)
			throw new ArgumentNullException(nameof(condition));
		if (condition.ContractType is null)
			throw new InvalidOperationException("A Deriv contract type is required.");
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount), amount,
				"Deriv contract amount must be positive.");

		var request = new JObject
		{
			["proposal"] = 1,
			["amount"] = amount,
			["basis"] = condition.Basis.ToNative(),
			["contract_type"] = condition.ContractType.Value.ToNative(),
			["currency"] = condition.Currency.ThrowIfEmpty(nameof(condition.Currency)),
			["underlying_symbol"] = symbol.ThrowIfEmpty(nameof(symbol)),
		};

		if (condition.ExpiryDate is DateTime expiry)
			request["date_expiry"] = expiry.ToDerivEpoch();
		else
		{
			if (condition.Duration <= 0)
				throw new InvalidOperationException("Deriv contract duration must be positive.");
			request["duration"] = condition.Duration;
			request["duration_unit"] = condition.DurationUnit.ToNative();
		}

		request.AddIfNotEmpty("barrier", condition.Barrier);
		request.AddIfNotEmpty("barrier2", condition.Barrier2);
		request.AddIfNotEmpty("cancellation", condition.Cancellation);
		request.AddIfValue("growth_rate", condition.GrowthRate);
		request.AddIfValue("multiplier", condition.Multiplier);
		request.AddIfValue("payout_per_point", condition.PayoutPerPoint);
		request.AddIfValue("selected_tick", condition.SelectedTick);

		if (condition.StopLoss is not null || condition.TakeProfit is not null)
		{
			var limit = new JObject();
			limit.AddIfValue("stop_loss", condition.StopLoss);
			limit.AddIfValue("take_profit", condition.TakeProfit);
			request["limit_order"] = limit;
		}

		return request;
	}

	private static void AddIfNotEmpty(this JObject target, string name, string value)
	{
		if (!value.IsEmpty())
			target[name] = value;
	}

	private static void AddIfValue<T>(this JObject target, string name, T? value)
		where T : struct
	{
		if (value is not null)
			target[name] = JToken.FromObject(value.Value);
	}
}
