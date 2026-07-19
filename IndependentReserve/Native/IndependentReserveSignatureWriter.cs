namespace StockSharp.IndependentReserve.Native;

static class IndependentReserveSignatureWriter
{
	public static string Create(Uri endpoint, string path, string secret,
		IndependentReservePrivateRequest request)
	{
		ArgumentNullException.ThrowIfNull(endpoint);
		ArgumentNullException.ThrowIfNull(request);
		secret = secret.ThrowIfEmpty(nameof(secret));

		var input = new StringBuilder(new Uri(endpoint,
			path.ThrowIfEmpty(nameof(path)).TrimStart('/')).ToString());
		Append(input, "apiKey", request.ApiKey);
		Append(input, "expiry", request.Expiry);

		switch (request)
		{
			case IndependentReserveAccountsRequest:
				break;
			case IndependentReserveOpenOrdersRequest value:
				Append(input, "primaryCurrencyCode", value.PrimaryCurrencyCode);
				Append(input, "secondaryCurrencyCode", value.SecondaryCurrencyCode);
				Append(input, "pageIndex", value.PageIndex);
				Append(input, "pageSize", value.PageSize);
				break;
			case IndependentReserveClosedOrdersRequest value:
				Append(input, "primaryCurrencyCode", value.PrimaryCurrencyCode);
				Append(input, "secondaryCurrencyCode", value.SecondaryCurrencyCode);
				Append(input, "pageIndex", value.PageIndex);
				Append(input, "pageSize", value.PageSize);
				Append(input, "includeTotals", value.IsIncludeTotals);
				Append(input, "fromTimestampUtc", value.FromTimestampUtc);
				break;
			case IndependentReserveOrderLookupRequest value:
				Append(input, "orderGuid", value.OrderGuid);
				Append(input, "clientId", value.ClientId);
				break;
			case IndependentReserveTradesRequest value:
				Append(input, "pageIndex", value.PageIndex);
				Append(input, "pageSize", value.PageSize);
				Append(input, "fromTimestampUtc", value.FromTimestampUtc);
				Append(input, "toTimestampUtc", value.ToTimestampUtc);
				Append(input, "includeTotals", value.IsIncludeTotals);
				break;
			case IndependentReserveTradesByOrderRequest value:
				Append(input, "orderGuid", value.OrderGuid);
				Append(input, "pageIndex", value.PageIndex);
				Append(input, "pageSize", value.PageSize);
				Append(input, "clientId", value.ClientId);
				break;
			case IndependentReserveLimitOrderRequest value:
				Append(input, "primaryCurrencyCode", value.PrimaryCurrencyCode);
				Append(input, "secondaryCurrencyCode", value.SecondaryCurrencyCode);
				Append(input, "orderType", value.OrderType);
				Append(input, "price", value.Price);
				Append(input, "volume", value.Volume);
				Append(input, "clientId", value.ClientId);
				Append(input, "timeInForce", value.TimeInForce);
				break;
			case IndependentReserveMarketOrderRequest value:
				Append(input, "primaryCurrencyCode", value.PrimaryCurrencyCode);
				Append(input, "secondaryCurrencyCode", value.SecondaryCurrencyCode);
				Append(input, "orderType", value.OrderType);
				Append(input, "volume", value.Volume);
				Append(input, "clientId", value.ClientId);
				Append(input, "allowedSlippagePercent",
					value.AllowedSlippagePercent);
				Append(input, "volumeCurrencyType", value.VolumeCurrencyType);
				break;
			case IndependentReserveCancelOrderRequest value:
				Append(input, "orderGuid", value.OrderGuid);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(request), request,
					"Unsupported Independent Reserve private request type.");
		}

		using var hmac = new HMACSHA256(Encoding.ASCII.GetBytes(secret));
		return Convert.ToHexString(hmac.ComputeHash(
			Encoding.ASCII.GetBytes(input.ToString())));
	}

	private static void Append(StringBuilder input, string name, string value)
	{
		if (value is null)
			return;
		input.Append(',').Append(name).Append('=').Append(
			value.Replace("\r", string.Empty, StringComparison.Ordinal)
				.Replace("\n", string.Empty, StringComparison.Ordinal));
	}

	private static void Append(StringBuilder input, string name, int value)
		=> Append(input, name, value.ToString(CultureInfo.InvariantCulture));

	private static void Append(StringBuilder input, string name, bool value)
		=> Append(input, name, value ? "true" : "false");

	private static void Append(StringBuilder input, string name, decimal value)
		=> Append(input, name, value.ToWire());

	private static void Append(StringBuilder input, string name, decimal? value)
	{
		if (value is not null)
			Append(input, name, value.Value);
	}

	private static void Append<TEnum>(StringBuilder input, string name,
		TEnum value)
		where TEnum : struct, Enum
		=> Append(input, name, value.ToString());
}
