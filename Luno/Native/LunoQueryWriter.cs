namespace StockSharp.Luno.Native;

static class LunoQueryWriter
{
	public static string Create(LunoTickerRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "pair", request.Pair);
		return query.ToString();
	}

	public static string Create(LunoTradesRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "pair", request.Pair);
		Append(query, "since", request.Since);
		return query.ToString();
	}

	public static string Create(LunoCandlesRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "pair", request.Pair);
		Append(query, "since", request.Since);
		Append(query, "duration", request.Duration);
		return query.ToString();
	}

	public static string Create(LunoOrderListRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "pair", request.Pair);
		Append(query, "closed", request.IsClosed);
		Append(query, "created_before", request.CreatedBefore);
		Append(query, "limit", request.Limit);
		return query.ToString();
	}

	public static string Create(LunoOrderLookupRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "id", request.OrderId);
		Append(query, "client_order_id", request.ClientOrderId);
		return query.ToString();
	}

	public static string Create(LunoUserTradesRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "pair", request.Pair);
		Append(query, "since", request.Since);
		Append(query, "before", request.Before);
		Append(query, "after_seq", request.AfterSequence);
		Append(query, "before_seq", request.BeforeSequence);
		Append(query, "sort_desc", request.IsDescending);
		Append(query, "limit", request.Limit);
		return query.ToString();
	}

	public static string Create(LunoLimitOrderRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var form = new StringBuilder();
		Append(form, "pair", request.Pair);
		Append(form, "type", request.Side.ToWire());
		Append(form, "time_in_force", request.TimeInForce.ToWire());
		Append(form, "post_only", request.IsPostOnly);
		Append(form, "volume", request.Volume.ToWire());
		Append(form, "price", request.Price.ToWire());
		Append(form, "stop_price", request.StopPrice?.ToWire());
		Append(form, "stop_direction", request.StopDirection?.ToWire());
		Append(form, "timestamp", request.Timestamp);
		Append(form, "ttl", request.TimeToLive);
		Append(form, "client_order_id", request.ClientOrderId);
		return form.ToString();
	}

	public static string Create(LunoMarketOrderRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var form = new StringBuilder();
		Append(form, "pair", request.Pair);
		Append(form, "type", request.Side.ToWire());
		Append(form, "counter_volume", request.CounterVolume?.ToWire());
		Append(form, "base_volume", request.BaseVolume?.ToWire());
		Append(form, "timestamp", request.Timestamp);
		Append(form, "ttl", request.TimeToLive);
		Append(form, "client_order_id", request.ClientOrderId);
		return form.ToString();
	}

	public static string Create(LunoCancelOrderRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var form = new StringBuilder();
		Append(form, "order_id", request.OrderId);
		return form.ToString();
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

	private static void Append(StringBuilder query, string name, int value)
		=> Append(query, name, value.ToString(CultureInfo.InvariantCulture));

	private static void Append(StringBuilder query, string name, long? value)
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

	private static void Append(StringBuilder query, string name, bool value)
		=> Append(query, name, value ? "true" : "false");
}
