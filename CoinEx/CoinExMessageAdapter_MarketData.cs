namespace StockSharp.CoinEx;

public partial class CoinExMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		if (left > 0 && _spot is NativeAdapter spot)
		{
			await foreach (var secMsg in spot.SecurityLookup(lookupMsg, cancellationToken).WithEnforcedCancellation(cancellationToken))
			{
				if (left > 0 && secMsg.IsMatch(lookupMsg, secTypes))
				{
					left--;
					await SendOutMessageAsync(secMsg, cancellationToken);
				}
			}
		}

		if (left > 0 && _futures is NativeAdapter fut)
		{
			await foreach (var secMsg in fut.SecurityLookup(lookupMsg, cancellationToken).WithEnforcedCancellation(cancellationToken))
			{
				if (left > 0 && secMsg.IsMatch(lookupMsg, secTypes))
				{
					left--;
					await SendOutMessageAsync(secMsg, cancellationToken);
				}
			}
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> GetAdapter(mdMsg.SecurityId).Level1(mdMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> GetAdapter(mdMsg.SecurityId).OrderBook(mdMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> GetAdapter(mdMsg.SecurityId).Ticks(mdMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> GetAdapter(mdMsg.SecurityId).TFCandles(mdMsg, cancellationToken);
}
