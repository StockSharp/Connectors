namespace StockSharp.Avantis;

public partial class AvantisMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = Math.Max(0, lookupMsg.Count ?? long.MaxValue);
		foreach (var market in GetMarkets().OrderBy(static item => item.Symbol,
			StringComparer.Ordinal))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Avantis))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.Equals(market.Symbol,
					StringComparison.OrdinalIgnoreCase))
				continue;
			if (securityTypes.Count > 0 &&
				!securityTypes.Contains(SecurityTypes.Future))
				continue;
			var security = CreateSecurity(market, lookupMsg.TransactionId);
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
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeLevel1Async(mdMsg.OriginalTransactionId,
				cancellationToken);
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
				"Avantis does not publish historical Level1 changes.");

		var market = GetMarket(mdMsg.SecurityId);
		var snapshot = await GetPriceSnapshotAsync(market, cancellationToken);
		await SendLevel1Async(market, snapshot, mdMsg.TransactionId,
			cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var isFirst = false;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, market.PairIndex);
			_feedReferences.TryGetValue(market.PairIndex, out var references);
			_feedReferences[market.PairIndex] = references + 1;
			isFirst = references == 0;
		}
		if (!isFirst)
			return;
		try
		{
			await FeedClient.SubscribeAsync(market, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_level1Subscriptions.Remove(mdMsg.TransactionId);
				_feedReferences.Remove(market.PairIndex);
			}
			throw;
		}
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		var pairIndex = -1;
		var isLast = false;
		using (_sync.EnterScope())
		{
			if (!_level1Subscriptions.Remove(transactionId, out pairIndex))
				return;
			if (!_feedReferences.TryGetValue(pairIndex, out var references) ||
				references <= 1)
			{
				_feedReferences.Remove(pairIndex);
				isLast = true;
			}
			else
				_feedReferences[pairIndex] = references - 1;
		}
		if (isLast)
			await FeedClient.UnsubscribeAsync(pairIndex, cancellationToken);
	}

	private async ValueTask<AvantisPriceUpdate> GetPriceSnapshotAsync(
		AvantisMarket market, CancellationToken cancellationToken)
	{
		if (market.IsLazerStable && market.LazerFeedId is int feedId)
		{
			try
			{
				var latest = await ApiClient.GetLazerPricesAsync([feedId],
					cancellationToken);
				var price = latest?.Prices?.FirstOrDefault(item =>
					item?.FeedId == feedId);
				if (price is not null)
				{
					var time = latest.TimestampMicroseconds.IsEmpty()
						? DateTime.UtcNow
						: latest.TimestampMicroseconds
							.FromUnixMicrosecondsUtc();
					var middle = price.Price.ApplyExponent(price.Exponent,
						"Lazer price");
					var confidence = price.Confidence.TryParseScaled(
						-price.Exponent);
					return StorePrice(new()
					{
						PairIndex = market.PairIndex,
						Time = time,
						Price = middle,
						Bid = price.Bid.IsEmpty()
							? confidence is decimal bidConfidence
								? middle - bidConfidence
								: null
							: price.Bid.ApplyExponent(price.Exponent,
								"Lazer bid"),
						Ask = price.Ask.IsEmpty()
							? confidence is decimal askConfidence
								? middle + askConfidence
								: null
							: price.Ask.ApplyExponent(price.Exponent,
								"Lazer ask"),
						Confidence = confidence,
					});
				}
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				this.AddWarningLog(
					"Unable to get Avantis Lazer snapshot for {0}: {1}",
					market.Symbol, error.Message);
			}
		}

		var response = await ApiClient.GetPriceAsync(market.PairIndex,
			cancellationToken) ?? throw new InvalidDataException(
				"Avantis returned no price for " + market.Symbol + ".");
		var priceData = market.IsLazerStable && response.Pro is not null
			? response.Pro
			: response.Core ?? response.Pro;
		if (priceData is null || priceData.Price <= 0)
			throw new InvalidDataException(
				"Avantis returned an invalid price for " + market.Symbol + ".");
		return StorePrice(new()
		{
			PairIndex = market.PairIndex,
			Time = priceData.PublishTimestampMilliseconds > 0
				? priceData.PublishTimestampMilliseconds.FromUnix(false)
				: DateTime.UtcNow,
			Price = priceData.Price,
		});
	}

	private async ValueTask OnPriceAsync(AvantisPriceUpdate update,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(update.PairIndex);
		if (market is null || update.Price <= 0)
			return;
		StorePrice(update);
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value == update.PairIndex).Select(static pair => pair.Key)];
		foreach (var transactionId in subscriptions)
			await SendLevel1Async(market, update, transactionId,
				cancellationToken);
	}

	private AvantisPriceUpdate StorePrice(AvantisPriceUpdate update)
	{
		UpdateServerTime(update.Time);
		using (_sync.EnterScope())
			_prices[update.PairIndex] = update;
		return update;
	}

	private ValueTask SendLevel1Async(AvantisMarket market,
		AvantisPriceUpdate price, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = price?.Time ?? ServerTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.PriceStep, market.PriceStep)
		.TryAdd(Level1Fields.VolumeStep, 0.000001m)
		.TryAdd(Level1Fields.State, market.IsOpen
			? SecurityStates.Trading
			: SecurityStates.Stoped)
		.TryAdd(Level1Fields.LastTradePrice, price?.Price)
		.TryAdd(Level1Fields.BestBidPrice, price?.Bid)
		.TryAdd(Level1Fields.BestBidTime,
			price?.Bid is null ? null : price.Time)
		.TryAdd(Level1Fields.BestAskPrice, price?.Ask)
		.TryAdd(Level1Fields.BestAskTime,
			price?.Ask is null ? null : price.Time)
		.TryAdd(Level1Fields.OpenInterest, market.OpenInterest),
			cancellationToken);

	private static SecurityMessage CreateSecurity(AvantisMarket market,
		long transactionId)
	{
		var message = new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = market.Symbol + " Avantis perpetual",
			ShortName = market.Symbol,
			Class = "PERPETUAL",
			SecurityType = SecurityTypes.Future,
			Currency = market.QuoteAsset.ToAvantisCurrency(),
			PriceStep = market.PriceStep,
			VolumeStep = 0.000001m,
			MinVolume = market.MinimumPositionValue > 0 &&
				market.MaximumLeverage > 0
				? market.MinimumPositionValue / market.MaximumLeverage
				: null,
			Multiplier = 1m,
			OriginalTransactionId = transactionId,
		};
		message.TryFillUnderlyingId(market.BaseAsset);
		return message;
	}
}
