namespace StockSharp.BitpandaFusion;

public partial class BitpandaFusionMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();

		var board = lookupMsg.SecurityId.BoardCode;
		var securityTypes = lookupMsg.GetSecurityTypes();
		if ((!board.IsEmpty() &&
			!board.EqualsIgnoreCase(BoardCodes.BitpandaFusion)) ||
			(securityTypes.Count > 0 &&
			!securityTypes.Contains(SecurityTypes.CryptoCurrency)) ||
			lookupMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var requested = lookupMsg.SecurityId.SecurityCode;
		if (!requested.IsEmpty())
			requested = NormalizePair(requested);
		BitpandaFusionPair[] pairs;
		using (_sync.EnterScope())
			pairs = [.. _pairs.Values.OrderBy(static pair => pair.Pair,
				StringComparer.OrdinalIgnoreCase)];

		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var pair in pairs)
		{
			if (!requested.IsEmpty() && !NormalizePair(pair.Pair).EqualsIgnoreCase(requested))
				continue;

			var security = CreateSecurity(pair, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;

			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = security.SecurityId,
				ServerTime = CurrentTime,
				OriginalTransactionId = lookupMsg.TransactionId,
			}.TryAdd(Level1Fields.State, SecurityStates.Trading), cancellationToken);
			if (--left <= 0)
				break;
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
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteSnapshotAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Bitpanda Fusion does not expose historical Level1 events.");

		var pair = GetPair(mdMsg.SecurityId);
		var ticker = (await RestClient.GetTickersAsync(pair, cancellationToken))
			?.FirstOrDefault(value => value?.Pair.EqualsIgnoreCase(pair) == true)
			?? throw new InvalidOperationException(
				$"Bitpanda Fusion returned no ticker for '{pair}'.");
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = pair.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = mdMsg.TransactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.Price.ToNullableDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.High.ToNullableDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.Low.ToNullableDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume.ToNullableDecimal()),
			cancellationToken);
		await CompleteSnapshotAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteSnapshotAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Bitpanda Fusion does not expose historical order-book events.");

		var pair = GetPair(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? 10).Min(100).Max(1);
		var snapshot = await RestClient.GetOrderBookAsync(pair, depth,
			cancellationToken);
		var serverTime = snapshot.Timestamp == default
			? CurrentTime
			: snapshot.Timestamp.UtcDateTime;
		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = pair.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = mdMsg.TransactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = CreateQuotes(snapshot.Bids, false, depth),
			Asks = CreateQuotes(snapshot.Asks, true, depth),
		}, cancellationToken);
		await CompleteSnapshotAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteSnapshotAsync(mdMsg, cancellationToken);
			return;
		}

		var pair = GetPair(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!BitpandaFusionExtensions.TimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"Bitpanda Fusion does not document {timeFrame} candles.");
		var from = mdMsg.From?.ToBitpandaFusionUtc();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToBitpandaFusionUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The candle-history start time is after its end time.");
		var count = (mdMsg.Count ?? 1440).Min(1440).Max(1).To<int>();

		var candles = await RestClient.GetCandlesAsync(pair, new()
		{
			Interval = timeFrame.ToBitpandaFusionInterval(),
			From = from,
			To = to,
			Limit = count,
		}, cancellationToken);
		var selected = (candles ?? [])
			.Where(candle => candle is not null)
			.Select(candle => (Candle: candle,
				OpenTime: candle.Timestamp.FromUnixSeconds()))
			.Where(item => item.OpenTime <= to &&
				(from is null || item.OpenTime >= from.Value))
			.OrderBy(static item => item.OpenTime)
			.GroupBy(static item => item.OpenTime)
			.Select(static group => group.First())
			.TakeLast(count);
		foreach (var item in selected)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = pair.ToStockSharp(),
				OpenTime = item.OpenTime,
				CloseTime = item.OpenTime + timeFrame,
				OpenPrice = item.Candle.Open,
				HighPrice = item.Candle.High,
				LowPrice = item.Candle.Low,
				ClosePrice = item.Candle.Close,
				TotalVolume = item.Candle.Volume,
				TypedArg = timeFrame,
				OriginalTransactionId = mdMsg.TransactionId,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
		await CompleteSnapshotAsync(mdMsg, cancellationToken);
	}

	private SecurityMessage CreateSecurity(BitpandaFusionPair pair,
		long originalTransactionId)
	{
		BitpandaFusionAsset baseAsset;
		BitpandaFusionAsset quoteAsset;
		using (_sync.EnterScope())
		{
			_assets.TryGetValue(pair.BaseAsset ?? string.Empty, out baseAsset);
			_assets.TryGetValue(pair.QuoteAsset ?? string.Empty, out quoteAsset);
		}
		var baseName = (baseAsset?.Name).IsEmpty(pair.BaseAsset);
		var quoteName = (quoteAsset?.Name).IsEmpty(pair.QuoteAsset);
		return new()
		{
			SecurityId = NormalizePair(pair.Pair).ToStockSharp(),
			Name = $"{baseName}/{quoteName}",
			ShortName = $"{pair.BaseAsset}/{pair.QuoteAsset}",
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = pair.QuoteAsset.ToCurrency(),
			PriceStep = pair.TickSize.ToNullableDecimal(),
			VolumeStep = pair.SizeIncrement.ToNullableDecimal(),
			MaxVolume = pair.MaxOrderSize.ToNullableDecimal(),
			OriginalTransactionId = originalTransactionId,
		};
	}

	private static QuoteChange[] CreateQuotes(
		IEnumerable<BitpandaFusionOrderBookLevel> levels, bool isAsk, int depth)
		=> [.. (levels ?? [])
			.Select(static level =>
			{
				var quantity = level.TotalQuantity.ToNullableDecimal();
				if (quantity is not > 0)
					quantity = level.Quantity.ToNullableDecimal();
				return (Price: level.Price.ToNullableDecimal(), Quantity: quantity);
			})
			.Where(static level => level.Price is > 0 && level.Quantity is > 0)
			.GroupBy(static level => level.Price.Value)
			.Select(static group => new QuoteChange(group.Key,
				group.Sum(static level => level.Quantity.Value)))
			.OrderBy(quote => isAsk ? quote.Price : -quote.Price)
			.Take(depth)];

	private async ValueTask CompleteSnapshotAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
	}
}
