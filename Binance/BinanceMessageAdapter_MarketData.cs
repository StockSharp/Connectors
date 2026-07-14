namespace StockSharp.Binance;

using System.Threading.Channels;

using Nito.AsyncEx;

public partial class BinanceMessageAdapter
{
	private enum DepthStates
	{
		None,
		Snapshot,
		Incremental,
		Failed,
	}

	private class DepthInfo
	{
		public DepthInfo(BinanceSections section, string symbol)
		{
			if (symbol.IsEmpty())
				throw new ArgumentNullException(nameof(symbol));

			Section = section;
			Symbol = symbol;
		}

		public BinanceSections Section { get; }
		public string Symbol { get; }

		public AsyncLock Sync { get; } = new();

		public DepthStates State { get; set; }
		public int ErrorCount { get; set; }

		public long LastUpdateId { get; set; }

		public List<OrderBook> Increments { get; } = [];
	}

	private readonly SynchronizedDictionary<(BinanceSections, string), DepthInfo> _orderBooks = [];
	private readonly SynchronizedDictionary<(SecurityId, TimeSpan), long> _candleTransactions = [];
	private Channel<DepthInfo> _snapshotRequests;

	private const int _maxFetch = 1000;

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var section in NonMarginSections)
		{
			var secType = section.ToSecurityType();

			if (secTypes.Count > 0 && !secTypes.Contains(secType))
				continue;

			foreach (var symbol in await _httpClient.GetSymbols(section, cancellationToken))
			{
				cancellationToken.ThrowIfCancellationRequested();

				var priceFilter = symbol.Filters?.FirstOrDefault(f => f.FilterType == "PRICE_FILTER");
				var volFilter = symbol.Filters?.FirstOrDefault(f => f.FilterType == "LOT_SIZE");
				var notionalFilter = symbol.Filters?.FirstOrDefault(f => f.FilterType == "MIN_NOTIONAL");

				var secMsg = new SecurityMessage
				{
					SecurityId = symbol.Name.ToStockSharp(section),
					SecurityType = secType,
					OriginalTransactionId = lookupMsg.TransactionId,
					Decimals = symbol.PricePrecision > 0 ? symbol.PricePrecision : symbol.BaseAssetPrecision,
					PriceStep = priceFilter?.TickSize.To<decimal?>(),
					VolumeStep = volFilter?.StepSize.To<decimal?>(),
					MinVolume = volFilter?.MinQty.To<decimal?>(),
					MaxVolume = volFilter?.MaxQty.To<decimal?>(),
					UnderlyingSecurityMinVolume = notionalFilter?.MinNotional.To<decimal?>(),
					ExpiryDate = symbol.DeliveryDate,
				}.TryFillUnderlyingId(symbol.BaseAsset.ToUpperInvariant());

				if (!secMsg.IsMatch(lookupMsg, secTypes))
					continue;

				await SendOutMessageAsync(secMsg, cancellationToken);

				if (symbol.Status.ToSecurityState() == SecurityStates.Stoped)
				{
					await SendOutMessageAsync(new Level1ChangeMessage
					{
						SecurityId = secMsg.SecurityId,
						ServerTime = CurrentTime
					}.Add(Level1Fields.State, SecurityStates.Stoped), cancellationToken);
				}

				if (--left <= 0)
					break;
			}

			if (left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		var (section, symbol) = secId.ToNative();

		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
			{
				_pusherClient.SubscribeTicker(section, symbol);
				_pusherClient.SubscribeBookTicker(section, symbol);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_pusherClient.UnSubscribeTicker(section, symbol);
			_pusherClient.UnSubscribeBookTicker(section, symbol);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		var (section, symbol) = secId.ToNative();

		var mls = (int?)mdMsg.RefreshSpeed?.TotalMilliseconds ??
			(section == BinanceSections.Futures ? 0 : 100);

		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
			{
				GetDepthInfo(section, symbol).State = DepthStates.None;
				_pusherClient.SubscribeOrderBook(section, symbol, mls);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			_pusherClient.UnSubscribeOrderBook(section, symbol, mls);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		var (section, symbol) = secId.ToNative();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is not null)
			{
				var from = mdMsg.From.Value;
				var to = mdMsg.To ?? DateTime.UtcNow;
				var left = mdMsg.Count ?? long.MaxValue;

				var fromId = await _httpClient.GetFirstTradeIdFromTime(section, symbol, from, cancellationToken);

				if (fromId == 0)
					this.AddWarningLog("unable to find first trade id for {0}({1}), from={2}", symbol, section, from);

				var needFinish = false;

				while (fromId != 0 && !needFinish && left > 0)
				{
					var trades = (await _httpClient.GetHistoricalTrades(section, symbol, fromId, _maxFetch, cancellationToken)).ToArray();
					var hasNew = false;

					foreach (var trade in trades.OrderBy(t => t.Time))
					{
						cancellationToken.ThrowIfCancellationRequested();

						if (trade.Time > to)
						{
							needFinish = true;
							break;
						}

						if (fromId >= trade.Id)
							continue;

						await SendOutMessageAsync(new ExecutionMessage
						{
							DataTypeEx = DataType.Ticks,
							SecurityId = secId,
							TradeId = trade.Id,
							TradePrice = (decimal)trade.Price,
							TradeVolume = (decimal)trade.Quantity,
							ServerTime = trade.Time,
							OriginSide = trade.IsBuyerMaker ? Sides.Buy : Sides.Sell,
							OriginalTransactionId = transId,
						}, cancellationToken);

						fromId = trade.Id;
						hasNew = true;

						if (--left <= 0)
							break;
					}

					if (left <= 0 || !hasNew || trades.Length < (_maxFetch / 2))
						break;
					else
					{
						await IterationInterval.Delay(cancellationToken);
						fromId++;
					}
				}
			}

			if (!mdMsg.IsHistoryOnly())
				_pusherClient.SubscribeTrades(section, symbol);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_pusherClient.UnSubscribeTrades(section, symbol);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		var (section, symbol) = secId.ToNative();

		var tf = mdMsg.GetTimeFrame();
		var tfName = tf.ToNative();
		var subKey = (secId, tf);

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is not null)
			{
				var from = mdMsg.From.Value;
				var to = mdMsg.To ?? DateTime.UtcNow;
				var left = mdMsg.Count ?? long.MaxValue;

				var last = from;

				while (true)
				{
					var candles = (await _httpClient.GetCandles(section, symbol, tfName, (long)from.ToUnix(false), null, _maxFetch, cancellationToken)).ToArray();

					if (candles.IsEmpty())
					{
						from = from.AddDays(1);

						if (from > to)
							break;

						await IterationInterval.Delay(cancellationToken);
						continue;
					}

					var needFinish = false;
					var hasNew = false;

					foreach (var candle in candles.OrderBy(t => t.StartTime))
					{
						cancellationToken.ThrowIfCancellationRequested();

						var timestamp = candle.StartTime.FromUnix(false);

						if (timestamp > to)
						{
							needFinish = true;
							break;
						}

						if (timestamp <= last)
							continue;

						await SendOutMessageAsync(new TimeFrameCandleMessage
						{
							SecurityId = mdMsg.SecurityId,
							TypedArg = tf,
							OpenPrice = candle.Open,
							ClosePrice = candle.Close,
							HighPrice = candle.High,
							LowPrice = candle.Low,
							TotalVolume = candle.AssetVolume,
							BuyVolume = candle.TakerBuyAssetVolume,
							SellVolume = candle.AssetVolume - candle.TakerBuyAssetVolume,
							OpenTime = timestamp,
							State = CandleStates.Finished,
							TotalTicks = candle.TradesCount,
							OriginalTransactionId = mdMsg.TransactionId,
						}, cancellationToken);

						last = timestamp;
						hasNew = true;

						if (--left <= 0)
							break;
					}

					if (left <= 0 || !hasNew || needFinish)
						break;

					from = last + tf;
					await IterationInterval.Delay(cancellationToken);
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				_candleTransactions.Add(subKey, transId);
				_pusherClient.SubscribeCandles(section, symbol, tfName);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_candleTransactions.Remove(subKey);
			_pusherClient.UnSubscribeCandles(section, symbol, tfName);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnOrderLogSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var transId = mdMsg.TransactionId;

		var (section, symbol) = secId.ToNative();

		if (!section.IsCommonFutures())
		{
			await SendSubscriptionNotSupportedAsync(transId, cancellationToken);
			return;
		}

		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
				_pusherClient.SubscribeOrderLog(section, symbol);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			_pusherClient.UnSubscribeOrderLog(section, symbol);
	}

	private ValueTask SessionOnTickerChanged(BinanceSections section, Ticker ticker, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Symbol.ToStockSharp(section),
			ServerTime = ticker.EventTime == default ? CurrentTime : ticker.EventTime,
		}
		.TryAdd(Level1Fields.Change, (decimal?)ticker.PriceChange)
		.TryAdd(Level1Fields.OpenPrice, (decimal?)ticker.Open)
		.TryAdd(Level1Fields.HighPrice, (decimal?)ticker.High)
		.TryAdd(Level1Fields.LowPrice, (decimal?)ticker.Low)
		.TryAdd(Level1Fields.VWAP, (decimal?)ticker.VWAP)
		.TryAdd(Level1Fields.TradesCount, ticker.TradesCount)
		.TryAdd(Level1Fields.LastTradeId, ticker.LastTradeId)
		.TryAdd(Level1Fields.LastTradePrice, (decimal?)ticker.CurrClose)
		.TryAdd(Level1Fields.LastTradeVolume, (decimal?)ticker.CloseQuantity)
		.TryAdd(Level1Fields.BestBidPrice, (decimal?)ticker.BestBidPrice)
		.TryAdd(Level1Fields.BestBidVolume, (decimal?)ticker.BestBidQuantity)
		.TryAdd(Level1Fields.BestAskPrice, (decimal?)ticker.BestAskPrice)
		.TryAdd(Level1Fields.BestAskVolume, (decimal?)ticker.BestAskQuantity)
		.TryAdd(Level1Fields.Volume, (decimal?)ticker.AssetVolume), cancellationToken);
	}

	private ValueTask SessionOnNewTrade(BinanceSections section, Trade trade, CancellationToken cancellationToken)
	{
		if (section != BinanceSections.Spot && !trade.Source.EqualsIgnoreCase("MARKET"))
		{
			this.AddVerboseLog("ignore tick src='{0}'", trade.Source);
			return default;
		}

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Symbol.ToStockSharp(section),
			TradeId = trade.Id,
			TradePrice = (decimal)trade.Price,
			TradeVolume = (decimal)trade.Quantity,
			ServerTime = trade.Time,
			OriginSide = trade.IsMarketMaker ? Sides.Sell : Sides.Buy,
			OrderBuyId = trade.Buyer.DefaultAsNull(),
			OrderSellId = trade.Seller.DefaultAsNull(),
		}, cancellationToken);
	}

	private bool? CanProcess(DepthInfo info, OrderBook book)
	{
		var lastId = info.LastUpdateId;

		if (info.Section.IsCommonFutures())
		{
			if (book.FutLastUpdateId == lastId)
			{
				info.LastUpdateId = book.LastUpdateId;
				return true;
			}
			else if (book.FutLastUpdateId > lastId)
			{
				this.AddDebugLog($"{book.Symbol}: {lastId}<{book.FutLastUpdateId}");
				return false;
			}
			else
			{
				return null;
			}
		}
		else
		{
			var currId = book.FirstUpdateId;

			if ((lastId + 1) < currId)
			{
				// gap
				info.LastUpdateId = book.LastUpdateId;
				return true;
				//return false;
			}
			else if (lastId >= currId)
			{
				this.AddDebugLog($"{book.Symbol}: {currId}<={lastId}");

				return null;
			}

			info.LastUpdateId = book.LastUpdateId;
			return true;
		}
	}

	private Task StartSnapshotsThread(CancellationToken cancellationToken)
	{
		return Task.Run(async () =>
		{
			try
			{
				var sleep = TimeSpan.FromSeconds(0.6);
				var reader = _snapshotRequests.Reader;

				await foreach (var info in reader.ReadAllAsync(cancellationToken).WithEnforcedCancellation(cancellationToken))
				{
					await sleep.Delay(cancellationToken);

					const int maxError = 10;

					for (var i = 0; i < maxError; i++)
					{
						try
						{
							var section = info.Section;

							var depth = await _httpClient.GetDepth(section, info.Symbol, 1000, cancellationToken);

							await SendOutMessageAsync(new QuoteChangeMessage
							{
								SecurityId = info.Symbol.ToStockSharp(section),
								Bids = depth.Bids?.Select(e => e.ToChange()).ToArray() ?? [],
								Asks = depth.Asks?.Select(e => e.ToChange()).ToArray() ?? [],
								State = QuoteChangeStates.SnapshotComplete,
								ServerTime = CurrentTime,
								SeqNum = depth.LastUpdateId,
							}, cancellationToken);

							using (await info.Sync.LockAsync(cancellationToken))
							{
								info.ErrorCount = 0;
								info.LastUpdateId = depth.LastUpdateId;

								if (section.IsCommonFutures())
									info.Increments.RemoveWhere(b => b.LastUpdateId < info.LastUpdateId);
								else
									info.Increments.RemoveWhere(b => b.LastUpdateId <= info.LastUpdateId);

								info.Increments.Sort((b1, b2) => b1.FirstUpdateId.CompareTo(b2.FirstUpdateId));

								var gap = false;

								while (!gap && info.Increments.Count > 0)
								{
									var increment = info.Increments[0];

									switch (CanProcess(info, increment))
									{
										case true:
											await ProcessIncrementBook(section, increment, cancellationToken);
											info.Increments.RemoveAt(0);
											continue;
										case null:
											// stored increments can be duplicated
											//throw new InvalidOperationException("res=null");
											info.Increments.RemoveAt(0);
											continue;
										default:
											gap = true;
											break;
									}
								}

								if (gap)
								{
									if (i == (maxError - 1))
									{
										this.AddErrorLog($"Depth {info.Symbol} max attempts.");

										info.Increments.Clear();

										this.AddErrorLog($"{info.Symbol}: {info.State}->{DepthStates.Failed}");
										info.State = DepthStates.Failed;
										break;
									}

									continue;
								}
								else
								{
									this.AddInfoLog($"{info.Symbol}: {info.State}->{DepthStates.Incremental}");
									info.State = DepthStates.Incremental;
								}

								break;
							}
						}
						catch (Exception ex)
						{
							if (cancellationToken.IsCancellationRequested)
								break;

							this.AddErrorLog(ex);

							using (await info.Sync.LockAsync(cancellationToken))
							{
								info.ErrorCount++;

								if (info.ErrorCount < maxError)
									continue;

								this.AddErrorLog($"Depth {info.Symbol} max errors.");

								this.AddErrorLog($"{info.Symbol}: {info.State}->{DepthStates.Failed}");
								info.State = DepthStates.Failed;
								break;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				if (!cancellationToken.IsCancellationRequested)
					await SendOutErrorAsync(ex, cancellationToken);
			}
		}, cancellationToken);
	}

	private DepthInfo GetDepthInfo(BinanceSections section, string symbol)
		=> _orderBooks.SafeAdd((section, symbol.ToUpperInvariant()), key => new(key.Item1, key.Item2));

	private async ValueTask SessionOnOrderBookChanged(BinanceSections section, OrderBook book, CancellationToken cancellationToken)
	{
		var info = GetDepthInfo(section, book.Symbol);

		void ToSnapshot(bool isWarning)
		{
			if (isWarning)
				this.AddWarningLog($"{book.Symbol}: {info.State}->{DepthStates.Snapshot}");
			else
				this.AddInfoLog($"{book.Symbol}: {info.State}->{DepthStates.Snapshot}");

			info.State = DepthStates.Snapshot;
			info.Increments.Add(book);

			_snapshotRequests.Writer.TryWrite(info);
		}

		using (await info.Sync.LockAsync(cancellationToken))
		{
			switch (info.State)
			{
				case DepthStates.None:
					ToSnapshot(false);
					return;
				case DepthStates.Snapshot:
					info.Increments.Add(book);
					return;
				case DepthStates.Incremental:
					break;
				case DepthStates.Failed:
					return;
				default:
					throw new InvalidOperationException(info.State.To<string>());
			}

			switch (CanProcess(info, book))
			{
				case null:
					return;
				case false:
					ToSnapshot(true);
					return;
			}
		}

		await ProcessIncrementBook(section, book, cancellationToken);
	}

	private ValueTask ProcessIncrementBook(BinanceSections section, OrderBook book, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = book.Symbol.ToStockSharp(section),
			Bids = book.Bids?.Select(e => e.ToChange()).ToArray() ?? [],
			Asks = book.Asks?.Select(e => e.ToChange()).ToArray() ?? [],
			State = QuoteChangeStates.Increment,
			ServerTime = book.EventTime,
			SeqNum = book.FutLastUpdateId ?? book.LastUpdateId,
		}, cancellationToken);
	}

	private ValueTask SessionOnNewCandle(BinanceSections section, Ohlc ohlc, CancellationToken cancellationToken)
	{
		var secId = ohlc.Symbol.ToStockSharp(section);
		var candle = ohlc.Candle;
		var tf = candle.Interval.ToTimeFrame();

		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = secId,
			TypedArg = tf,
			OpenPrice = candle.Open,
			ClosePrice = candle.Close,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			TotalVolume = candle.AssetVolume,
			BuyVolume = candle.TakerBuyAssetVolume,
			SellVolume = candle.AssetVolume - candle.TakerBuyAssetVolume,
			OpenTime = candle.StartTime,
			State = candle.IsFormed ? CandleStates.Finished : CandleStates.Active,
			TotalTicks = candle.TradesCount,
			OriginalTransactionId = _candleTransactions.TryGetValue((secId, tf)),
			SeqNum = candle.LastTradeId,
		}, cancellationToken);
	}

	private ValueTask SessionOnNewOrderLog(BinanceSections section, FutOrderLog ol, CancellationToken cancellationToken)
	{
		var order = ol.Order;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.OrderLog,
			ServerTime = order.TradeTime ?? ol.EventTime,
			SecurityId = order.Symbol.ToStockSharp(section),
			Side = order.Side.ToSide(),
			OrderPrice = (decimal)order.Price,
			OrderType = order.Type.ToOrderType(out var postOnly, out _),
			OrderState = order.Status.ToOrderState(),
			OrderVolume = (decimal?)(order.LastTradeSize ?? order.Quantity),
			Balance = (decimal?)(order.Quantity - order.AccumFilled),
			TimeInForce = order.Tif.ToTimeInForce(out var postOnly2),
			PostOnly = postOnly ?? postOnly2,
			AveragePrice = (decimal?)order.AveragePrice,
		}, cancellationToken);
	}
}
