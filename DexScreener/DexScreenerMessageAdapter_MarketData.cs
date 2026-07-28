namespace StockSharp.DexScreener;

public partial class DexScreenerMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		if (securityTypes.Count > 0 &&
			!securityTypes.Contains(
				SecurityTypes.CryptoCurrency))
		{
			await SendSubscriptionResultAsync(
				lookupMsg, cancellationToken);
			return;
		}
		var requested =
			(lookupMsg.SecurityId.Native as string)
				.IsEmpty(lookupMsg.SecurityId.SecurityCode)
				.IsEmpty(lookupMsg.Name);
		var query = requested.IsEmpty()
			? SearchQuery
			: requested;
		var pairs = await RestClient.LookupAsync(
			ChainId,
			TokenAddress,
			query,
			cancellationToken);
		RememberPairs(pairs);
		var skip = Math.Max(0L, lookupMsg.Skip ?? 0);
		var left = Math.Min(
			lookupMsg.Count ?? MaximumItems,
			MaximumItems);
		foreach (var pair in pairs
			.Where(pair =>
				ChainId.IsEmpty() ||
				pair.ChainId.EqualsIgnoreCase(ChainId))
			.Where(pair => Matches(pair, requested))
			.OrderByDescending(static pair =>
				pair.LiquidityUsd ?? 0)
			.ThenBy(static pair =>
				pair.Symbol,
				StringComparer.OrdinalIgnoreCase))
		{
			var security = CreateSecurity(
				pair, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(
				security, cancellationToken);
			await SendLevel1Async(
				pair,
				pair,
				lookupMsg.TransactionId,
				cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(
			lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(
					mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"DEX Screener does not expose historical Level1 events.");
		var pair = await ResolvePairAsync(
			mdMsg.SecurityId, cancellationToken);
		var snapshot = await RestClient.GetPairAsync(
			pair.ChainId,
			pair.PairAddress,
			cancellationToken);
		if (snapshot is null)
			throw new InvalidDataException(
				$"DEX Screener returned no data for " +
					$"'{pair.PairAddress}'.");
		await SendLevel1Async(
			pair,
			snapshot,
			mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = new()
			{
				Pair = pair,
				LastUpdate = CurrentTime,
			};
		await SendSubscriptionResultAsync(
			mdMsg, cancellationToken);
	}

	private async ValueTask<DexScreenerPair> ResolvePairAsync(
		SecurityId securityId,
		CancellationToken cancellationToken)
	{
		try
		{
			return GetPair(securityId);
		}
		catch (InvalidOperationException)
		{
			RememberPairs(
				await RestClient.LookupAsync(
					ChainId,
					TokenAddress,
					securityId.SecurityCode.IsEmpty()
						? SearchQuery
						: securityId.SecurityCode,
					cancellationToken));
			return GetPair(securityId);
		}
	}

	private ValueTask SendLevel1Async(
		DexScreenerPair pair,
		DexScreenerPair snapshot,
		long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(
			new Level1ChangeMessage
			{
				SecurityId = pair.ToStockSharp(),
				ServerTime = CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(
				Level1Fields.LastTradePrice,
				PriceInUsd
					? snapshot.PriceUsd ??
						snapshot.PriceNative
					: snapshot.PriceNative ??
						snapshot.PriceUsd)
			.TryAdd(
				Level1Fields.Volume,
				snapshot.Volume24Hours)
			.TryAdd(
				Level1Fields.Change,
				snapshot.PriceChange24Hours)
			.TryAdd(
				Level1Fields.BidsVolume,
				snapshot.LiquidityBase)
			.TryAdd(
				Level1Fields.AsksVolume,
				snapshot.LiquidityQuote)
			.TryAdd(
				Level1Fields.State,
				SecurityStates.Trading),
			cancellationToken);

	private static SecurityMessage CreateSecurity(
		DexScreenerPair pair,
		long originalTransactionId)
		=> new()
		{
			SecurityId = pair.ToStockSharp(),
			Name =
				$"{pair.BaseName}/{pair.QuoteName}",
			ShortName =
				$"{pair.BaseSymbol}/{pair.QuoteSymbol}",
			Class = $"{pair.DexId}:{pair.ChainId}",
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = Enum.TryParse<CurrencyTypes>(
				pair.QuoteSymbol,
				true,
				out var currency)
					? currency
					: null,
			OriginalTransactionId = originalTransactionId,
		};

	private static bool Matches(
		DexScreenerPair pair,
		string requested)
		=> requested.IsEmpty() ||
			pair.NativeId.Contains(
				requested,
				StringComparison.OrdinalIgnoreCase) ||
			pair.PairAddress.Contains(
				requested,
				StringComparison.OrdinalIgnoreCase) ||
			pair.Symbol.Contains(
				requested,
				StringComparison.OrdinalIgnoreCase) ||
			pair.BaseAddress.EqualsIgnoreCase(requested) ||
			pair.QuoteAddress.EqualsIgnoreCase(requested) ||
			pair.BaseSymbol.EqualsIgnoreCase(requested) ||
			pair.QuoteSymbol.EqualsIgnoreCase(requested);

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}
}
