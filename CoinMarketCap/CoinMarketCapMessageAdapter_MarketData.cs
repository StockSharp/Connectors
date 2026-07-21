namespace StockSharp.CoinMarketCap;

public partial class CoinMarketCapMessageAdapter
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
		if (!message.SecurityId.BoardCode.IsEmpty() &&
			!message.SecurityId.BoardCode.EqualsIgnoreCase(
				BoardCodes.CoinMarketCap))
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}

		var value = (message.SecurityId.Native as string)
			.IsEmpty(message.SecurityId.SecurityCode).IsEmpty(message.Name)?.Trim();
		if (CoinMarketCapSecurityKey.TryParse(
			message.SecurityId.Native as string, out var nativeKey))
			value = nativeKey.Id.ToString(CultureInfo.InvariantCulture);
		var skip = Math.Max(0L, message.Skip ?? 0);
		var left = Math.Min(message.Count ?? MaximumItems, MaximumItems);
		var quoteCurrency =
			CoinMarketCapExtensions.NormalizeCurrency(QuoteCurrency);

		foreach (var coin in await GetCoinsAsync(cancellationToken))
		{
			if (left <= 0)
				break;
			if (!Matches(coin, value))
				continue;
			var key = ToKey(coin, quoteCurrency);
			var security = ToSecurityMessage(key, message.TransactionId);
			if (value.IsEmpty() && !security.IsMatch(message, securityTypes))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			await SendOutMessageAsync(security, cancellationToken);
			left--;
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
		var isHistoryOnly = message.IsHistoryOnly();
		if (!isHistoryOnly &&
			!key.QuoteCurrency.EqualsIgnoreCase("USD"))
			throw new NotSupportedException(
				"CoinMarketCap WebSocket prices are USD-denominated. Use USD for live Level 1 subscriptions.");
		var securityId = key.ToSecurityId();
		var sent = await SendLevel1Async(key, securityId,
			message.TransactionId, cancellationToken);
		var remaining = message.Count;
		if (sent && remaining is > 0)
			remaining--;
		if (isHistoryOnly || remaining == 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}
		await AddLiveSubscriptionAsync(message, key, securityId, remaining,
			cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		if (message.Count is <= 0)
		{
			await FinishSubscriptionAsync(message, cancellationToken);
			return;
		}
		if (!message.IsHistoryOnly())
			throw new NotSupportedException(
				"CoinMarketCap aggregated OHLCV candles are historical only.");
		if (AccessMode != CoinMarketCapAccessModes.ApiKey || Token.IsEmpty())
			throw new NotSupportedException(
				"CoinMarketCap historical OHLCV requires an API key and Startup plan or above.");

		var timeFrame = message.GetTimeFrame();
		var period = timeFrame.ToTimePeriod();
		var count = (message.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
			.To<int>();
		var to = (message.To ?? CurrentTime).EnsureUtc();
		var earliest = EstimateFrom(to, timeFrame, count);
		var from = message.From?.EnsureUtc() ?? earliest;
		if (from < earliest)
			from = earliest;
		if (from >= to)
			throw new ArgumentOutOfRangeException(nameof(message),
				"CoinMarketCap candle start time must be earlier than end time.");

		var key = await ResolveKeyAsync(message.SecurityId, cancellationToken);
		var response = await SafeRest().GetOhlcvAsync(key.Id,
			key.QuoteCurrency, period, Subtract(from, timeFrame), to, count,
			cancellationToken);
		if (response is not null && response.Id != key.Id)
			throw new InvalidDataException(
				$"CoinMarketCap returned cryptocurrency {response.Id} for requested ID {key.Id}.");

		var candles = new SortedDictionary<DateTime, CoinMarketCapCandle>();
		foreach (var row in response?.Quotes ?? [])
		{
			var candle = ToCandle(row, key.QuoteCurrency);
			if (candle is not null && candle.OpenTime >= from &&
				candle.OpenTime < to)
				candles[candle.OpenTime] = candle;
		}
		foreach (var candle in candles.Values.TakeLast(count))
			await SendCandleAsync(key.ToSecurityId(), candle, timeFrame,
				message.TransactionId, cancellationToken);
		await FinishSubscriptionAsync(message, cancellationToken);
	}

	private async ValueTask<CoinMarketCapMapEntry[]> GetCoinsAsync(
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			if (_coins.Count > 0)
				return OrderCoins(_coins.Values);
		}

		var downloaded = new Dictionary<int, CoinMarketCapMapEntry>();
		for (var start = 1; downloaded.Count < MaximumItems;)
		{
			var limit = Math.Min(5000, MaximumItems - downloaded.Count);
			var page = await SafeRest().GetMapPageAsync(start, limit,
				cancellationToken);
			foreach (var coin in page.Where(IsValidCoin))
				downloaded[coin.Id] = coin;
			if (page.Length < limit)
				break;
			start = checked(start + page.Length);
		}
		using (_sync.EnterScope())
		{
			if (_coins.Count == 0)
				foreach (var pair in downloaded)
					_coins[pair.Key] = pair.Value;
			return OrderCoins(_coins.Values);
		}
	}

	private async ValueTask<CoinMarketCapSecurityKey> ResolveKeyAsync(
		SecurityId securityId, CancellationToken cancellationToken)
	{
		if (CoinMarketCapSecurityKey.TryParse(securityId.Native as string,
			out var key))
			return key;
		var code = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		var separator = code.IndexOf('/');
		var identity = (separator > 0 ? code[..separator] : code).Trim();
		var quoteCurrency = separator > 0 && separator < code.Length - 1
			? CoinMarketCapExtensions.NormalizeCurrency(code[(separator + 1)..])
			: CoinMarketCapExtensions.NormalizeCurrency(QuoteCurrency);
		var coins = await GetCoinsAsync(cancellationToken);
		CoinMarketCapMapEntry coin = null;
		if (int.TryParse(identity, NumberStyles.None,
			CultureInfo.InvariantCulture, out var id) && id > 0)
			coin = coins.FirstOrDefault(item => item.Id == id);
		coin ??= coins.FirstOrDefault(item =>
			item.Slug.EqualsIgnoreCase(identity));
		if (coin is null)
		{
			var matches = coins.Where(item =>
				item.Symbol.EqualsIgnoreCase(identity)).Take(2).ToArray();
			if (matches.Length != 1)
				throw new InvalidOperationException(
					$"CoinMarketCap cryptocurrency '{identity}' is unknown or ambiguous. Use security lookup to preserve its API ID.");
			coin = matches[0];
		}
		return ToKey(coin, quoteCurrency);
	}

	private static CoinMarketCapSecurityKey ToKey(CoinMarketCapMapEntry coin,
		string quoteCurrency)
		=> new(coin.Id, quoteCurrency, coin.Symbol.Trim(), coin.Name.Trim(),
			coin.Slug?.Trim());

	private static SecurityMessage ToSecurityMessage(
		CoinMarketCapSecurityKey key, long originalTransactionId)
	{
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = key.ToSecurityId(),
			Name = key.Name.IsEmpty(key.Symbol),
			ShortName = key.Symbol.ToUpperInvariant(),
			Class = key.Slug.IsEmpty(key.Id.ToString(
				CultureInfo.InvariantCulture)),
			SecurityType = SecurityTypes.CryptoCurrency,
		};
		if (Enum.TryParse<CurrencyTypes>(key.QuoteCurrency, true,
			out var currency))
			message.Currency = currency;
		return message;
	}

	private async ValueTask<bool> SendLevel1Async(
		CoinMarketCapSecurityKey key, SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
	{
		var asset = await SafeRest().GetQuoteAsync(key.Id, key.QuoteCurrency,
			cancellationToken);
		var quote = asset?.Quotes?.FirstOrDefault(item =>
			item?.Symbol.EqualsIgnoreCase(key.QuoteCurrency) == true);
		if (quote is null)
			return false;
		var timeText = quote.LastUpdated.IsEmpty(asset.LastUpdated);
		var time = timeText.IsEmpty()
			? CurrentTime.EnsureUtc()
			: timeText.ParseCoinMarketCapTime();
		var price = Positive(quote.Price);
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.LastTradePrice, price)
		.TryAdd(Level1Fields.LastTradeTime, price is null ? null : time)
		.TryAdd(Level1Fields.Volume, NonNegative(quote.Volume24Hours))
		.TryAdd(Level1Fields.Change, quote.PriceChange24Hours),
			cancellationToken);
		return true;
	}

	private async ValueTask AddLiveSubscriptionAsync(MarketDataMessage message,
		CoinMarketCapSecurityKey key, SecurityId securityId, long? remaining,
		CancellationToken cancellationToken)
	{
		var subscription = new LiveSubscription
		{
			TransactionId = message.TransactionId,
			SecurityId = securityId,
			Key = key,
			Remaining = remaining,
		};
		var isFirst = false;
		using (_sync.EnterScope())
		{
			if (_liveSubscriptions.ContainsKey(message.TransactionId))
				throw new InvalidOperationException(
					$"CoinMarketCap subscription {message.TransactionId} already exists.");
			isFirst = !_liveSubscriptions.Values.Any(item =>
				item.Key.Id == key.Id);
			if (isFirst && _liveSubscriptions.Values.Select(static item =>
				item.Key.Id).Distinct().Count() >= 100)
				throw new InvalidOperationException(
					"CoinMarketCap WebSocket allows at most 100 distinct cryptocurrency subscriptions per connection.");
			_liveSubscriptions.Add(message.TransactionId, subscription);
		}
		try
		{
			if (isFirst)
				await GetOrCreateSocket().SubscribeAsync(key.Id, cancellationToken);
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
			isLast = !_liveSubscriptions.Values.Any(item =>
				item.Key.Id == removed.Key.Id);
		}
		if (isLast && _socket is not null)
			await _socket.UnsubscribeAsync(removed.Key.Id, cancellationToken);
	}

	private async ValueTask OnPriceAsync(CoinMarketCapSocketPrice update,
		long timestamp, CancellationToken cancellationToken)
	{
		if (update?.CryptoId <= 0 || timestamp < 0)
			return;
		LiveSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _liveSubscriptions.Values.Where(item =>
				item.Key.Id == update.CryptoId)];
		var time = timestamp.ToCoinMarketCapTime();
		foreach (var subscription in subscriptions)
		{
			var price = Positive(update.Price);
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = time,
			}
			.TryAdd(Level1Fields.LastTradePrice, price)
			.TryAdd(Level1Fields.LastTradeTime, price is null ? null : time)
			.TryAdd(Level1Fields.Volume, NonNegative(update.Volume24Hours))
			.TryAdd(Level1Fields.Change, update.PriceChange24Hours),
				cancellationToken);
			await ConsumeLiveItemAsync(subscription, cancellationToken);
		}
	}

	private async ValueTask ConsumeLiveItemAsync(LiveSubscription subscription,
		CancellationToken cancellationToken)
	{
		var isFinished = false;
		var isLast = false;
		using (_sync.EnterScope())
		{
			if (!_liveSubscriptions.TryGetValue(subscription.TransactionId,
				out var current) || !ReferenceEquals(current, subscription))
				return;
			if (current.Remaining is not > 0 || --current.Remaining != 0)
				return;
			_liveSubscriptions.Remove(current.TransactionId);
			isLast = !_liveSubscriptions.Values.Any(item =>
				item.Key.Id == current.Key.Id);
			isFinished = true;
		}
		if (!isFinished)
			return;
		await SendSubscriptionFinishedAsync(subscription.TransactionId,
			cancellationToken);
		if (isLast && _socket is not null)
			await _socket.UnsubscribeAsync(subscription.Key.Id, cancellationToken);
	}

	private static CoinMarketCapCandle ToCandle(CoinMarketCapOhlcv row,
		string quoteCurrency)
	{
		var quote = row?.Quote;
		var values = quote?.Values;
		if (row?.OpenTime.IsEmpty() != false ||
			!quote.Currency.EqualsIgnoreCase(quoteCurrency) || values?.Open is not >= 0 ||
			values.High is not >= 0 || values.Low is not >= 0 ||
			values.Close is not >= 0 || values.High < values.Low)
			return null;
		return new()
		{
			OpenTime = row.OpenTime.ParseCoinMarketCapTime(),
			Open = values.Open.Value,
			High = values.High.Value,
			Low = values.Low.Value,
			Close = values.Close.Value,
			Volume = NonNegative(values.Volume),
		};
	}

	private ValueTask SendCandleAsync(SecurityId securityId,
		CoinMarketCapCandle candle, TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
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
			State = CandleStates.Finished,
		}, cancellationToken);

	private async ValueTask FinishSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}

	private static bool Matches(CoinMarketCapMapEntry coin, string value)
		=> value.IsEmpty() ||
			coin.Id.ToString(CultureInfo.InvariantCulture).EqualsIgnoreCase(value) ||
			coin.Symbol.ContainsIgnoreCase(value) ||
			coin.Name.ContainsIgnoreCase(value) ||
			coin.Slug.ContainsIgnoreCase(value);

	private static bool IsValidCoin(CoinMarketCapMapEntry coin)
		=> coin?.Id > 0 && coin.IsActive == 1 &&
			!coin.Symbol.IsEmpty() && !coin.Name.IsEmpty();

	private static CoinMarketCapMapEntry[] OrderCoins(
		IEnumerable<CoinMarketCapMapEntry> coins)
		=> [.. coins.OrderBy(static coin => coin.Rank ?? int.MaxValue)
			.ThenBy(static coin => coin.Id)];

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

	private static DateTime Subtract(DateTime value, TimeSpan timeFrame)
		=> value - DateTime.UnixEpoch < timeFrame
			? DateTime.UnixEpoch
			: value - timeFrame;

	private static decimal? Positive(decimal? value)
		=> value is > 0 ? value : null;

	private static decimal? NonNegative(decimal? value)
		=> value is >= 0 ? value : null;
}
