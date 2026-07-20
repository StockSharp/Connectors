namespace StockSharp.VariationalOmni;

public partial class VariationalOmniMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		_ = Client;
		await EnsureFreshStatisticsAsync(cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedCode = lookupMsg.SecurityId.SecurityCode?.Trim();
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = Math.Max(0, lookupMsg.Count ?? long.MaxValue);
		foreach (var listing in GetListings().OrderBy(static item => item.Ticker,
			StringComparer.OrdinalIgnoreCase))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.VariationalOmni))
				continue;
			if (!requestedCode.IsEmpty() &&
				!requestedCode.EqualsIgnoreCase(listing.Ticker))
				continue;
			if (securityTypes.Count > 0 &&
				!securityTypes.Contains(SecurityTypes.Future))
				continue;
			var security = CreateSecurity(listing, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			if (left <= 0)
				break;
			await SendOutMessageAsync(security, cancellationToken);
			left--;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		_ = Client;
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"Variational Omni does not publish historical Level1 data.");

		await EnsureFreshStatisticsAsync(cancellationToken);
		var listing = GetListing(mdMsg.SecurityId);
		await SendLevel1Async(listing, mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = listing.Ticker.Trim();
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private static SecurityMessage CreateSecurity(
		VariationalOmniListing listing, long transactionId)
		=> new SecurityMessage
		{
			SecurityId = listing.ToStockSharp(),
			Name = listing.Name.IsEmpty()
				? listing.Ticker
				: listing.Name,
			ShortName = listing.Ticker,
			Class = "PERPETUAL",
			SecurityType = SecurityTypes.Future,
			Currency = CurrencyTypes.USD,
			OriginalTransactionId = transactionId,
		}.TryFillUnderlyingId(listing.Ticker);

	private ValueTask SendLevel1Async(VariationalOmniListing listing,
		long transactionId, CancellationToken cancellationToken)
	{
		if (listing is null || transactionId == 0)
			return default;
		var quote = listing.GetBestQuote();
		var serverTime = listing.Quotes?.UpdatedAt.ToVariationalOmniTime() ??
			DateTime.UtcNow;
		var bid = quote?.Bid.ToPositiveDecimal();
		var ask = quote?.Ask.ToPositiveDecimal();
		var message = new Level1ChangeMessage
		{
			SecurityId = listing.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.TheorPrice, listing.MarkPrice.ToPositiveDecimal())
		.TryAdd(Level1Fields.BestBidPrice, bid)
		.TryAdd(Level1Fields.BestAskPrice, ask)
		.TryAdd(Level1Fields.BestBidTime, bid is null ? null : serverTime)
		.TryAdd(Level1Fields.BestAskTime, ask is null ? null : serverTime)
		.TryAdd(Level1Fields.Turnover,
			listing.Volume24h.ToNonNegativeDecimal())
		.TryAdd(Level1Fields.OpenInterest, listing.OpenInterest?.GetTotal())
		.TryAdd(Level1Fields.State, SecurityStates.Trading);
		return SendOutMessageAsync(message, cancellationToken);
	}
}
