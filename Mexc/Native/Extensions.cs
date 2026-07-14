namespace StockSharp.Mexc.Native;

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
			OrderTypes.Conditional => "STOP_LIMIT",
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, LocalizedStrings.InvalidValue),
		};
	}

	public static OrderTypes? ToOrderType(this string type)
	{
		return type?.ToUpperInvariant() switch
		{
			"LIMIT" => OrderTypes.Limit,
			"MARKET" => OrderTypes.Market,
			"STOP_LIMIT" => OrderTypes.Conditional,
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
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};
	}

	public static TimeInForce? ToTimeInForce(this string tif)
	{
		return tif?.ToUpperInvariant() switch
		{
			"GTC" => TimeInForce.PutInQueue,
			"FOK" => TimeInForce.MatchOrCancel,
			"IOC" => TimeInForce.CancelBalance,
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};
	}

	public static OrderStates ToOrderState(this string status)
	{
		return status?.ToUpperInvariant() switch
		{
			"NEW" or "PARTIALLY_FILLED" => OrderStates.Active,
			"FILLED" or "CANCELED" or "REJECTED" or "EXPIRED" => OrderStates.Done,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};
	}

	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromDays(1), "1d" },
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

	public static string ToSymbol(this SecurityId securityId)
	{
		return securityId.SecurityCode.ToUpperInvariant();
	}

	public static string ToFuturesWsSymbol(this SecurityId securityId)
		=> securityId.ToSymbol().ToFuturesWsSymbol();

	public static string ToFuturesWsSymbol(this string symbol)
	{
		if (symbol.IsEmpty())
			throw new ArgumentNullException(nameof(symbol));

		symbol = symbol.ToUpperInvariant();

		if (symbol.Contains('_'))
			return symbol;

		foreach (var quote in new[] { "USDT", "USDC", "BTC", "ETH", "USD" })
		{
			if (!symbol.EndsWith(quote, StringComparison.OrdinalIgnoreCase) || symbol.Length <= quote.Length)
				continue;

			return $"{symbol[..^quote.Length]}_{quote}";
		}

		return symbol;
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
		=> $"ss_{transId}";

	public static bool TryToTransId(this string requestId, out long transId)
		=> long.TryParse(requestId.Remove("ss_"), out transId);

	public static RestRequest ApplySecret(this RestRequest request, Uri url, Authenticator authenticator)
	{
		if (request is null) throw new ArgumentNullException(nameof(request));
		if (url is null) throw new ArgumentNullException(nameof(url));
		if (authenticator is null) throw new ArgumentNullException(nameof(authenticator));

		var timestamp = (long)DateTime.UtcNow.ToUnix(false);

		var queryString = request.Parameters
			.Where(p => p.Type == ParameterType.QueryString)
			.OrderBy(p => p.Name)
			.Select(p => $"{p.Name}={p.Value}")
			.JoinCommaSpace();

		if (!queryString.IsEmpty())
			queryString = $"&{queryString}";

		var body = request.Parameters
			.FirstOrDefault(p => p.Type == ParameterType.RequestBody)?.Value?.ToString() ?? string.Empty;

		var payload = $"{request.Method.ToString().ToUpperInvariant()}&{url.PathAndQuery}&timestamp={timestamp}{queryString}";
		
		if (!body.IsEmpty())
			payload += $"&{body}";

		var signature = authenticator.Sign(payload);
		
		request
			.AddQueryParameter("timestamp", timestamp.ToString())
			.AddQueryParameter("signature", signature)
			.AddHeader("X-MEXC-APIKEY", authenticator.Key.UnSecure());

		return request;
	}

	public static JsonSerializerSettings CreateJsonSettings()
		=> new()
		{
			NullValueHandling = NullValueHandling.Ignore,
		};
}