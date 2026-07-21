namespace StockSharp.Copper.Native;

static class CopperExtensions
{
	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static string NormalizeCopperEndpoint(this string endpoint)
	{
		endpoint = endpoint?.Trim();
		if (endpoint.IsEmpty())
			return endpoint;
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "https://" + endpoint.TrimStart('/');
		var uri = new Uri(endpoint, UriKind.Absolute);
		if (uri.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException(
				"Copper API endpoint must use HTTPS.", nameof(endpoint));
		if (!uri.Query.IsEmpty() || !uri.Fragment.IsEmpty())
			throw new ArgumentException(
				"Copper API endpoint cannot contain a query or fragment.",
				nameof(endpoint));
		var path = uri.AbsolutePath.TrimEnd('/');
		if (!path.EndsWith("/platform", StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException(
				"Copper API endpoint must end in /platform.", nameof(endpoint));
		return endpoint.TrimEnd('/');
	}

	public static DateTime ToCopperTime(this string value, DateTime fallback)
	{
		if (decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var milliseconds))
		{
			try
			{
				return DateTime.UnixEpoch.AddMilliseconds(
					decimal.ToInt64(milliseconds));
			}
			catch (Exception error) when (error is OverflowException or
				ArgumentOutOfRangeException)
			{
			}
		}
		if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var parsed))
			return parsed.EnsureUtc();
		return fallback.EnsureUtc();
	}

	public static long ToCopperMilliseconds(this DateTime value)
		=> checked((long)(value.EnsureUtc() - DateTime.UnixEpoch)
			.TotalMilliseconds);

	public static decimal ParseCopperAmount(this string value)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result)
			? result
			: 0m;

	public static OrderStates ToOrderState(this CopperOrderStatuses status)
		=> status switch
		{
			CopperOrderStatuses.Executed or
			CopperOrderStatuses.Canceled or
			CopperOrderStatuses.Settled or
			CopperOrderStatuses.Liquidated => OrderStates.Done,
			CopperOrderStatuses.Rejected or
			CopperOrderStatuses.Declined or
			CopperOrderStatuses.Suspended or
			CopperOrderStatuses.Blocked or
			CopperOrderStatuses.Error => OrderStates.Failed,
			_ => OrderStates.Active,
		};

	public static bool IsFinal(this CopperOrderStatuses status)
		=> status.ToOrderState() != OrderStates.Active;

	public static Sides ToSide(this CopperOrderTypes type)
		=> type switch
		{
			CopperOrderTypes.Buy or
			CopperOrderTypes.Deposit or
			CopperOrderTypes.RetrievedDeposit or
			CopperOrderTypes.EarnReward or
			CopperOrderTypes.EarnSharedReward or
			CopperOrderTypes.ClaimSharedReward or
			CopperOrderTypes.CrossChainDeposit => Sides.Buy,
			_ => Sides.Sell,
		};

	public static string GetPortfolioName(string portfolioId)
		=> "Copper_" + portfolioId.ThrowIfEmpty(nameof(portfolioId));

	public static string GetClearLoopPortfolioName(string portfolioId,
		string clientAccountId)
		=> "Copper_CL_" + portfolioId.ThrowIfEmpty(nameof(portfolioId)) + "_" +
			clientAccountId.ThrowIfEmpty(nameof(clientAccountId));
}
