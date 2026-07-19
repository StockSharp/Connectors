namespace StockSharp.VALR.Native;

static class VALRQueryWriter
{
	public static string Create(VALRTradesRequest request)
	{
		if (request is null)
			return null;
		var query = new StringBuilder();
		Append(query, "skip", request.Skip);
		Append(query, "limit", request.Limit);
		Append(query, "startTime", request.StartTime);
		Append(query, "endTime", request.EndTime);
		Append(query, "beforeId", request.BeforeId);
		return query.Length == 0 ? null : query.ToString();
	}

	public static string Create(VALRCandlesRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "periodSeconds", (int?)request.PeriodSeconds);
		Append(query, "startTime", request.StartTime);
		Append(query, "endTime", request.EndTime);
		Append(query, "limit", request.Limit);
		Append(query, "skip", request.Skip);
		Append(query, "includeEmpty", request.IsIncludeEmpty);
		return query.ToString();
	}

	public static string Create(VALROrderHistoryRequest request)
	{
		if (request is null)
			return null;
		var query = new StringBuilder();
		Append(query, "skip", request.Skip);
		Append(query, "limit", request.Limit);
		Append(query, "currencyPair", request.CurrencyPair);
		if (request.Statuses is { Length: > 0 })
			Append(query, "statuses", string.Join(",",
				request.Statuses.Select(static status => status.ToQueryValue())));
		Append(query, "startTime", request.StartTime);
		Append(query, "endTime", request.EndTime);
		Append(query, "excludeFailures", request.IsExcludeFailures);
		Append(query, "showZeroVolumeCancels",
			request.IsShowZeroVolumeCancels);
		return query.Length == 0 ? null : query.ToString();
	}

	public static string Create(VALRPositionRequest request)
	{
		if (request is null)
			return null;
		var query = new StringBuilder();
		Append(query, "currencyPair", request.CurrencyPair);
		Append(query, "skip", request.Skip);
		Append(query, "limit", request.Limit);
		return query.Length == 0 ? null : query.ToString();
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

	private static void Append(StringBuilder query, string name, int? value)
	{
		if (value is not null)
			Append(query, name, value.Value.ToString(CultureInfo.InvariantCulture));
	}

	private static void Append(StringBuilder query, string name, long value)
		=> Append(query, name, value.ToString(CultureInfo.InvariantCulture));

	private static void Append(StringBuilder query, string name, bool? value)
	{
		if (value is not null)
			Append(query, name, value.Value ? "true" : "false");
	}

	private static void Append(StringBuilder query, string name,
		DateTime? value)
	{
		if (value is not null)
			Append(query, name, value.Value.ToUniversalTime().ToString("O",
				CultureInfo.InvariantCulture));
	}
}
