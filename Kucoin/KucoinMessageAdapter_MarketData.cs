namespace StockSharp.Kucoin;

public partial class KucoinMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		if (_spotAdapter is INativeAdapter spot)
		{
			await foreach (var secMsg in spot.SecurityLookupAsync(lookupMsg, cancellationToken).WithEnforcedCancellation(cancellationToken))
			{
				if (!secMsg.IsMatch(lookupMsg, secTypes))
					continue;

				await SendOutMessageAsync(secMsg, cancellationToken);

				if (--left <= 0)
					break;
			}
		}

		if (left > 0)
		{
			if (_futuresAdapter is INativeAdapter fut)
			{
				await foreach (var secMsg in fut.SecurityLookupAsync(lookupMsg, cancellationToken).WithEnforcedCancellation(cancellationToken))
				{
					if (!secMsg.IsMatch(lookupMsg, secTypes))
						continue;

					await SendOutMessageAsync(secMsg, cancellationToken);

					if (--left <= 0)
						break;
				}
			}
		}

		await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> GetAdapter(mdMsg).Level1SubscriptionAsync(mdMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> GetAdapter(mdMsg).TicksSubscriptionAsync(mdMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> GetAdapter(mdMsg).TFCandlesSubscriptionAsync(mdMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> GetAdapter(mdMsg).MarketDepthSubscriptionAsync(mdMsg, cancellationToken);
}
