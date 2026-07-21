namespace StockSharp.Anchorage.Native;

static class AnchorageExtensions
{
	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static DateTime ToAnchorageTime(this string value, DateTime fallback)
		=> DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var parsed)
			? parsed.EnsureUtc()
			: fallback.EnsureUtc();

	public static string ToAnchorageTime(this DateTime value)
		=> value.EnsureUtc().ToIso8601();

	public static string NormalizeAnchorageEndpoint(this string endpoint)
	{
		endpoint = endpoint?.Trim();
		if (endpoint.IsEmpty())
			return endpoint;
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "https://" + endpoint.TrimStart('/');
		var uri = new Uri(endpoint, UriKind.Absolute);
		if (uri.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException(
				"Anchorage REST endpoint must use HTTPS.", nameof(endpoint));
		if (!uri.Query.IsEmpty() || !uri.Fragment.IsEmpty())
			throw new ArgumentException(
				"Anchorage REST endpoint cannot contain a query or fragment.",
				nameof(endpoint));
		if (!uri.AbsolutePath.TrimEnd('/').EndsWith("/v2",
			StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException(
				"Anchorage REST endpoint must end in /v2.", nameof(endpoint));
		return endpoint.TrimEnd('/');
	}

	public static Uri NormalizeAnchorageSocketEndpoint(this string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme != "wss")
			throw new ArgumentException(
				"Anchorage WebSocket endpoint must use WSS.", nameof(endpoint));
		if (!uri.Query.IsEmpty() || !uri.Fragment.IsEmpty())
			throw new ArgumentException(
				"Anchorage WebSocket endpoint cannot contain a query or fragment.",
				nameof(endpoint));
		return uri;
	}

	public static decimal ParseAnchorageAmount(this string value)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result)
			? result
			: 0m;

	public static AnchorageSides ToAnchorage(this Sides side)
		=> side switch
		{
			Sides.Buy => AnchorageSides.Buy,
			Sides.Sell => AnchorageSides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static Sides ToStockSharp(this AnchorageSides side)
		=> side switch
		{
			AnchorageSides.Buy => Sides.Buy,
			AnchorageSides.Sell => Sides.Sell,
			_ => Sides.Buy,
		};

	public static AnchorageTimeInForces ToAnchorage(
		this TimeInForce? timeInForce)
		=> timeInForce switch
		{
			TimeInForce.MatchOrCancel => AnchorageTimeInForces.FillOrKill,
			TimeInForce.CancelBalance =>
				AnchorageTimeInForces.ImmediateOrCancel,
			null => AnchorageTimeInForces.GoodTillCancel,
			_ => throw new NotSupportedException(
				$"Anchorage does not support {timeInForce} time-in-force."),
		};

	public static OrderStates ToOrderState(this AnchorageOrderStatuses status)
		=> status switch
		{
			AnchorageOrderStatuses.Filled or
			AnchorageOrderStatuses.Canceled => OrderStates.Done,
			AnchorageOrderStatuses.Rejected => OrderStates.Failed,
			_ => OrderStates.Active,
		};

	public static bool IsFinal(this AnchorageOrderStatuses status)
		=> status.ToOrderState() != OrderStates.Active;

	public static OrderStates ToOrderState(
		this AnchorageTransferStatuses status)
		=> status switch
		{
			AnchorageTransferStatuses.Completed => OrderStates.Done,
			AnchorageTransferStatuses.Failed => OrderStates.Failed,
			_ => OrderStates.Active,
		};

	public static bool IsFinal(this AnchorageTransferStatuses status)
		=> status.ToOrderState() != OrderStates.Active;

	public static OrderStates ToOrderState(
		this AnchorageTransactionStatuses status)
		=> status switch
		{
			AnchorageTransactionStatuses.Success => OrderStates.Done,
			AnchorageTransactionStatuses.Failure or
			AnchorageTransactionStatuses.Rejected or
			AnchorageTransactionStatuses.Expired => OrderStates.Failed,
			_ => OrderStates.Active,
		};

	public static bool IsFinal(this AnchorageTransactionStatuses status)
		=> status.ToOrderState() != OrderStates.Active;

	public static string GetTradingPortfolioName(string accountId)
		=> "Anchorage_TRADE_" + accountId.ThrowIfEmpty(nameof(accountId));

	public static string GetVaultPortfolioName(string vaultId)
		=> "Anchorage_VAULT_" + vaultId.ThrowIfEmpty(nameof(vaultId));
}
