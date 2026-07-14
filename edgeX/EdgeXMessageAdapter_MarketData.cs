namespace StockSharp.EdgeX;

public partial class EdgeXMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		EnsureConnected();

		var secTypes = lookupMsg.GetSecurityTypes();

		foreach (var adapter in _adapters.CachedValues)
		{
			if (secTypes.Count > 0 && !secTypes.Contains(adapter.SecurityType))
				continue;

			await adapter.SecurityLookupAsync(lookupMsg, cancellationToken);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		
		EnsureConnected();
		await EnsureGetAdapter(mdMsg.SecurityId).Level1SubscriptionAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		
		EnsureConnected();
		await EnsureGetAdapter(mdMsg.SecurityId).TicksSubscriptionAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		
		EnsureConnected();
		await EnsureGetAdapter(mdMsg.SecurityId).MarketDepthSubscriptionAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		
		EnsureConnected();
		await EnsureGetAdapter(mdMsg.SecurityId).TFCandlesSubscriptionAsync(mdMsg, cancellationToken);
	}
}
