namespace StockSharp.CoinCap;

using Ecng.Configuration;

public partial class CoinCapMessageAdapter
{
	private readonly PairSet<string, string> _assets = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, string> _exchanges = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, TradesPusherClient> _tradesPushers = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, SecurityId> _pricesAssets = new(StringComparer.InvariantCultureIgnoreCase);
	private PricesPusherClient _pricesPusher;

	private static string GetBoardCode(string exchangeId)
	{
		var provider = ConfigManager.TryGetService<IBoardMessageProvider>();

		if (provider is null)
			return exchangeId;

		var board = provider.Lookup(new() { Like = exchangeId }).FirstOrDefault();
		return board?.Code ?? exchangeId;
	}

	private static Tuple<string, string> ToSymbols(SecurityId securityId)
	{
		var parts = securityId.SecurityCode.Split('/');

		if (parts.Length != 2)
			throw new InvalidOperationException(LocalizedStrings.WrongSecCode.Put(securityId));

		return Tuple.Create(parts[0], parts[1]);
	}

	private async ValueTask EnsureInitAsync(CancellationToken cancellationToken)
	{
		if (_assets.Count == 0)
		{
			foreach (var asset in await _httpClient.GetAssetsAsync(cancellationToken))
				_assets.Add(asset.Symbol, asset.Id);
		}

		if (_exchanges.Count == 0)
		{
			foreach (var exchange in await _httpClient.GetExchangesAsync(cancellationToken))
				_exchanges[GetBoardCode(exchange.ExchangeId)] = exchange.ExchangeId;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		await EnsureInitAsync(cancellationToken);

		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var market in await _httpClient.GetMarketsAsync(cancellationToken))
		{
			var secCode = $"{market.BaseSymbol}/{market.QuoteSymbol}".ToUpperInvariant();

			var secMsg1 = new SecurityMessage
			{
				SecurityId = new SecurityId { SecurityCode = secCode, BoardCode = GetBoardCode(market.ExchangeId), },
				OriginalTransactionId = lookupMsg.TransactionId,
			}.FillDefaultCryptoFields();

			if (secMsg1.IsMatch(lookupMsg, secTypes))
			{
				await SendOutMessageAsync(secMsg1, cancellationToken);
				if (--left <= 0)
					break;
			}

			var secMsg2 = new SecurityMessage
			{
				SecurityId = new SecurityId { SecurityCode = secCode, BoardCode = BoardCodes.CoinCap, },
				OriginalTransactionId = lookupMsg.TransactionId,
			}.FillDefaultCryptoFields();

			if (secMsg2.IsMatch(lookupMsg, secTypes))
			{
				await SendOutMessageAsync(secMsg2, cancellationToken);
				if (--left <= 0)
					break;
			}
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await EnsureInitAsync(cancellationToken);

		var symbols = ToSymbols(mdMsg.SecurityId);

		if (!symbols.Item2.EqualsIgnoreCase("USD") && !symbols.Item2.EqualsIgnoreCase("USDT"))
		{
			await SendSubscriptionNotSupportedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		if (!_assets.TryGetValue(symbols.Item1, out var asset))
		{
			await SendSubscriptionNotSupportedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			if (!_pricesAssets.ContainsKey(asset))
			{
				_pricesAssets.Add(asset, mdMsg.SecurityId);
				await ReCreatePricesPusherAsync(cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			if (_pricesAssets.Remove(asset))
				await ReCreatePricesPusherAsync(cancellationToken);
		}
	}

	private async ValueTask ReCreatePricesPusherAsync(CancellationToken cancellationToken)
	{
		if (_pricesPusher != null)
		{
			_pricesPusher.PricesChanged -= PricesPusherOnPricesChanged;
			_pricesPusher.Disconnect();
			_pricesPusher.Dispose();
			_pricesPusher = null;
		}

		if (_pricesAssets.Count == 0)
			return;

		_pricesPusher = new PricesPusherClient(_pricesAssets.Keys, ReConnectionSettings.AttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
		_pricesPusher.PricesChanged += PricesPusherOnPricesChanged;
		await _pricesPusher.ConnectAsync(cancellationToken);
	}

	private async ValueTask PricesPusherOnPricesChanged(IDictionary<string, double> prices, CancellationToken cancellationToken)
	{
		foreach (var kv in prices)
		{
			if (_pricesAssets.TryGetValue(kv.Key, out var secId))
			{
				await SendOutMessageAsync(new Level1ChangeMessage { SecurityId = secId, ServerTime = CurrentTime }
					.TryAdd(Level1Fields.LastTradePrice, (decimal)kv.Value), cancellationToken);
			}
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await EnsureInitAsync(cancellationToken);

		if (!_exchanges.TryGetValue(mdMsg.SecurityId.BoardCode, out var exchange))
		{
			await SendSubscriptionNotSupportedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			if (!_tradesPushers.ContainsKey(exchange))
			{
				var pusher = new TradesPusherClient(exchange, ReConnectionSettings.AttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
				_tradesPushers.Add(exchange, pusher);

				pusher.NewTrade += PusherOnNewTrade;
				await pusher.ConnectAsync(cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			if (_tradesPushers.TryGetValue(exchange, out var pusher))
			{
				pusher.NewTrade -= PusherOnNewTrade;
				pusher.Disconnect();
				pusher.Dispose();
				_tradesPushers.Remove(exchange);
			}
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var tf = mdMsg.GetTimeFrame();
		var tfName = tf.ToNative();

		if (!mdMsg.IsSubscribe)
			return;

		await EnsureInitAsync(cancellationToken);

		if (!_exchanges.TryGetValue(mdMsg.SecurityId.BoardCode, out var exchange))
		{
			await SendSubscriptionNotSupportedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbols = ToSymbols(mdMsg.SecurityId);
		var from = mdMsg.From ?? DateTime.MinValue;
		var to = mdMsg.To ?? DateTime.UtcNow;
		var left = mdMsg.Count ?? long.MaxValue;

		var candles = await _httpClient.GetCandlesAsync(exchange, tfName, _assets.GetValue(symbols.Item1), _assets.GetValue(symbols.Item2), (long?)from.ToUnix(false), (long)to.ToUnix(false), cancellationToken);

		foreach (var candle in candles.OrderBy(c => c.Period))
		{
			await ProcessCandleAsync(candle, mdMsg.SecurityId, tf, mdMsg.TransactionId, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private ValueTask ProcessCandleAsync(Ohlc candle, SecurityId secId, TimeSpan timeFrame, long originTransId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = secId,
			TypedArg = timeFrame,
			OpenPrice = candle.Open?.ToDecimal() ?? 0,
			ClosePrice = candle.Close?.ToDecimal() ?? 0,
			HighPrice = candle.High?.ToDecimal() ?? 0,
			LowPrice = candle.Low?.ToDecimal() ?? 0,
			TotalVolume = candle.Volume?.ToDecimal() ?? 0,
			OpenTime = candle.Period,
			State = CandleStates.Finished,
			OriginalTransactionId = originTransId,
		}, cancellationToken);
	}

	private ValueTask PusherOnNewTrade(Trade trade, CancellationToken cancellationToken)
	{
		if (!_assets.TryGetKey(trade.Base, out var baseAsset))
			baseAsset = trade.Base;

		if (!_assets.TryGetKey(trade.Quote, out var quoteAsset))
			quoteAsset = trade.Quote;

		var secId = new SecurityId { SecurityCode = $"{baseAsset}/{quoteAsset}", BoardCode = GetBoardCode(trade.Exchange) };

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = secId,
			ServerTime = trade.Timestamp,
			TradePrice = (decimal)trade.Price,
			TradeVolume = (decimal)trade.Volume,
			OriginSide = trade.Direction.ToSide(),
		}, cancellationToken);
	}
}
