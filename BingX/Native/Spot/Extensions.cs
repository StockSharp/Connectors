namespace StockSharp.BingX.Native.Spot;

static class Extensions
{
	public static string ToNative(this Sides side)
	{
		return side switch
		{
			Sides.Buy => "BUY",
			Sides.Sell => "SELL",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static Sides ToSide(this string side)
	{
		return side?.ToUpperInvariant() switch
		{
			"BUY" => Sides.Buy,
			"SELL" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToNative(this OrderTypes? orderType)
	{
		return orderType switch
		{
			null or OrderTypes.Limit => "LIMIT",
			OrderTypes.Market => "MARKET",
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, LocalizedStrings.InvalidValue),
		};
	}

	public static OrderTypes? ToOrderType(this string type)
	{
		return type?.ToUpperInvariant() switch
		{
			null => null,
			"LIMIT" => OrderTypes.Limit,
			"MARKET" => OrderTypes.Market,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToNative(this TimeInForce? tif)
	{
		return tif switch
		{
			TimeInForce.PutInQueue => "GTC",
			TimeInForce.MatchOrCancel => "FOK",
			TimeInForce.CancelBalance => "IOC",
			_ => "GTC",
		};
	}

	public static TimeInForce? ToTimeInForce(this string tif)
	{
		return tif?.ToUpperInvariant() switch
		{
			"GTC" => TimeInForce.PutInQueue,
			"FOK" => TimeInForce.MatchOrCancel,
			"IOC" => TimeInForce.CancelBalance,
			_ => TimeInForce.PutInQueue,
		};
	}

	public static OrderStates ToOrderState(this string status)
	{
		return status?.ToUpperInvariant() switch
		{
			"NEW" => OrderStates.Active,
			"PENDING" => OrderStates.Active,
			"PARTIALLY_FILLED" => OrderStates.Active,
			"FILLED" => OrderStates.Done,
			"CANCELED" => OrderStates.Done,
			"CANCELLED" => OrderStates.Done,
			"PENDING_CANCEL" => OrderStates.Active,
			"REJECTED" => OrderStates.Failed,
			"EXPIRED" => OrderStates.Done,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToSymbol(this SecurityId securityId)
	{
		// Для спота используем формат с дефисом: BTC-USDT
		return securityId.SecurityCode;
	}

	public static SecurityId ToStockSharp(this string symbol, string boardCode)
	{
		return new()
		{
			SecurityCode = symbol,
			BoardCode = boardCode,
		};
	}

	public static string ToRequestId(this long transId)
		=> $"spot-{transId}";

	public static bool TryToTransId(this string requestId, out long transId)
	{
		transId = 0;
		if (requestId.IsEmpty() || !requestId.StartsWithIgnoreCase("spot-"))
			return false;
		
		return long.TryParse(requestId.Remove("spot-"), out transId);
	}

	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(3), "3m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(2), "2h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromHours(6), "6h" },
		{ TimeSpan.FromHours(8), "8h" },
		{ TimeSpan.FromHours(12), "12h" },
		{ TimeSpan.FromDays(1), "1d" },
		{ TimeSpan.FromDays(3), "3d" },
		{ TimeSpan.FromDays(7), "1w" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "1M" },
	};

	public static string ToNative(this TimeSpan timeFrame)
	{
		return TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	public static TimeSpan ToTimeFrame(this string name)
	{
		return TimeFrames.TryGetKey2(name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);
	}

	public static RestRequest ApplySecret(this RestRequest request, Uri url, Authenticator authenticator)
	{
		if (request is null)		throw new ArgumentNullException(nameof(request));
		if (url is null)			throw new ArgumentNullException(nameof(url));
		if (authenticator is null)	throw new ArgumentNullException(nameof(authenticator));

		var timestamp = (long)DateTime.UtcNow.ToUnix(false);

		var queryString = request.Parameters
			.Where(p => p.Type == ParameterType.QueryString)
			.OrderBy(p => p.Name)
			.Select(p => $"{p.Name}={p.Value}")
			.JoinAnd();

		var bodyString = request.Parameters
			.Where(p => p.Type == ParameterType.RequestBody)
			.OrderBy(p => p.Name)
			.Select(p => $"{p.Name}={p.Value}")
			.JoinAnd();

		// Для спота объединяем query и body параметры
		var allParams = new List<string>();
		if (!queryString.IsEmpty()) allParams.Add(queryString);
		if (!bodyString.IsEmpty()) allParams.Add(bodyString);
		allParams.Add($"timestamp={timestamp}");

		var data = allParams.JoinAnd();
		var signature = authenticator.Sign(data);

		request
			.AddHeader("X-BX-APIKEY", authenticator.Key.UnSecure())
			.AddParameter("timestamp", timestamp.ToString())
			.AddParameter("signature", signature);

		return request;
	}

	public static JsonSerializerSettings CreateJsonSettings()
		=> new()
		{
			NullValueHandling = NullValueHandling.Ignore,
			DateFormatHandling = DateFormatHandling.IsoDateFormat,
		};
}