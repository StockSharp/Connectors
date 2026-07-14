namespace StockSharp.BingX;

public partial class BingXMessageAdapter
{
	private NativeAdapter EnsureGetAdapter(SecurityId secId)
		=> _adapters[secId.BoardCode];

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var adapter in _adapters.CachedValues)
		{
			if (left <= 0)
				break;

			if (secTypes.Count > 0 && !secTypes.Contains(adapter.SecType))
				continue;

			await foreach (var secMsg in adapter.SecurityLookup(lookupMsg, cancellationToken).WithEnforcedCancellation(cancellationToken))
			{
				if (!secMsg.IsMatch(lookupMsg, secTypes))
					continue;

				await SendOutMessageAsync(secMsg, cancellationToken);

				if (--left <= 0)
					break;
			}
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		
		var adapter = EnsureGetAdapter(mdMsg.SecurityId);
		await adapter.Level1(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var adapter = EnsureGetAdapter(mdMsg.SecurityId);
		await adapter.OrderBook(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var adapter = EnsureGetAdapter(mdMsg.SecurityId);
		await adapter.Ticks(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var adapter = EnsureGetAdapter(mdMsg.SecurityId);
		await adapter.TFCandles(mdMsg, cancellationToken);
	}
}
