namespace StockSharp.Anchorage;

public partial class AnchorageMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
			!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Anchorage))
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		await RefreshReferenceDataAsync(cancellationToken);
		var types = lookupMsg.GetSecurityTypes();
		var requestedCode = lookupMsg.SecurityId.SecurityCode?.Trim();
		var skip = (lookupMsg.Skip ?? 0).Max(0L);
		var left = (lookupMsg.Count ?? long.MaxValue).Max(0L);
		var securities = GetProducts().Select(product =>
			CreateSecurity(product, lookupMsg.TransactionId)).Concat(
				GetAssets().Select(asset => CreateSecurity(asset,
					lookupMsg.TransactionId)));

		foreach (var security in securities
			.Where(security => requestedCode.IsEmpty() ||
				security.SecurityId.SecurityCode.EqualsIgnoreCase(requestedCode) ||
				(security.SecurityId.Native as string).EqualsIgnoreCase(
					requestedCode))
			.GroupBy(static security => security.SecurityId.ToStringId(),
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static security => security.SecurityId.SecurityCode,
				StringComparer.OrdinalIgnoreCase))
		{
			if (!security.IsMatch(lookupMsg, types))
				continue;
			if (skip-- > 0)
				continue;
			if (left-- <= 0)
				break;
			await SendOutMessageAsync(security, cancellationToken);
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private AnchorageAssetType[] GetAssets()
	{
		using (_sync.EnterScope())
			return [.. _assets.Values.OrderBy(static asset => asset.AssetType,
				StringComparer.OrdinalIgnoreCase)];
	}

	private static SecurityMessage CreateSecurity(AnchorageTradePair product,
		long originalTransactionId)
	{
		ArgumentNullException.ThrowIfNull(product);
		var reference = product.Reference;
		var priceStep = reference?.PriceIncrement.ParseAnchorageAmount();
		var volumeStep = reference?.BaseSizeIncrement.ParseAnchorageAmount();
		var minimum = reference?.MinimumOrderSize.ParseAnchorageAmount();
		return new()
		{
			SecurityId = ToSecurityId(product.Pair),
			Name = product.Description.IsEmpty()
				? product.Pair
				: product.Description,
			ShortName = product.Pair,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = ToCurrency(reference?.QuoteAssetType),
			PriceStep = priceStep is > 0 ? priceStep : null,
			Decimals = priceStep is > 0 ? priceStep.Value.GetCachedDecimals() : null,
			VolumeStep = volumeStep is > 0 ? volumeStep : null,
			MinVolume = minimum is > 0 ? minimum : null,
			OriginalTransactionId = originalTransactionId,
		};
	}

	private static SecurityMessage CreateSecurity(AnchorageAssetType asset,
		long originalTransactionId)
	{
		ArgumentNullException.ThrowIfNull(asset);
		return new()
		{
			SecurityId = new()
			{
				SecurityCode = asset.AssetType,
				BoardCode = BoardCodes.Anchorage,
				Native = asset.AssetType,
			},
			Name = asset.Name.IsEmpty() ? asset.AssetType : asset.Name,
			ShortName = asset.AssetType,
			SecurityType = SecurityTypes.CryptoCurrency,
			VolumeStep = GetStep(asset.Decimals),
			OriginalTransactionId = originalTransactionId,
		};
	}

	private static decimal? GetStep(int? decimals)
	{
		if (decimals is not int count || count is < 0 or > 28)
			return null;
		var step = 1m;
		for (var index = 0; index < count; index++)
			step /= 10m;
		return step;
	}

	private static CurrencyTypes? ToCurrency(string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscriptionAsync(mdMsg, DataType.Level1,
			cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscriptionAsync(mdMsg, DataType.MarketDepth,
			cancellationToken);

	private async ValueTask ProcessMarketSubscriptionAsync(
		MarketDataMessage mdMsg, DataType dataType,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeMarketAsync(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"Anchorage exposes current market snapshots, not historical books.");

		var product = GetProduct(mdMsg.SecurityId);
		var depth = dataType == DataType.MarketDepth
			? (mdMsg.MaxDepth ?? MarketDepth).Max(1).Min(MarketDepth)
			: 1;
		var snapshot = await RestClient.GetMarketDataAsync(product.Pair,
			_marketDataAccountId, MarketDataSubaccount, depth, cancellationToken) ??
			throw new InvalidDataException(
				$"Anchorage returned no market snapshot for '{product.Pair}'.");
		await SendMarketDataAsync(product, snapshot.Bids, snapshot.Asks,
			snapshot.Timestamp, mdMsg.TransactionId, dataType, depth,
			cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var isFirst = false;
		using (_sync.EnterScope())
		{
			isFirst = !_marketSubscriptions.Values.Any(subscription =>
				subscription.Product.Pair.EqualsIgnoreCase(product.Pair));
			_marketSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Product = product,
				DataType = dataType,
				Depth = depth,
			});
		}
		try
		{
			if (isFirst && _socketClient is not null)
				await _socketClient.SubscribeMarketDataAsync(product.Pair,
					_marketDataAccountId, MarketDataSubaccount, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_marketSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask UnsubscribeMarketAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var isLast = false;
		using (_sync.EnterScope())
		{
			if (_marketSubscriptions.Remove(transactionId, out subscription))
				isLast = !_marketSubscriptions.Values.Any(value =>
					value.Product.Pair.EqualsIgnoreCase(subscription.Product.Pair));
		}
		if (subscription is not null && isLast && _socketClient is not null)
			await _socketClient.UnsubscribeMarketDataAsync(
				subscription.Product.Pair, cancellationToken);
	}

	private async ValueTask OnMarketDataReceivedAsync(
		AnchorageWebSocketMessage message, CancellationToken cancellationToken)
	{
		var payload = message?.Payload ?? throw new InvalidDataException(
			"Anchorage returned an empty market-data payload.");
		var product = GetProduct(payload.Symbol) ?? throw new InvalidDataException(
			$"Anchorage returned an unknown market symbol '{payload.Symbol}'.");
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _marketSubscriptions.Where(item =>
				item.Value.Product.Pair.EqualsIgnoreCase(product.Pair))];
		foreach (var (transactionId, subscription) in subscriptions)
			await SendMarketDataAsync(product, payload.Bids, payload.Asks,
				message.Timestamp, transactionId, subscription.DataType,
				subscription.Depth, cancellationToken);
	}

	private async ValueTask PollMarketDataAsync(
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _marketSubscriptions];
		foreach (var group in subscriptions.GroupBy(static item =>
			item.Value.Product.Pair, StringComparer.OrdinalIgnoreCase))
		{
			var depth = group.Max(static item => item.Value.Depth);
			var product = group.First().Value.Product;
			var snapshot = await RestClient.GetMarketDataAsync(product.Pair,
				_marketDataAccountId, MarketDataSubaccount, depth,
				cancellationToken);
			if (snapshot is null)
				continue;
			foreach (var (transactionId, subscription) in group)
				await SendMarketDataAsync(product, snapshot.Bids, snapshot.Asks,
					snapshot.Timestamp, transactionId, subscription.DataType,
					subscription.Depth, cancellationToken);
		}
	}

	private async ValueTask SendMarketDataAsync(AnchorageTradePair product,
		AnchoragePriceLevel[] bidLevels, AnchoragePriceLevel[] askLevels,
		string timestamp, long originalTransactionId, DataType dataType,
		int depth, CancellationToken cancellationToken)
	{
		var bids = ConvertLevels(bidLevels, true, depth);
		var asks = ConvertLevels(askLevels, false, depth);
		var serverTime = timestamp.ToAnchorageTime(CurrentTime.EnsureUtc());
		if (dataType == DataType.Level1)
		{
			var bid = bids.FirstOrDefault();
			var ask = asks.FirstOrDefault();
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = ToSecurityId(product.Pair),
				ServerTime = serverTime,
				OriginalTransactionId = originalTransactionId,
			}
			.TryAdd(Level1Fields.BestBidPrice,
				bid.Price > 0 ? bid.Price : null)
			.TryAdd(Level1Fields.BestBidVolume,
				bid.Volume > 0 ? bid.Volume : null)
			.TryAdd(Level1Fields.BestAskPrice,
				ask.Price > 0 ? ask.Price : null)
			.TryAdd(Level1Fields.BestAskVolume,
				ask.Volume > 0 ? ask.Volume : null), cancellationToken);
			return;
		}
		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = ToSecurityId(product.Pair),
			ServerTime = serverTime,
			OriginalTransactionId = originalTransactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);
	}

	private static QuoteChange[] ConvertLevels(AnchoragePriceLevel[] levels,
		bool isBid, int depth)
	{
		var result = new List<QuoteChange>();
		foreach (var level in levels ?? [])
		{
			if (level is null)
				continue;
			var price = level.Price.ParseAnchorageAmount();
			var volume = (level.Size.IsEmpty() ? level.Amount : level.Size)
				.ParseAnchorageAmount();
			if (price <= 0 || volume < 0)
				throw new InvalidDataException(
					"Anchorage returned an invalid order-book level.");
			if (volume > 0)
				result.Add(new(price, volume));
		}
		return [.. (isBid
			? result.OrderByDescending(static quote => quote.Price)
			: result.OrderBy(static quote => quote.Price)).Take(depth)];
	}

	private static SecurityId ToSecurityId(string symbol)
		=> new()
		{
			SecurityCode = symbol,
			BoardCode = BoardCodes.Anchorage,
			Native = symbol,
		};

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
