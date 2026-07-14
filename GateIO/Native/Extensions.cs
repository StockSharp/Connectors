namespace StockSharp.GateIO.Native;

using System.Security.Cryptography;

static class Extensions
{
	public static string ToNative(this Sides side)
	{
		return side switch
		{
			Sides.Buy => "buy",
			Sides.Sell => "sell",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToBidAsk(this Sides side)
	{
		return side switch
		{
			Sides.Buy => "bid",
			Sides.Sell => "ask",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static Sides ToSide(this string side)
	{
		return side?.ToLowerInvariant() switch
		{
			"buy" => Sides.Buy,
			"sell" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToNative(this OrderTypes? orderType, bool? postOnly)
	{
		if (postOnly == true)
			return "post_only";

		return orderType switch
		{
			null or OrderTypes.Limit => "limit",
			OrderTypes.Market => "market",
			OrderTypes.Conditional => "stop_limit",
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, LocalizedStrings.InvalidValue),
		};
	}

	public static OrderTypes? ToOrderType(this string type, out bool? postOnly)
	{
		postOnly = null;

		switch (type?.ToLowerInvariant())
		{
			case "limit":
				return OrderTypes.Limit;
			case "market":
				return OrderTypes.Market;
			case "post_only":
				postOnly = true;
				return OrderTypes.Limit;
			case "stop_limit":
				return OrderTypes.Conditional;
			default:
				throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
		}
	}

	public static string ToNative(this TimeInForce? tif, bool? postOnly)
	{
		if (postOnly == true)
			return "poc";

		return tif switch
		{
			TimeInForce.PutInQueue => "gtc",
			TimeInForce.MatchOrCancel => "fok",
			TimeInForce.CancelBalance => "ioc",
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};
	}

	public static TimeInForce? ToTimeInForce(this string tif, out bool? postOnly)
	{
		postOnly = null;

		if (tif == "poc")
		{
			postOnly = true;
			return null;
		}

		return tif?.ToLowerInvariant() switch
		{
			"gtc" => TimeInForce.PutInQueue,
			"fok" => TimeInForce.MatchOrCancel,
			"ioc" => TimeInForce.CancelBalance,
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};
	}

	public static OrderStates ToOrderState(this string status)
	{
		return status?.ToLowerInvariant() switch
		{
			"open" => OrderStates.Active,
			"closed" or "finished" or "cancelled" or "stp" or "ioc" => OrderStates.Done,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};
	}

	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromSeconds(10), "10s" },
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromHours(8), "8h" },
		{ TimeSpan.FromDays(1), "1d" },
		{ TimeSpan.FromDays(7), "7d" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "30d" },
	};

	public static string ToNative(this TimeSpan timeFrame)
	{
		return TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	public static TimeSpan ToTimeFrame(this string name)
	{
		return TimeFrames.TryGetKey2(name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);
	}

	public static string ToSymbol(this SecurityId securityId)
	{
		return securityId.SecurityCode.ToUpperInvariant();
	}

	public static SecurityId ToStockSharp(this string symbol, string boardCode)
	{
		return new()
		{
			SecurityCode = symbol.ToUpperInvariant(),
			BoardCode = boardCode,
		};
	}

	public static string ToRequestId(this long transId)
		=> $"t-{transId}";

	public static bool TryToTransId(this string requestId, out long transId)
		=> long.TryParse(requestId.Remove("t-"), out transId);

	public static RestRequest ApplySecret(this RestRequest request, Uri url, Authenticator authenticator)
	{
		if (request is null)		throw new ArgumentNullException(nameof(request));
		if (url is null)			throw new ArgumentNullException(nameof(url));
		if (authenticator is null)	throw new ArgumentNullException(nameof(authenticator));

		var timestamp = (long)DateTime.UtcNow.ToUnix();
		var method = request.Method.ToString().ToUpperInvariant();

		var queryString = request.Parameters
			.Where(p => p.Type == ParameterType.QueryString)
			.OrderBy(p => p.Name)
			.Select(p => $"{p.Name}={p.Value}")
			.JoinComma();

		var bodyString = request.Parameters
			.FirstOrDefault(p => p.Type == ParameterType.RequestBody)?.Value?.ToString() ?? string.Empty;

		static string sha512(string data)
		{
			using var hash = SHA512.Create();
			var buff = hash.ComputeHash(data.UTF8());
			return buff.Digest().ToLowerInvariant();
		}

		var payload = $"{method}\n{url.PathAndQuery}\n{queryString}\n{sha512(bodyString)}\n{timestamp}";
		var signature = authenticator.Sign(payload);
		request
			.AddHeader("KEY", authenticator.Key.UnSecure())
			.AddHeader("Timestamp", timestamp.ToString())
			.AddHeader("SIGN", signature);

		return request;
	}

	public static bool ToNative(this OrderPositionEffects effect)
		=> effect == OrderPositionEffects.CloseOnly;

	public static JsonSerializerSettings CreateJsonSettings()
		=> new()
		{
			NullValueHandling = NullValueHandling.Ignore,
		};

	public const string BrokerRefKey = "X-Gate-Channel-Id";
	public const string BrokerRefValue = "stocksh";

	public static RestRequest ApplyBrokerRef(this RestRequest request)
		=> request.AddHeader(BrokerRefKey, BrokerRefValue);
}