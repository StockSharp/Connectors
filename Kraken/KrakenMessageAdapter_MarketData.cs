namespace StockSharp.Kraken;

public partial class KrakenMessageAdapter
{
	private readonly SynchronizedSet<long> _candlesTransactions = [];
	private readonly SynchronizedSet<long> _orderBooks = [];

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var pair in await _spotHttpClient.GetAssetPairs(default, default, cancellationToken))
		{
			var asset = pair.Value;

			var secMsg = new SecurityMessage
			{
				SecurityId = asset.AlternateName.ToStockSharp(),
				Name = asset.AlternateName,
				PriceStep = asset.PairDecimals.GetPriceStep(),
				Decimals = asset.PairDecimals,
				Multiplier = asset.LotMultiplier,
				VolumeStep = asset.LotDecimals.GetPriceStep(),
				SecurityType = SecurityTypes.CryptoCurrency,
				OriginalTransactionId = lookupMsg.TransactionId
			}.TryFillUnderlyingId(asset.Base.ToUpperInvariant());

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = asset.AlternateName.ToStockSharp(),
				ServerTime = CurrentTime,
			}
			.TryAdd(Level1Fields.CommissionTaker, asset.Fees?.FirstOrDefault().PercentFee)
			.TryAdd(Level1Fields.CommissionMaker, asset.FeesMaker?.FirstOrDefault().PercentFee), cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		var symbol = mdMsg.SecurityId.SecurityCode;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _spotPusherClient.SubscribeTicker(transId, symbol, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _spotPusherClient.UnSubscribeTicker(transId, symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		var symbol = mdMsg.SecurityId.SecurityCode;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is DateTime from)
			{
				//var to = mdMsg.To ?? DateTime.UtcNow;
				//var left = mdMsg.Count ?? long.MaxValue;

				var tradesRes = await _spotHttpClient.GetRecentTrades(mdMsg.SecurityId.ToSymbol(), default, cancellationToken);

				await ProcessTicks(mdMsg.TransactionId, mdMsg.SecurityId, tradesRes.Data.Values.SelectMany().ToArray(), cancellationToken);
			}

			if (!mdMsg.IsHistoryOnly())
				await _spotPusherClient.SubscribeTrades(transId, symbol, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _spotPusherClient.UnSubscribeTrades(transId, symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		var symbol = mdMsg.SecurityId.SecurityCode;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _spotPusherClient.SubscribeOrderBook(transId, symbol, mdMsg.MaxDepth, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _spotPusherClient.UnSubscribeOrderBook(transId, symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		var symbol = mdMsg.SecurityId.SecurityCode;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		var timeFrame = mdMsg.GetTimeFrame();
		var tf = (int)timeFrame.TotalMinutes;

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is DateTime from)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;
				var left = mdMsg.Count ?? long.MaxValue;

				const int count = 720;

				var last = from;

				while (last < to)
				{
					var (Data, Last) = await _spotHttpClient.GetOhlc(mdMsg.SecurityId.ToSymbol(), tf, (long)last.ToUnix(), cancellationToken);

					var arr = Data.Values.SelectMany().OrderBy(c => c.StartTime).ToArray();

					var index = 0;
					var needFinish = false;

					foreach (var ohlc in arr)
					{
						index++;

						var time = ohlc.StartTime.FromUnix();

						if (last > time)
							continue;

						if (to < time)
						{
							needFinish = true;
							break;
						}

						await ProcessCandle(transId, mdMsg.SecurityId, timeFrame, ohlc, index < arr.Length ? CandleStates.Finished : CandleStates.Active, cancellationToken);

						if (--left <= 0)
						{
							needFinish = true;
							break;
						}
					}

					if (needFinish)
						break;

					last += timeFrame.Multiply(count);
					await IterationInterval.Delay(cancellationToken);
				}
			}

			if (!mdMsg.IsHistoryOnly())
				await _spotPusherClient.SubscribeCandles(transId, symbol, tf, mdMsg.DataType2, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _spotPusherClient.UnSubscribeCandles(transId, symbol, tf, mdMsg.DataType2, cancellationToken);
	}

	private ValueTask ProcessCandle(long transId, SecurityId secId, TimeSpan timeFrame, Native.Spot.Model.Ohlc ohlc, CandleStates state, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = secId,
			TypedArg = timeFrame,
			OpenPrice = ohlc.Open,
			ClosePrice = ohlc.Close,
			HighPrice = ohlc.High,
			LowPrice = ohlc.Low,
			TotalVolume = ohlc.Volume,
			OpenTime = ohlc.StartTime.FromUnix(),
			TotalTicks = ohlc.Count,
			State = state,
			OriginalTransactionId = transId,
		}, cancellationToken);
	}

	private ValueTask SessionOnSubscriptionResponse(long transId, Exception error, CancellationToken cancellationToken)
	{
		if (_candlesTransactions.Contains(transId))
			return default;

		return SendSubscriptionReplyAsync(transId, cancellationToken, error);
	}

	private async ValueTask ProcessTicks(long transactionId, SecurityId secId, Native.Spot.Model.Trade[] trades, CancellationToken cancellationToken)
	{
		foreach (var trade in trades.OrderBy(t => t.Time))
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = secId,
				OriginSide = trade.Side.ToSide(),
				TradePrice = (decimal)trade.Price,
				TradeVolume = (decimal)trade.Volume,
				ServerTime = trade.Time.FromUnix(),
				OriginalTransactionId = transactionId,
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnTickerChanged(long transactionId, string symbol, Native.Spot.Model.TickerInfo info, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(Level1Fields.HighPrice, (decimal?)info.High.FirstOr())
		.TryAdd(Level1Fields.LowPrice, (decimal?)info.Low.FirstOr())
		.TryAdd(Level1Fields.ClosePrice, (decimal?)info.Closed.FirstOr())
		.TryAdd(Level1Fields.OpenPrice, (decimal?)info.Open.FirstOr())
		.TryAdd(Level1Fields.Volume, (decimal?)info.Volume.FirstOr())
		.TryAdd(Level1Fields.VWAP, (decimal?)info.VWAP.FirstOr())
		.TryAdd(Level1Fields.BestBidPrice, (decimal?)info.Bid.FirstOr())
		.TryAdd(Level1Fields.BestAskPrice, (decimal?)info.Ask.FirstOr())
		.TryAdd(Level1Fields.BestBidVolume, (decimal?)info.Bid.ElementAtOrDefault(2))
		.TryAdd(Level1Fields.BestAskVolume, (decimal?)info.Ask.ElementAtOrDefault(2))
		.TryAdd(Level1Fields.TradesCount, info.Trades.FirstOr()), cancellationToken);
	}

	private ValueTask SessionOnNewTrades(long transactionId, string symbol, Native.Spot.Model.Trade[] trades, CancellationToken cancellationToken)
	{
		return ProcessTicks(0, symbol.ToStockSharp(), trades, cancellationToken);
	}

	private async ValueTask SessionOnOrderBookChanged(long transactionId, string symbol, Native.Spot.Model.OrderBook book, CancellationToken cancellationToken)
	{
		var state = _orderBooks.TryAdd(transactionId) ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment;

		static QuoteChange ToChange(Native.Spot.Model.OrderBookEntry entry)
			=> new((decimal)entry.Price, (decimal)entry.Volume);

		var bids = new List<QuoteChange>();
		var asks = new List<QuoteChange>();

		double maxTimestamp = 0;

		foreach (var entry in book.Bids)
		{
			bids.Add(ToChange(entry));
			maxTimestamp = maxTimestamp.Max(entry.Timestamp);
		}

		foreach (var entry in book.Asks)
		{
			asks.Add(ToChange(entry));
			maxTimestamp = maxTimestamp.Max(entry.Timestamp);
		}

		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = maxTimestamp.Abs() < double.Epsilon ? CurrentTime : maxTimestamp.FromUnix(),
			Bids = [.. bids],
			Asks = [.. asks],
			State = state,
		}, cancellationToken);
	}

	private ValueTask SessionOnNewCandle(long transactionId, string symbol, int interval, Native.Spot.Model.Ohlc candle, CancellationToken cancellationToken)
	{
		return ProcessCandle(transactionId, symbol.ToStockSharp(), TimeSpan.FromMinutes(interval), candle, CandleStates.Active, cancellationToken);
	}

	private ValueTask SessionOnSystemUpdated(string status, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new BoardStateMessage
		{
			ServerTime = CurrentTime,
			BoardCode = BoardCodes.Kraken,
			State = status.ToSessionState(),
		}, cancellationToken);
	}
}
