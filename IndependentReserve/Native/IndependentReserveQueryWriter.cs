namespace StockSharp.IndependentReserve.Native;

static class IndependentReserveQueryWriter
{
	public static string Create(IndependentReserveMarketRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "primaryCurrencyCode", request.PrimaryCurrencyCode);
		Append(query, "secondaryCurrencyCode", request.SecondaryCurrencyCode);
		return query.ToString();
	}

	public static string Create(IndependentReserveRecentTradesRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder(Create((IndependentReserveMarketRequest)request));
		Append(query, "numberOfRecentTradesToRetrieve", request.Count);
		return query.ToString();
	}

	public static string Create(IndependentReserveHistoryRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder(Create((IndependentReserveMarketRequest)request));
		Append(query, "numberOfHoursInThePastToRetrieve", request.Hours);
		return query.ToString();
	}

	private static void Append(StringBuilder query, string name, string value)
	{
		if (value.IsEmpty())
			return;
		if (query.Length > 0)
			query.Append('&');
		query.Append(Uri.EscapeDataString(name));
		query.Append('=');
		query.Append(Uri.EscapeDataString(value));
	}

	private static void Append(StringBuilder query, string name, int value)
		=> Append(query, name, value.ToString(CultureInfo.InvariantCulture));
}
