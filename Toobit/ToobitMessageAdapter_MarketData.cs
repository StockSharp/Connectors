namespace StockSharp.Toobit;

public partial class ToobitMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();

		var securityTypes = lookupMsg.GetSecurityTypes();
		foreach (var adapter in _adapters.CachedValues)
		{
			if (securityTypes.Count == 0 || securityTypes.Contains(adapter.SecurityType))
				await adapter.SecurityLookupAsync(lookupMsg, cancellationToken);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		await GetAdapter(mdMsg.SecurityId).Level1SubscriptionAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		await GetAdapter(mdMsg.SecurityId).MarketDepthSubscriptionAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		await GetAdapter(mdMsg.SecurityId).TicksSubscriptionAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		await GetAdapter(mdMsg.SecurityId).TFCandlesSubscriptionAsync(mdMsg, cancellationToken);
	}
}
