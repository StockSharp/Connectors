namespace StockSharp.ZeroHash;

public partial class ZeroHashMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
			!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.ZeroHash))
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}
		var types = lookupMsg.GetSecurityTypes();
		if (types.Count > 0 &&
			!types.Contains(SecurityTypes.CryptoCurrency) &&
			!types.Contains(SecurityTypes.Currency))
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}
		await RefreshInstrumentsAsync(cancellationToken);
		ZeroHashInstrument[] instruments;
		using (_sync.EnterScope())
			instruments = [.. _instruments.Values];
		var requestedCode = lookupMsg.SecurityId.SecurityCode;
		var skip = Math.Max(0L, lookupMsg.Skip ?? 0);
		var left = Math.Max(0L, lookupMsg.Count ?? long.MaxValue);
		foreach (var instrument in instruments
			.Where(item => requestedCode.IsEmpty() || item.Symbol.Equals(
				requestedCode, StringComparison.OrdinalIgnoreCase))
			.OrderBy(static item => item.Symbol,
				StringComparer.OrdinalIgnoreCase))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var security = CreateSecurity(instrument, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, types))
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
	protected override ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscriptionAsync(mdMsg, DataType.Ticks,
			cancellationToken);

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
		if (mdMsg.IsHistoryOnly() || mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"Zero Hash CLOB streams do not publish historical market data.");
		var instrument = GetInstrument(mdMsg.SecurityId);
		var depth = dataType == DataType.MarketDepth
			? (mdMsg.MaxDepth ?? 50).Max(1).Min(1000)
			: 1;
		var source = CancellationTokenSource.CreateLinkedTokenSource(
			_connectionCancellation.Token);
		var subscription = new MarketSubscription
		{
			TransactionId = mdMsg.TransactionId,
			Instrument = instrument,
			DataType = dataType,
			Depth = depth,
			Cancellation = source,
			Task = Task.CompletedTask,
		};
		using (_sync.EnterScope())
			_marketSubscriptions.Add(mdMsg.TransactionId, subscription);
		try
		{
			subscription.Task = RunMarketStreamLoopAsync(subscription,
				source.Token).AsTask();
		}
		catch
		{
			using (_sync.EnterScope())
				_marketSubscriptions.Remove(mdMsg.TransactionId);
			source.Dispose();
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask UnsubscribeMarketAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		using (_sync.EnterScope())
			_marketSubscriptions.Remove(transactionId, out subscription);
		if (subscription is null)
			return;
		subscription.Cancellation.Cancel();
		try
		{
			await subscription.Task;
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			subscription.Cancellation.Dispose();
		}
		cancellationToken.ThrowIfCancellationRequested();
	}

	private async ValueTask RunMarketStreamLoopAsync(
		MarketSubscription subscription, CancellationToken cancellationToken)
	{
		var attempt = 0;
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await RestClient.ReadMarketStreamAsync(new()
				{
					Depth = subscription.Depth,
					IsSnapshotOnly = false,
					Symbols = [subscription.Instrument.Symbol],
					IsUnaggregated = false,
				}, (envelope, token) => OnMarketEnvelopeAsync(subscription,
					envelope, token), cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				throw new EndOfStreamException(
					"Zero Hash closed the market-data subscription stream.");
			}
			catch (OperationCanceledException) when (
				cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception error)
			{
				await ReportStreamErrorAsync(error);
				attempt++;
				var delay = TimeSpan.FromSeconds(Math.Min(10,
					1 << Math.Min(attempt - 1, 3)));
				try
				{
					await Task.Delay(delay, cancellationToken);
				}
				catch (OperationCanceledException) when (
					cancellationToken.IsCancellationRequested)
				{
					return;
				}
			}
		}
	}

	private async ValueTask OnMarketEnvelopeAsync(
		MarketSubscription subscription, ZeroHashMarketEnvelope envelope,
		CancellationToken cancellationToken)
	{
		var error = envelope?.Error?.GetMessage();
		if (!error.IsEmpty())
			throw new InvalidDataException(
				"Zero Hash market stream error: " + error);
		var update = envelope?.Result?.Update;
		if (update is null)
			return;
		if (!update.Symbol.IsEmpty() && !update.Symbol.Equals(
			subscription.Instrument.Symbol, StringComparison.OrdinalIgnoreCase))
			return;
		var time = update.TransactionTime.TryParseZeroHashTime() ??
			update.Statistics?.LastTradeTime.TryParseZeroHashTime() ??
			DateTime.UtcNow;
		UpdateServerTime(time);
		if (subscription.DataType == DataType.MarketDepth)
			await SendDepthAsync(subscription, update, time, cancellationToken);
		else if (subscription.DataType == DataType.Level1)
			await SendLevel1Async(subscription, update, time, cancellationToken);
		else
			await SendTickAsync(subscription, update, time, cancellationToken);
	}

	private ValueTask SendDepthAsync(MarketSubscription subscription,
		ZeroHashMarketUpdate update, DateTime time,
		CancellationToken cancellationToken)
	{
		var priceScale = subscription.Instrument.GetPriceScale();
		var quantityScale = subscription.Instrument.GetQuantityScale();
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = subscription.Instrument.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = subscription.TransactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = CreateQuotes(update.IsBookHidden ? [] : update.Bids,
				priceScale, quantityScale, true, subscription.Depth),
			Asks = CreateQuotes(update.IsBookHidden ? [] : update.Offers,
				priceScale, quantityScale, false, subscription.Depth),
		}, cancellationToken);
	}

	private ValueTask SendLevel1Async(MarketSubscription subscription,
		ZeroHashMarketUpdate update, DateTime time,
		CancellationToken cancellationToken)
	{
		var priceScale = subscription.Instrument.GetPriceScale();
		var quantityScale = subscription.Instrument.GetQuantityScale();
		var bids = CreateQuotes(update.Bids, priceScale, quantityScale, true, 1);
		var asks = CreateQuotes(update.Offers, priceScale, quantityScale, false, 1);
		var stats = update.Statistics;
		decimal? bidPrice = bids.Length > 0 ? bids[0].Price : null;
		decimal? bidVolume = bids.Length > 0 ? bids[0].Volume : null;
		decimal? askPrice = asks.Length > 0 ? asks[0].Price : null;
		decimal? askVolume = asks.Length > 0 ? asks[0].Volume : null;
		SecurityStates? state = update.State is null
			? null
			: update.State == ZeroHashInstrumentStates.Open
				? SecurityStates.Trading
				: SecurityStates.Stoped;
		var message = new Level1ChangeMessage
		{
			SecurityId = subscription.Instrument.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = subscription.TransactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, bidPrice)
		.TryAdd(Level1Fields.BestBidVolume, bidVolume)
		.TryAdd(Level1Fields.BestAskPrice, askPrice)
		.TryAdd(Level1Fields.BestAskVolume, askVolume)
		.TryAdd(Level1Fields.LastTradePrice,
			ZeroHashExtensions.UnscaleValue(stats?.LastTradePrice, priceScale))
		.TryAdd(Level1Fields.LastTradeVolume,
			ZeroHashExtensions.UnscaleValue(stats?.LastTradeQuantity,
				quantityScale))
		.TryAdd(Level1Fields.OpenPrice,
			ZeroHashExtensions.UnscaleValue(stats?.OpenPrice, priceScale))
		.TryAdd(Level1Fields.HighPrice,
			ZeroHashExtensions.UnscaleValue(stats?.HighPrice, priceScale))
		.TryAdd(Level1Fields.LowPrice,
			ZeroHashExtensions.UnscaleValue(stats?.LowPrice, priceScale))
		.TryAdd(Level1Fields.ClosePrice,
			ZeroHashExtensions.UnscaleValue(stats?.ClosePrice, priceScale))
		.TryAdd(Level1Fields.SettlementPrice,
			ZeroHashExtensions.UnscaleValue(stats?.SettlementPrice, priceScale))
		.TryAdd(Level1Fields.Volume,
			ZeroHashExtensions.UnscaleValue(stats?.SharesTraded, quantityScale))
		.TryAdd(Level1Fields.OpenInterest,
			ZeroHashExtensions.UnscaleValue(stats?.OpenInterest, quantityScale))
		.TryAdd(Level1Fields.State, state);
		return SendOutMessageAsync(message, cancellationToken);
	}

	private ValueTask SendTickAsync(MarketSubscription subscription,
		ZeroHashMarketUpdate update, DateTime fallbackTime,
		CancellationToken cancellationToken)
	{
		var stats = update.Statistics;
		var tradeTime = stats?.LastTradeTime.TryParseZeroHashTime();
		var price = ZeroHashExtensions.UnscaleValue(stats?.LastTradePrice,
			subscription.Instrument.GetPriceScale());
		var volume = ZeroHashExtensions.UnscaleValue(stats?.LastTradeQuantity,
			subscription.Instrument.GetQuantityScale());
		if (price is null || volume is null || volume <= 0)
			return default;
		var time = tradeTime ?? fallbackTime;
		var key = time.Ticks.ToString(CultureInfo.InvariantCulture) + "|" +
			price.Value.ToString(CultureInfo.InvariantCulture) + "|" +
			volume.Value.ToString(CultureInfo.InvariantCulture);
		using (_sync.EnterScope())
		{
			if (subscription.LastTradeKey == key)
				return default;
			subscription.LastTradeKey = key;
			subscription.LastTradeTime = time;
		}
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = subscription.Instrument.ToStockSharp(),
			ServerTime = time,
			TradePrice = price,
			TradeVolume = volume,
			TradeStringId = key,
			OriginalTransactionId = subscription.TransactionId,
		}, cancellationToken);
	}

	private static QuoteChange[] CreateQuotes(ZeroHashBookLevel[] levels,
		decimal priceScale, decimal quantityScale, bool isBid, int depth)
	{
		var aggregated = new Dictionary<decimal, decimal>();
		foreach (var level in levels ?? [])
		{
			var price = ZeroHashExtensions.UnscaleValue(level?.Price, priceScale);
			var volume = ZeroHashExtensions.UnscaleValue(level?.Quantity,
				quantityScale);
			if (price is not > 0 || volume is not > 0)
				continue;
			aggregated[price.Value] = aggregated.TryGetValue(price.Value,
				out var existing) ? existing + volume.Value : volume.Value;
		}
		return [.. aggregated
			.Select(static pair => new QuoteChange(pair.Key, pair.Value))
			.OrderBy(quote => quote.Price * (isBid ? -1 : 1))
			.Take(depth)];
	}

	private static SecurityMessage CreateSecurity(ZeroHashInstrument instrument,
		long transactionId)
	{
		var priceScale = instrument.GetPriceScale();
		var quantityScale = instrument.GetQuantityScale();
		var priceStep = instrument.TickSize is > 0
			? instrument.TickSize.Value
			: 1m / priceScale;
		var minimum = ZeroHashExtensions.UnscaleValue(
			instrument.MinimumTradeQuantity, quantityScale);
		var quote = instrument.ForexAttributes?.QuoteCurrency;
		if (quote.IsEmpty())
		{
			var separator = instrument.Symbol.LastIndexOf('/');
			if (separator >= 0 && separator + 1 < instrument.Symbol.Length)
				quote = instrument.Symbol[(separator + 1)..];
		}
		return new()
		{
			SecurityId = instrument.ToStockSharp(),
			Name = instrument.Description.IsEmpty()
				? instrument.Symbol
				: instrument.Description,
			ShortName = instrument.Symbol,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = quote.ToCurrencyType(),
			PriceStep = priceStep,
			Decimals = priceStep.GetCachedDecimals(),
			VolumeStep = 1m / quantityScale,
			MinVolume = minimum is > 0 ? minimum : 1m / quantityScale,
			OriginalTransactionId = transactionId,
		};
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
