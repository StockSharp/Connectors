namespace StockSharp.Fireblocks.Native;

static class FireblocksExtensions
{
	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static string NormalizeFireblocksEndpoint(this string endpoint)
	{
		endpoint = endpoint?.Trim();
		if (endpoint.IsEmpty())
			return endpoint;
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "https://" + endpoint.TrimStart('/');
		var uri = new Uri(endpoint, UriKind.Absolute);
		if (uri.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException(
				"Fireblocks API endpoint must use HTTPS.", nameof(endpoint));
		return endpoint.TrimEnd('/');
	}

	public static DateTime ToFireblocksTime(this decimal? milliseconds,
		DateTime fallback)
	{
		if (milliseconds is not decimal value)
			return fallback.EnsureUtc();
		try
		{
			return DateTime.UnixEpoch.AddMilliseconds(decimal.ToInt64(value));
		}
		catch (Exception error) when (error is OverflowException or
			ArgumentOutOfRangeException)
		{
			return fallback.EnsureUtc();
		}
	}

	public static long ToFireblocksMilliseconds(this DateTime value)
		=> checked((long)(value.EnsureUtc() - DateTime.UnixEpoch)
			.TotalMilliseconds);

	public static decimal ParseFireblocksAmount(this string value)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result)
			? result
			: 0m;

	public static OrderStates ToOrderState(
		this FireblocksTransactionStatuses status)
		=> status switch
		{
			FireblocksTransactionStatuses.Completed or
			FireblocksTransactionStatuses.Confirmed or
			FireblocksTransactionStatuses.PartiallyCompleted or
			FireblocksTransactionStatuses.Cancelled => OrderStates.Done,
			FireblocksTransactionStatuses.Blocked or
			FireblocksTransactionStatuses.Rejected or
			FireblocksTransactionStatuses.Failed or
			FireblocksTransactionStatuses.Timeout => OrderStates.Failed,
			_ => OrderStates.Active,
		};

	public static bool IsFinal(this FireblocksTransactionStatuses status)
		=> status.ToOrderState() != OrderStates.Active;

	public static string GetPortfolioName(this FireblocksVaultAccount account)
		=> GetPortfolioName(account?.Id);

	public static string GetPortfolioName(string vaultId)
		=> "Fireblocks_VAULT_" + vaultId.ThrowIfEmpty(nameof(vaultId));
}
