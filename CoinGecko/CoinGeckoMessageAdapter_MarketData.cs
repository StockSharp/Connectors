namespace StockSharp.CoinGecko;

public partial class CoinGeckoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var securityTypes = message.GetSecurityTypes();
		if (securityTypes.Count > 0 &&
			!securityTypes.Contains(SecurityTypes.CryptoCurrency))
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}

		var board = message.SecurityId.BoardCode;
		var value = (message.SecurityId.Native as string)
			.IsEmpty(message.SecurityId.SecurityCode).IsEmpty(message.Name)?.Trim();
		if (CoinGeckoSecurityKey.TryParse(message.SecurityId.Native as string,
			out var nativeKey))
			value = nativeKey.Kind == CoinGeckoSecurityKinds.Coin
				? nativeKey.CoinId
				: nativeKey.PoolAddress;
		var skip = Math.Max(0L, message.Skip ?? 0);
		var left = Math.Min(message.Count ?? MaximumItems, MaximumItems);
		var sent = new HashSet<string>(StringComparer.Ordinal);

		async ValueTask EmitAsync(SecurityMessage security)
		{
			if (security is null || left <= 0 ||
				(value.IsEmpty() && !security.IsMatch(message, securityTypes)) ||
				!sent.Add(security.SecurityId.Native as string))
				return;
			if (skip > 0)
			{
				skip--;
				return;
			}
			await SendOutMessageAsync(security, cancellationToken);
			left--;
		}

		var coinsRequested = board.IsEmpty() ||
			board.EqualsIgnoreCase(BoardCodes.CoinGecko);
		if (coinsRequested && left > 0)
		{
			var quote = CoinGeckoExtensions.NormalizeCurrency(QuoteCurrency);
			foreach (var coin in await GetCoinsAsync(cancellationToken))
			{
				if (!Matches(coin, value))
					continue;
				var key = new CoinGeckoSecurityKey(CoinGeckoSecurityKinds.Coin,
					coin.Id, quote, null, null, null, coin.Symbol, coin.Name, null);
				await EmitAsync(ToSecurityMessage(key, message.TransactionId));
				if (left <= 0)
					break;
			}
		}

		var poolsRequested = board.IsEmpty() ||
			board.EqualsIgnoreCase(BoardCodes.CoinGeckoOnChain);
		if (poolsRequested && !value.IsEmpty() && left > 0)
		{
			for (var page = 1; page <= PoolSearchPages && left > 0; page++)
			{
				var response = await SafeRest().SearchPoolsAsync(value,
					OnchainNetwork?.Trim(), page, cancellationToken);
				var resources = response?.Data ?? [];
				foreach (var resource in resources)
				{
					var key = CreatePoolKey(resource, response?.Included);
					if (key is not CoinGeckoSecurityKey poolKey)
						continue;
					RememberPool(poolKey);
					await EmitAsync(ToSecurityMessage(poolKey,
						message.TransactionId));
					if (left <= 0)
						break;
				}
				if (resources.Length < 20)
					break;
			}
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			await RemoveLiveSubscriptionAsync(message.OriginalTransactionId,
				cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		if (message.Count is <= 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}

		var key = await ResolveKeyAsync(message.SecurityId, cancellationToken);
		var securityId = key.ToSecurityId();
		var sent = key.Kind == CoinGeckoSecurityKinds.Coin
			? await SendCoinLevel1Async(key, securityId, message.TransactionId,
				cancellationToken)
			: await SendPoolLevel1Async(key, securityId, message.TransactionId,
				cancellationToken);
		var remaining = message.Count;
		if (sent && remaining is > 0)
			remaining--;
		if (message.IsHistoryOnly() || remaining == 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}
		await AddLiveSubscriptionAsync(message, key, securityId, remaining,
			null, null, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			await RemoveLiveSubscriptionAsync(message.OriginalTransactionId,
				cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		if (message.Count is <= 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}

		var key = await ResolveKeyAsync(message.SecurityId, cancellationToken);
		if (key.Kind != CoinGeckoSecurityKinds.OnchainPool)
			throw new NotSupportedException(
				"CoinGecko trade data is available only for on-chain pools.");
		var securityId = key.ToSecurityId();
		var remaining = message.Count;
		if (message.From is not null || message.To is not null ||
			message.IsHistoryOnly())
		{
			var from = message.From?.EnsureUtc();
			var to = (message.To ?? CurrentTime).EnsureUtc();
			var maximum = (remaining ?? HistoryLimit).Min(HistoryLimit).Max(1)
				.To<int>();
			var trades = await SafeRest().GetPoolTradesAsync(key.Network,
				key.PoolAddress, cancellationToken);
			var selected = trades.Where(static item => item?.Attributes is not null)
				.Select(item => ToTradeMessage(key, securityId, item,
					message.TransactionId))
				.Where(static item => item is not null)
				.Where(item => (from is null || item.ServerTime >= from) &&
					item.ServerTime <= to)
				.OrderBy(static item => item.ServerTime)
				.TakeLast(maximum)
				.ToArray();
			foreach (var trade in selected)
			{
				RememberTrade(trade.TradeStringId);
				await SendOutMessageAsync(trade, cancellationToken);
			}
			if (remaining is > 0)
				remaining = Math.Max(0, remaining.Value - selected.Length);
		}

		if (message.IsHistoryOnly() || remaining == 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}
		await AddLiveSubscriptionAsync(message, key, securityId, remaining,
			null, null, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			await RemoveLiveSubscriptionAsync(message.OriginalTransactionId,
				cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		if (message.Count is <= 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}

		var key = await ResolveKeyAsync(message.SecurityId, cancellationToken);
		var securityId = key.ToSecurityId();
		var timeFrame = message.GetTimeFrame();
		if (!CoinGeckoExtensions.TimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"CoinGecko does not support the {timeFrame} candle interval.");
		if (key.Kind == CoinGeckoSecurityKinds.Coin && !message.IsHistoryOnly())
			throw new NotSupportedException(
				"CoinGecko aggregated-coin candles are historical only. " +
				"Live OHLCV is available for on-chain pools.");
		if (key.Kind == CoinGeckoSecurityKinds.OnchainPool &&
			!message.IsHistoryOnly() &&
			(timeFrame == TimeSpan.FromSeconds(15) ||
				timeFrame == TimeSpan.FromSeconds(30) ||
				timeFrame == TimeSpan.FromMinutes(30) ||
				timeFrame == TimeSpan.FromDays(4)))
			throw new NotSupportedException(
				"CoinGecko 15-second, 30-second, 30-minute, and 4-day pool candles " +
				"are historical only; the WebSocket exposes its documented intervals.");

		var count = (message.Count ?? Math.Min(500, HistoryLimit))
			.Min(HistoryLimit).Max(1).To<int>();
		var to = (message.To ?? CurrentTime).EnsureUtc();
		var from = message.From?.EnsureUtc() ?? EstimateFrom(to, timeFrame, count);
		if (from >= to)
			throw new ArgumentOutOfRangeException(nameof(message),
				"CoinGecko candle start time must be earlier than end time.");
		var candles = key.Kind == CoinGeckoSecurityKinds.Coin
			? await GetCoinCandlesAsync(key, from, to, timeFrame, count,
				cancellationToken)
			: await GetPoolCandlesAsync(key, from, to, timeFrame, count,
				cancellationToken);
		foreach (var candle in candles)
			await SendCandleAsync(securityId, candle, timeFrame,
				message.TransactionId, CandleStates.Finished, cancellationToken);

		var remaining = message.Count;
		if (remaining is > 0)
			remaining = Math.Max(0, remaining.Value - candles.Length);
		if (message.IsHistoryOnly() || remaining == 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}
		await AddLiveSubscriptionAsync(message, key, securityId, remaining,
			timeFrame, candles.LastOrDefault(), cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private async ValueTask<CoinGeckoCoin[]> GetCoinsAsync(
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			if (_coins.Count > 0)
				return [.. _coins.Values];
		}
		var coins = await SafeRest().GetCoinsAsync(cancellationToken);
		using (_sync.EnterScope())
		{
			foreach (var coin in coins.Where(static coin => coin?.Id.IsEmpty() == false))
				_coins[coin.Id] = coin;
			return [.. _coins.Values];
		}
	}

	private async ValueTask<CoinGeckoSecurityKey> ResolveKeyAsync(
		SecurityId securityId, CancellationToken cancellationToken)
	{
		if (CoinGeckoSecurityKey.TryParse(securityId.Native as string, out var key))
			return key;
		if (securityId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinGeckoOnChain))
		{
			var value = securityId.SecurityCode.ThrowIfEmpty(
				nameof(securityId.SecurityCode)).Trim();
			using (_sync.EnterScope())
			{
				var cached = _pools.Values.FirstOrDefault(item =>
					item.PoolAddress.Equals(value, StringComparison.Ordinal) ||
					$"{item.Network}:{item.PoolAddress}".Equals(value,
						StringComparison.Ordinal));
				if (cached.Kind == CoinGeckoSecurityKinds.OnchainPool)
					return cached;
			}
			var separator = value.IndexOf(':');
			if (separator > 0 && separator < value.Length - 1)
			{
				var network = value[..separator];
				var address = value[(separator + 1)..];
				var response = await SafeRest().GetPoolAsync(network, address,
					cancellationToken);
				var resolved = CreatePoolKey(response?.Data, response?.Included,
					network);
				if (resolved is CoinGeckoSecurityKey poolKey)
				{
					RememberPool(poolKey);
					return poolKey;
				}
			}
			throw new InvalidOperationException(
				"An on-chain pool must come from security lookup or use network:poolAddress as its code.");
		}

		var code = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		var separatorIndex = code.IndexOf('/');
		var identity = (separatorIndex > 0 ? code[..separatorIndex] : code).Trim();
		var quote = separatorIndex > 0 && separatorIndex < code.Length - 1
			? CoinGeckoExtensions.NormalizeCurrency(code[(separatorIndex + 1)..])
			: CoinGeckoExtensions.NormalizeCurrency(QuoteCurrency);
		var coins = await GetCoinsAsync(cancellationToken);
		var coin = coins.FirstOrDefault(item => item.Id.EqualsIgnoreCase(identity));
		if (coin is null)
		{
			var matches = coins.Where(item => item.Symbol.EqualsIgnoreCase(identity))
				.Take(2).ToArray();
			if (matches.Length != 1)
				throw new InvalidOperationException(
					$"CoinGecko coin '{identity}' is unknown or ambiguous. Use security lookup to preserve its API ID.");
			coin = matches[0];
		}
		return new(CoinGeckoSecurityKinds.Coin, coin.Id, quote, null, null,
			null, coin.Symbol, coin.Name, null);
	}

	private static CoinGeckoSecurityKey? CreatePoolKey(
		CoinGeckoPoolResource resource, CoinGeckoIncludedResource[] included,
		string knownNetwork = null)
	{
		if (resource?.Type != CoinGeckoResourceTypes.Pool ||
			resource.Attributes?.Address.IsEmpty() != false)
			return null;
		included ??= [];
		var baseId = resource.Relationships?.BaseToken?.Data?.Id;
		var dexId = resource.Relationships?.Dex?.Data?.Id;
		var baseToken = included.FirstOrDefault(item =>
			item?.Type == CoinGeckoResourceTypes.Token && item.Id == baseId);
		var dex = included.FirstOrDefault(item =>
			item?.Type == CoinGeckoResourceTypes.Dex && item.Id == dexId);
		var tokenAddress = baseToken?.Attributes?.Address;
		var symbol = baseToken?.Attributes?.Symbol;
		if (symbol.IsEmpty())
			symbol = resource.Attributes.Name?.Split('/')[0].Trim();
		if (tokenAddress.IsEmpty() || symbol.IsEmpty())
			return null;
		var network = knownNetwork.IsEmpty()
			? CoinGeckoExtensions.DeriveNetwork(resource.Id,
				resource.Attributes.Address)
			: knownNetwork;
		if (network.IsEmpty())
			return null;
		return new(CoinGeckoSecurityKinds.OnchainPool, null, "usd", network,
			resource.Attributes.Address, tokenAddress, symbol,
			resource.Attributes.Name.IsEmpty(symbol),
			dex?.Attributes?.Name.IsEmpty(dexId));
	}

	private void RememberPool(CoinGeckoSecurityKey key)
	{
		using (_sync.EnterScope())
			_pools[PoolCacheKey(key.Network, key.PoolAddress)] = key;
	}

	private static SecurityMessage ToSecurityMessage(CoinGeckoSecurityKey key,
		long originalTransactionId)
	{
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = key.ToSecurityId(),
			Name = key.Name.IsEmpty(key.Symbol),
			ShortName = key.Symbol?.ToUpperInvariant(),
			Class = key.Kind == CoinGeckoSecurityKinds.Coin
				? key.CoinId
				: string.Join(':', new[] { key.Network, key.Dex, key.PoolAddress }
					.Where(static value => !value.IsEmpty())),
			SecurityType = SecurityTypes.CryptoCurrency,
		};
		var currency = key.Kind == CoinGeckoSecurityKinds.Coin
			? key.QuoteCurrency
			: "usd";
		if (Enum.TryParse<CurrencyTypes>(currency, true, out var currencyType))
			message.Currency = currencyType;
		return message;
	}

	private async ValueTask<bool> SendCoinLevel1Async(CoinGeckoSecurityKey key,
		SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
	{
		var market = await SafeRest().GetCoinMarketAsync(key.CoinId,
			key.QuoteCurrency, cancellationToken);
		if (market is null)
			return false;
		var time = market.LastUpdated.IsEmpty()
			? CurrentTime.EnsureUtc()
			: market.LastUpdated.ParseCoinGeckoTime();
		decimal? open = market.CurrentPrice is decimal current &&
			market.PriceChange24Hours is decimal change
				? current - change
				: null;
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.LastTradePrice, Positive(market.CurrentPrice))
		.TryAdd(Level1Fields.LastTradeTime,
			Positive(market.CurrentPrice) is null ? null : time)
		.TryAdd(Level1Fields.OpenPrice, Positive(open))
		.TryAdd(Level1Fields.HighPrice, Positive(market.High24Hours))
		.TryAdd(Level1Fields.LowPrice, Positive(market.Low24Hours))
		.TryAdd(Level1Fields.Volume, NonNegative(market.TotalVolume))
		.TryAdd(Level1Fields.Change, market.PriceChangePercentage24Hours);
		await SendOutMessageAsync(message, cancellationToken);
		return true;
	}

	private async ValueTask<bool> SendPoolLevel1Async(CoinGeckoSecurityKey key,
		SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
	{
		var response = await SafeRest().GetPoolAsync(key.Network, key.PoolAddress,
			cancellationToken);
		var attributes = response?.Data?.Attributes;
		if (attributes is null)
			return false;
		var time = CurrentTime.EnsureUtc();
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.LastTradePrice,
			Positive(attributes.BaseTokenPriceUsd.ParseCoinGeckoDecimal()))
		.TryAdd(Level1Fields.LastTradeTime,
			attributes.BaseTokenPriceUsd.ParseCoinGeckoDecimal() is > 0 ? time : null)
		.TryAdd(Level1Fields.Volume,
			NonNegative(attributes.VolumeUsd?.Hours24.ParseCoinGeckoDecimal()))
		.TryAdd(Level1Fields.Change,
			attributes.PriceChangePercentage?.Hours24.ParseCoinGeckoDecimal()),
			cancellationToken);
		return true;
	}

	private async ValueTask<CoinGeckoCandle[]> GetCoinCandlesAsync(
		CoinGeckoSecurityKey key, DateTime from, DateTime to, TimeSpan timeFrame,
		int count, CancellationToken cancellationToken)
	{
		CoinGeckoCandle[] source;
		TimeSpan sourceFrame;
		if (Tier == CoinGeckoApiTiers.Pro && timeFrame >= TimeSpan.FromHours(1))
		{
			var interval = timeFrame < TimeSpan.FromDays(1)
				? CoinGeckoCoinIntervals.Hourly
				: CoinGeckoCoinIntervals.Daily;
			sourceFrame = interval == CoinGeckoCoinIntervals.Hourly
				? TimeSpan.FromHours(1)
				: TimeSpan.FromDays(1);
			var factor = Math.Max(1, checked((int)(timeFrame.Ticks /
				sourceFrame.Ticks)));
			var raw = await SafeRest().GetCoinOhlcRangeAsync(key.CoinId,
				key.QuoteCurrency, from - timeFrame, to + sourceFrame, interval,
				checked(count * factor + factor * 2), cancellationToken);
			source = raw.Select(item => new CoinGeckoCandle
			{
				OpenTime = item.Timestamp.ToCoinGeckoTime() - sourceFrame,
				Open = item.Open,
				High = item.High,
				Low = item.Low,
				Close = item.Close,
			}).ToArray();
		}
		else
		{
			sourceFrame = timeFrame;
			var days = GetRecentCoinDays(timeFrame, from,
				CurrentTime.EnsureUtc());
			var raw = await SafeRest().GetCoinOhlcRecentAsync(key.CoinId,
				key.QuoteCurrency, days, cancellationToken);
			source = raw.Select(item => new CoinGeckoCandle
			{
				OpenTime = item.Timestamp.ToCoinGeckoTime() - sourceFrame,
				Open = item.Open,
				High = item.High,
				Low = item.Low,
				Close = item.Close,
			}).ToArray();
		}
		return SelectCandles(AggregateCandles(source, sourceFrame, timeFrame),
			from, to, count);
	}

	private async ValueTask<CoinGeckoCandle[]> GetPoolCandlesAsync(
		CoinGeckoSecurityKey key, DateTime from, DateTime to, TimeSpan timeFrame,
		int count, CancellationToken cancellationToken)
	{
		var interval = timeFrame.ToPoolHistoryInterval();
		if (Tier == CoinGeckoApiTiers.Demo &&
			interval.Timeframe == CoinGeckoOhlcvTimeframes.Second)
			throw new NotSupportedException(
				"CoinGecko second-level pool OHLCV requires a Pro API plan.");
		var factor = Math.Max(1, checked((int)(timeFrame.Ticks /
			interval.SourceFrame.Ticks)));
		var maximum = Math.Min(HistoryLimit, checked(count * factor + factor * 2));
		var raw = new List<CoinGeckoPoolOhlcv>();
		var seen = new HashSet<decimal>();
		long? before = ToUnix(to) + 1;
		while (raw.Count < maximum)
		{
			var page = await SafeRest().GetPoolOhlcvPageAsync(key.Network,
				key.PoolAddress, interval.Timeframe, interval.Aggregate, before,
				Math.Min(1000, maximum - raw.Count), cancellationToken);
			if (page.Length == 0)
				break;
			foreach (var item in page)
				if (seen.Add(item.Timestamp))
					raw.Add(item);
			var oldest = page.Min(static item => item.Timestamp);
			if (oldest.ToCoinGeckoTime() <= from ||
				before == checked((long)oldest))
				break;
			before = checked((long)oldest);
		}
		var source = raw.Select(item => new CoinGeckoCandle
		{
			OpenTime = item.Timestamp.ToCoinGeckoTime(),
			Open = item.Open,
			High = item.High,
			Low = item.Low,
			Close = item.Close,
			Volume = item.Volume,
		}).ToArray();
		return SelectCandles(AggregateCandles(source, interval.SourceFrame,
			timeFrame), from, to, count);
	}

	private static CoinGeckoCandle[] AggregateCandles(
		IEnumerable<CoinGeckoCandle> source, TimeSpan sourceFrame,
		TimeSpan targetFrame)
	{
		var ordered = source.Where(static candle => candle is not null)
			.OrderBy(static candle => candle.OpenTime).ToArray();
		if (sourceFrame == targetFrame)
			return ordered;
		return ordered.GroupBy(candle => candle.OpenTime.Align(targetFrame))
			.Select(group =>
			{
				var items = group.OrderBy(static candle => candle.OpenTime).ToArray();
				return new CoinGeckoCandle
				{
					OpenTime = group.Key,
					Open = items[0].Open,
					High = items.Max(static candle => candle.High),
					Low = items.Min(static candle => candle.Low),
					Close = items[^1].Close,
					Volume = items.Any(static candle => candle.Volume is not null)
						? items.Sum(static candle => candle.Volume ?? 0)
						: null,
				};
			})
			.OrderBy(static candle => candle.OpenTime)
			.ToArray();
	}

	private static CoinGeckoCandle[] SelectCandles(
		IEnumerable<CoinGeckoCandle> candles, DateTime from, DateTime to, int count)
		=> candles.Where(candle => candle.OpenTime >= from &&
			candle.OpenTime < to).OrderBy(static candle => candle.OpenTime)
			.TakeLast(count).ToArray();

	private static CoinGeckoRecentDays GetRecentCoinDays(TimeSpan timeFrame,
		DateTime from, DateTime now)
	{
		if (timeFrame == TimeSpan.FromMinutes(30))
		{
			if (from < now.AddDays(-1))
				throw new NotSupportedException(
					"CoinGecko 30-minute OHLC is limited to the recent one-day endpoint.");
			return CoinGeckoRecentDays.Day1;
		}
		if (timeFrame == TimeSpan.FromHours(4))
		{
			if (from >= now.AddDays(-7))
				return CoinGeckoRecentDays.Day7;
			if (from >= now.AddDays(-14))
				return CoinGeckoRecentDays.Day14;
			if (from >= now.AddDays(-30))
				return CoinGeckoRecentDays.Day30;
			throw new NotSupportedException(
				"CoinGecko four-hour Demo OHLC is limited to 30 recent days.");
		}
		if (timeFrame == TimeSpan.FromDays(4))
		{
			if (from >= now.AddDays(-90))
				return CoinGeckoRecentDays.Day90;
			if (from >= now.AddDays(-180))
				return CoinGeckoRecentDays.Day180;
			if (from >= now.AddDays(-365))
				return CoinGeckoRecentDays.Day365;
			throw new NotSupportedException(
				"CoinGecko four-day Demo OHLC is limited to 365 recent days.");
		}
		throw new NotSupportedException(
			"CoinGecko Demo coin OHLC supports 30-minute, 4-hour, and 4-day candles. " +
			"Hourly and daily range candles require a Pro Analyst plan or above.");
	}

	private async ValueTask AddLiveSubscriptionAsync(MarketDataMessage message,
		CoinGeckoSecurityKey key, SecurityId securityId, long? remaining,
		TimeSpan? timeFrame, CoinGeckoCandle lastCandle,
		CancellationToken cancellationToken)
	{
		var subscription = new LiveSubscription
		{
			TransactionId = message.TransactionId,
			SecurityId = securityId,
			Key = key,
			DataType = message.DataType2,
			TimeFrame = timeFrame,
			Remaining = remaining,
			LastCountedCandle = lastCandle?.OpenTime,
		};
		var streamKey = ToStreamKey(subscription);
		var isFirst = false;
		using (_sync.EnterScope())
		{
			if (_liveSubscriptions.ContainsKey(message.TransactionId))
				throw new InvalidOperationException(
					$"CoinGecko subscription {message.TransactionId} already exists.");
			isFirst = !_liveSubscriptions.Values.Any(item =>
				ToStreamKey(item) == streamKey);
			if (isFirst && _liveSubscriptions.Values.Select(ToStreamKey)
				.Where(item => item.Channel == streamKey.Channel).Distinct().Count() >= 100)
				throw new InvalidOperationException(
					"CoinGecko WebSocket allows at most 100 subscriptions per channel.");
			_liveSubscriptions.Add(message.TransactionId, subscription);
		}
		try
		{
			if (isFirst)
				await GetOrCreateSocket().SubscribeAsync(streamKey,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_liveSubscriptions.Remove(message.TransactionId);
			throw;
		}
	}

	private async ValueTask RemoveLiveSubscriptionAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		LiveSubscription removed;
		bool isLast;
		using (_sync.EnterScope())
		{
			if (!_liveSubscriptions.Remove(transactionId, out removed))
				return;
			var streamKey = ToStreamKey(removed);
			isLast = !_liveSubscriptions.Values.Any(item =>
				ToStreamKey(item) == streamKey);
		}
		if (isLast && _socket is not null)
			await _socket.UnsubscribeAsync(ToStreamKey(removed), cancellationToken);
	}

	private static CoinGeckoStreamKey ToStreamKey(LiveSubscription subscription)
	{
		if (subscription.DataType == DataType.Level1)
			return subscription.Key.Kind == CoinGeckoSecurityKinds.Coin
				? CoinGeckoStreamKey.CoinPrice(subscription.Key.CoinId,
					subscription.Key.QuoteCurrency)
				: CoinGeckoStreamKey.TokenPrice(subscription.Key.Network,
					subscription.Key.TokenAddress);
		if (subscription.DataType == DataType.Ticks)
			return CoinGeckoStreamKey.PoolTrades(subscription.Key.Network,
				subscription.Key.PoolAddress);
		if (subscription.DataType.IsTFCandles)
			return CoinGeckoStreamKey.PoolOhlcv(subscription.Key.Network,
				subscription.Key.PoolAddress,
				(subscription.TimeFrame ?? throw new InvalidOperationException(
					"CoinGecko candle interval is missing.")).ToSocketInterval());
		throw new NotSupportedException(
			$"Unsupported CoinGecko live data type {subscription.DataType}.");
	}

	private async ValueTask OnCoinPriceAsync(CoinGeckoCoinPriceUpdate update,
		CancellationToken cancellationToken)
	{
		if (update?.CoinId.IsEmpty() != false || update.Price is null)
			return;
		LiveSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _liveSubscriptions.Values.Where(item =>
				item.DataType == DataType.Level1 &&
				item.Key.Kind == CoinGeckoSecurityKinds.Coin &&
				item.Key.CoinId.EqualsIgnoreCase(update.CoinId) &&
				item.Key.QuoteCurrency.EqualsIgnoreCase(
					update.QuoteCurrency.IsEmpty("usd")))];
		var time = (update.Timestamp ?? ToUnix(CurrentTime))
			.ToCoinGeckoTime();
		foreach (var subscription in subscriptions)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = time,
			}
			.TryAdd(Level1Fields.LastTradePrice, Positive(update.Price))
			.TryAdd(Level1Fields.LastTradeTime, time)
			.TryAdd(Level1Fields.Volume, NonNegative(update.Volume24Hours))
			.TryAdd(Level1Fields.Change,
				update.PriceChangePercentage24Hours), cancellationToken);
			await ConsumeLiveItemAsync(subscription, cancellationToken);
		}
	}

	private async ValueTask OnOnchainPriceAsync(
		CoinGeckoOnchainPriceUpdate update, CancellationToken cancellationToken)
	{
		if (update?.Network.IsEmpty() != false ||
			update.TokenAddress.IsEmpty() || update.PriceUsd is null)
			return;
		LiveSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _liveSubscriptions.Values.Where(item =>
				item.DataType == DataType.Level1 &&
				item.Key.Kind == CoinGeckoSecurityKinds.OnchainPool &&
				item.Key.Network.EqualsIgnoreCase(update.Network) &&
				AddressEquals(item.Key.TokenAddress, update.TokenAddress))];
		var time = (update.Timestamp ?? ToUnix(CurrentTime))
			.ToCoinGeckoTime();
		foreach (var subscription in subscriptions)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = time,
			}
			.TryAdd(Level1Fields.LastTradePrice, Positive(update.PriceUsd))
			.TryAdd(Level1Fields.LastTradeTime, time)
			.TryAdd(Level1Fields.Volume, NonNegative(update.VolumeUsd24Hours))
			.TryAdd(Level1Fields.Change,
				update.PriceChangePercentage24Hours), cancellationToken);
			await ConsumeLiveItemAsync(subscription, cancellationToken);
		}
	}

	private async ValueTask OnOnchainTradeAsync(
		CoinGeckoOnchainTradeUpdate update, CancellationToken cancellationToken)
	{
		if (update?.Network.IsEmpty() != false || update.PoolAddress.IsEmpty() ||
			update.PriceUsd is null || update.TokenAmount is null)
			return;
		var time = (update.Timestamp ?? ToUnix(CurrentTime)).ToCoinGeckoTime();
		var tradeId = CreateTradeId(update.TransactionHash, null, time,
			update.TokenAmount.Value);
		if (!RememberTrade(tradeId))
			return;
		LiveSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _liveSubscriptions.Values.Where(item =>
				item.DataType == DataType.Ticks &&
				item.Key.Network.EqualsIgnoreCase(update.Network) &&
				AddressEquals(item.Key.PoolAddress, update.PoolAddress))];
		foreach (var subscription in subscriptions)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				DataTypeEx = DataType.Ticks,
				ServerTime = time,
				TradeStringId = tradeId,
				TradePrice = Positive(update.PriceUsd),
				TradeVolume = Positive(update.TokenAmount),
				OriginSide = update.Side switch
				{
					CoinGeckoSocketTradeSides.Buy => Sides.Buy,
					CoinGeckoSocketTradeSides.Sell => Sides.Sell,
					_ => null,
				},
			}, cancellationToken);
			await ConsumeLiveItemAsync(subscription, cancellationToken);
		}
	}

	private async ValueTask OnOnchainOhlcvAsync(
		CoinGeckoOnchainOhlcvUpdate update, CancellationToken cancellationToken)
	{
		if (update?.Network.IsEmpty() != false || update.PoolAddress.IsEmpty() ||
			update.Token != CoinGeckoOnchainTokens.Base ||
			update.Timestamp is null || update.Open is null || update.High is null ||
			update.Low is null || update.Close is null)
			return;
		var timeFrame = update.Interval.ToTimeFrame();
		var candle = new CoinGeckoCandle
		{
			OpenTime = update.Timestamp.Value.ToCoinGeckoTime(),
			Open = update.Open.Value,
			High = update.High.Value,
			Low = update.Low.Value,
			Close = update.Close.Value,
			Volume = update.Volume,
		};
		LiveSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _liveSubscriptions.Values.Where(item =>
				item.DataType.IsTFCandles && item.TimeFrame == timeFrame &&
				item.Key.Network.EqualsIgnoreCase(update.Network) &&
				AddressEquals(item.Key.PoolAddress, update.PoolAddress))];
		foreach (var subscription in subscriptions)
		{
			CoinGeckoCandle finished = null;
			var isNew = false;
			using (_sync.EnterScope())
			{
				if (!_liveSubscriptions.TryGetValue(subscription.TransactionId,
					out var current) || !ReferenceEquals(current, subscription) ||
					current.ActiveCandle?.OpenTime > candle.OpenTime)
					continue;
				if (current.ActiveCandle is not null &&
					current.ActiveCandle.OpenTime < candle.OpenTime)
					finished = current.ActiveCandle;
				current.ActiveCandle = candle;
				if (current.LastCountedCandle != candle.OpenTime)
				{
					current.LastCountedCandle = candle.OpenTime;
					isNew = true;
				}
			}
			if (finished is not null)
				await SendCandleAsync(subscription.SecurityId, finished, timeFrame,
					subscription.TransactionId, CandleStates.Finished,
					cancellationToken);
			await SendCandleAsync(subscription.SecurityId, candle, timeFrame,
				subscription.TransactionId, CandleStates.Active, cancellationToken);
			if (isNew)
				await ConsumeLiveItemAsync(subscription, cancellationToken);
		}
	}

	private async ValueTask ConsumeLiveItemAsync(LiveSubscription subscription,
		CancellationToken cancellationToken)
	{
		var isFinished = false;
		var isLast = false;
		CoinGeckoStreamKey streamKey = default;
		using (_sync.EnterScope())
		{
			if (!_liveSubscriptions.TryGetValue(subscription.TransactionId,
				out var current) || !ReferenceEquals(current, subscription))
				return;
			if (current.Remaining is not > 0 || --current.Remaining != 0)
				return;
			_liveSubscriptions.Remove(current.TransactionId);
			streamKey = ToStreamKey(current);
			isLast = !_liveSubscriptions.Values.Any(item =>
				ToStreamKey(item) == streamKey);
			isFinished = true;
		}
		if (!isFinished)
			return;
		await SendSubscriptionFinishedAsync(subscription.TransactionId,
			cancellationToken);
		if (isLast && _socket is not null)
			await _socket.UnsubscribeAsync(streamKey, cancellationToken);
	}

	private ValueTask SendCandleAsync(SecurityId securityId,
		CoinGeckoCandle candle, TimeSpan timeFrame, long transactionId,
		CandleStates state, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			OpenTime = candle.OpenTime,
			CloseTime = candle.OpenTime + timeFrame,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume ?? 0,
			State = state,
		}, cancellationToken);

	private static ExecutionMessage ToTradeMessage(CoinGeckoSecurityKey key,
		SecurityId securityId, CoinGeckoTradeResource resource,
		long transactionId)
	{
		var trade = resource?.Attributes;
		if (trade?.BlockTimestamp.IsEmpty() != false)
			return null;
		decimal? price;
		decimal? volume;
		if (AddressEquals(trade.FromTokenAddress, key.TokenAddress))
		{
			price = trade.FromPriceUsd.ParseCoinGeckoDecimal();
			volume = trade.FromTokenAmount.ParseCoinGeckoDecimal();
		}
		else if (AddressEquals(trade.ToTokenAddress, key.TokenAddress))
		{
			price = trade.ToPriceUsd.ParseCoinGeckoDecimal();
			volume = trade.ToTokenAmount.ParseCoinGeckoDecimal();
		}
		else
			return null;
		if (price is not > 0 || volume is not > 0)
			return null;
		var time = trade.BlockTimestamp.ParseCoinGeckoTime();
		return new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			DataTypeEx = DataType.Ticks,
			ServerTime = time,
			TradeStringId = CreateTradeId(trade.TransactionHash, resource.Id,
				time, volume.Value),
			TradePrice = price,
			TradeVolume = volume,
			OriginSide = trade.Kind switch
			{
				CoinGeckoTradeKinds.Buy => Sides.Buy,
				CoinGeckoTradeKinds.Sell => Sides.Sell,
				_ => null,
			},
		};
	}

	private async ValueTask FinishSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}

	private static bool Matches(CoinGeckoCoin coin, string value)
		=> value.IsEmpty() || coin?.Id.ContainsIgnoreCase(value) == true ||
			coin?.Symbol.ContainsIgnoreCase(value) == true ||
			coin?.Name.ContainsIgnoreCase(value) == true;

	private static DateTime EstimateFrom(DateTime to, TimeSpan timeFrame,
		int count)
	{
		try
		{
			var ticks = checked(timeFrame.Ticks * Math.Max(1, count));
			var result = to - TimeSpan.FromTicks(ticks);
			return result < DateTime.UnixEpoch ? DateTime.UnixEpoch : result;
		}
		catch (Exception error) when (error is OverflowException or
			ArgumentOutOfRangeException)
		{
			return DateTime.UnixEpoch;
		}
	}

	private static bool AddressEquals(string left, string right)
		=> left?.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true &&
			right?.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true
				? left.Equals(right, StringComparison.OrdinalIgnoreCase)
				: string.Equals(left, right, StringComparison.Ordinal);

	private static string CreateTradeId(string transactionHash, string fallback,
		DateTime time, decimal volume)
		=> string.Join(':', transactionHash.IsEmpty(fallback),
			ToUnixMilliseconds(time).ToString(CultureInfo.InvariantCulture),
			volume.ToString(CultureInfo.InvariantCulture));

	private static decimal? Positive(decimal? value)
		=> value is > 0 ? value : null;

	private static decimal? NonNegative(decimal? value)
		=> value is >= 0 ? value : null;

	private static long ToUnix(DateTime value)
		=> checked((long)(value.EnsureUtc() - DateTime.UnixEpoch).TotalSeconds);

	private static long ToUnixMilliseconds(DateTime value)
		=> checked((long)(value.EnsureUtc() - DateTime.UnixEpoch).TotalMilliseconds);
}
