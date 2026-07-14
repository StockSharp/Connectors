namespace StockSharp.Bitget.Native.Spot;

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

	public static Sides ToSide(this string side)
	{
		return side?.ToLowerInvariant() switch
		{
			"buy" => Sides.Buy,
			"sell" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToNative(this OrderTypes? orderType)
	{
		return orderType switch
		{
			null or OrderTypes.Limit => "limit",
			OrderTypes.Market => "market",
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, LocalizedStrings.InvalidValue),
		};
	}

	public static OrderTypes ToOrderType(this string type)
	{
		return type?.ToLowerInvariant() switch
		{
			"limit" => OrderTypes.Limit,
			"market" => OrderTypes.Market,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToNative(this TimeInForce? tif)
	{
		return tif switch
		{
			TimeInForce.PutInQueue => "gtc",
			TimeInForce.MatchOrCancel => "fok",
			TimeInForce.CancelBalance => "ioc",
			_ => "gtc",
		};
	}

	public static TimeInForce ToTimeInForce(this string tif)
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
		return status?.ToLowerInvariant() switch
		{
			"new" or "live" or "partially_filled" => OrderStates.Active,
			"filled" or "cancelled" or "expired" => OrderStates.Done,
			_ => OrderStates.None,
		};
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

		var timestamp = ((long)DateTime.UtcNow.ToUnix(false)).To<string>();
		var method = request.Method.ToString().ToUpperInvariant();

		var queryString = request.Parameters
			.Where(p => p.Type == ParameterType.QueryString)
			.OrderBy(p => p.Name)
			.Select(p => $"{p.Name}={p.Value}")
			.JoinComma();

		var bodyString = request.Parameters
			.FirstOrDefault(p => p.Type == ParameterType.RequestBody)?.Value?.ToString() ?? string.Empty;

		var path = url.PathAndQuery;
		if (!queryString.IsEmpty())
			path += "?" + queryString;

		var payload = $"{timestamp}{method}{path}{bodyString}";
		var signature = authenticator.Sign(payload);

		request
			.AddHeader("ACCESS-KEY", authenticator.Key.UnSecure())
			.AddHeader("ACCESS-SIGN", signature)
			.AddHeader("ACCESS-TIMESTAMP", timestamp)
			.AddHeader("ACCESS-PASSPHRASE", authenticator.Passphrase.UnSecure())
			.AddHeader("Content-Type", "application/json");

		if (authenticator.IsDemo)
			request.AddHeader("paptrading", "1");

		return request;
	}

	public static decimal? GetPriceStep(this int? priceScale)
	{
		if (priceScale == null) return null;
		return (decimal)Math.Pow(10, -priceScale.Value);
	}
}
