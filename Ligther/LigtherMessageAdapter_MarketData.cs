namespace StockSharp.Ligther;

public partial class LigtherMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		await NativeAdapters.Values.Select(a => a.SecurityLookupAsync(lookupMsg, cancellationToken)).WhenAll();
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		await EnsureGetAdapter(mdMsg.SecurityId).Level1SubscriptionAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		await EnsureGetAdapter(mdMsg.SecurityId).TicksSubscriptionAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		await EnsureGetAdapter(mdMsg.SecurityId).MarketDepthSubscriptionAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		await EnsureGetAdapter(mdMsg.SecurityId).TFCandlesSubscriptionAsync(mdMsg, cancellationToken);
	}
}
