namespace StockSharp.Transaq;

using System.Text;
using System.Text.RegularExpressions;

using Ecng.Configuration;

partial class TransaqMessageAdapter
{
	private readonly HashSet<(SecurityId id, SecurityTypes? type)> _registeredSecurityIds = [];
	private readonly CachedSynchronizedPairSet<int, DataType> _candlePeriods = new()
	{
		{ 1, TimeSpan.FromMinutes(1).TimeFrame() },
		{ 2, TimeSpan.FromMinutes(5).TimeFrame() },
		{ 3, TimeSpan.FromMinutes(15).TimeFrame() },
		{ 4, TimeSpan.FromHours(1).TimeFrame() },
		{ 5, TimeSpan.FromDays(1).TimeFrame() },
		{ 6, TimeSpan.FromDays(7).TimeFrame() },
	};
	private readonly SynchronizedDictionary<(SecurityId secId, int period), (long transId, List<CandleMessage> candles)> _candleTransactions = [];
	private readonly SynchronizedDictionary<int, string> _boards = [];
	private readonly SynchronizedSet<SecurityId> _quotes = [];
	private readonly SynchronizedSet<SecurityId> _useCreditSecurities = [];
	private readonly SynchronizedDictionary<(string secCode, int marketId), SecurityId> _secIdsByMarket = [];
	private readonly SynchronizedDictionary<SecurityId, string> _secBoards = [];

	/// <inheritdoc />
	protected override async ValueTask MarketDataAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		if (mdMsg.DataType2 == DataType.Level1)
		{
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

			var secId = (ToTransaq(mdMsg.SecurityId.SecurityCode), ToTransaq(mdMsg.SecurityId, mdMsg.SecurityType), (int)mdMsg.SecurityId.NativeAsInt);

			await SendCommand(mdMsg.IsSubscribe
						? new SubscribeMessage { Quotations = { secId } }
						: new UnsubscribeMessage { Quotations = { secId } }, cancellationToken);

			if (mdMsg.IsSubscribe)
				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (mdMsg.DataType2 == DataType.MarketDepth)
		{
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

			var secId = (ToTransaq(mdMsg.SecurityId.SecurityCode), ToTransaq(mdMsg.SecurityId, mdMsg.SecurityType), (int)mdMsg.SecurityId.NativeAsInt);

			await SendCommand(mdMsg.IsSubscribe
						? new SubscribeMessage { Quotes = { secId } }
						: new UnsubscribeMessage { Quotes = { secId } }, cancellationToken);

			if (mdMsg.IsSubscribe)
				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else if (mdMsg.DataType2 == DataType.Ticks)
		{
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

			if (mdMsg.IsSubscribe)
			{
				//Подписаться/отписаться на тики можно двумя способами:
				//SubscribeMessage/UnsubscribeMessage - тики приходят с момента подписки
				//SubscribeTicksMessage - Тики приходят с момента подиски(TradeNo = 0), или с любого номера. При повторном запросе отписка получения тиков по предыдущему запросу.

				//var command = new SubscribeMessage();
				//command.AllTrades.Add(security.GetTransaqId());
				//ApiClient.Send((command, ProcessResult));

				//---

				_registeredSecurityIds.Add((mdMsg.SecurityId, mdMsg.SecurityType));

				var command = new SubscribeTicksMessage { Filter = true }; //Filter только сделки нормально периода торгов

				foreach (var (id, type) in _registeredSecurityIds)
				{
					command.Items.Add(new SubscribeTicksSecurity
					{
						SecCode = ToTransaq(id.SecurityCode),
						Board = ToTransaq(id, type),
						// http://stocksharp.com/forum/yaf_postsm35978_Obnovlieniie-Tranzak-do-viersii-2-16-1.aspx#post35978
						//TradeNo = tuple.Item1.Equals(mdMsg.SecurityId) ? 1 : 0,
						TradeNo = 1,
					});
				}

				await SendCommand(command, cancellationToken);

				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			}
			else
			{
				//var command = new UnsubscribeMessage();
				//command.AllTrades.Add(security.GetTransaqId());
				//ApiClient.Send((command, ProcessResult));

				//---

				_registeredSecurityIds.RemoveWhere(t => t.id == mdMsg.SecurityId);

				var command = new SubscribeTicksMessage { Filter = true }; //Filter только сделки нормально периода торгов

				foreach (var (id, type) in _registeredSecurityIds)
				{
					command.Items.Add(new SubscribeTicksSecurity
					{
						SecCode = ToTransaq(id.SecurityCode),
						Board = ToTransaq(id, type),
						TradeNo = 0,
					});
				}

				await SendCommand(command, cancellationToken);
			}
		}
		else if (mdMsg.DataType2 == DataType.News)
		{
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

			if (mdMsg.IsSubscribe)
			{
				if (mdMsg.NewsId.IsEmpty())
				{
					var count = mdMsg.Count;

					if (count == null)
						count = MaxNewsHeaderCount;
					else
					{
						if (count < 0 || count > MaxNewsHeaderCount)
							throw new ArgumentOutOfRangeException(nameof(mdMsg), count, LocalizedStrings.InvalidValue);
					}

					await SendCommand(new RequestOldNewsMessage { Count = (int)count.Value }, cancellationToken);
				}
				else
				{
					await SendCommand(new RequestNewsBodyMessage { NewsId = mdMsg.NewsId.To<int>() }, cancellationToken);
				}

				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			}
		}
		else if (mdMsg.DataType2.IsTFCandles)
		{
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

			if (mdMsg.IsSubscribe)
			{
				var periodId = _candlePeriods[mdMsg.DataType2];
				var key = (mdMsg.SecurityId, periodId);

				_candleTransactions.Add(key, (mdMsg.TransactionId, []));

				var count = mdMsg.Count;

				if (count == null)
				{
					var now = DateTime.UtcNow;
					var from = mdMsg.From ?? now.AddDays(-365);
					var to = mdMsg.To ?? now;
					var range = new Range<DateTime>(from, to);
					var board = ConfigManager.TryGetService<IBoardMessageProvider>()?.Lookup(new() { Like = mdMsg.SecurityId.BoardCode }).FirstOrDefault() ?? new();

					count = range.GetTimeFrameCount(mdMsg.GetTimeFrame(), board.WorkingTime);
				}

				var command = new RequestHistoryDataMessage
				{
					SecCode = ToTransaq(mdMsg.SecurityId.SecurityCode),
					Board = ToTransaq(mdMsg.SecurityId, mdMsg.SecurityType),
					Period = periodId,
					Count = count.Value,
					Reset = mdMsg.To == null,
				};

				try
				{
					await SendCommand(command, cancellationToken);
				}
				catch (Exception)
				{
					_candleTransactions.Remove(key);
					throw;
				}
			}
		}
		else
		{
			await SendSubscriptionNotSupportedAsync(mdMsg.TransactionId, cancellationToken);
		}
	}

	/// <summary>
	/// Максимально-допустимое количество заголовков новостей.
	/// </summary>
	public const int MaxNewsHeaderCount = 100;

	private async ValueTask OnAllTradesResponse(AllTradesResponse response)
	{
		foreach (var tick in response.AllTrades)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				TradeId = tick.TradeNo,
				ServerTime = tick.TradeTime.ToDto(),
				SecurityId = CreateId(tick.SecCode, tick.Board, tick.SecId),
				TradePrice = tick.Price.ToDecimal(),
				TradeVolume = tick.Quantity,
				OriginSide = tick.BuySell.FromTransaq(),
				OpenInterest = tick.OpenInterest.DefaultAsNull(),
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = response.TransactionId
			}, default);
		}

		await SendSubscriptionFinishedAsync(response.TransactionId, default);
	}

	private ValueTask OnCandleKindsResponse(CandleKindsResponse response)
	{
		using (_candlePeriods.EnterScope())
		{
			_candlePeriods.Clear();

			foreach (var kind in response.Kinds)
				_candlePeriods.Add(kind.Id, TimeSpan.FromSeconds(kind.Period).TimeFrame().Immutable());
		}

		if (response.TransactionId > 0)
		{
			foreach (var dt in _candlePeriods.CachedValues)
			{
				return TrySuspendOutAsync(new DataTypeInfoMessage
				{
					FileDataType = dt,
					OriginalTransactionId = response.TransactionId,
				});
			}
		}

		return default;
	}

	private async ValueTask OnCandlesResponse(CandlesResponse response)
	{
		var secId = CreateId(response.SecCode, response.Board, response.SecId);

		var key = (secId, response.Period);

		if (!_candleTransactions.TryGetValue(key, out var pair))
		{
			await SendOutErrorAsync(LocalizedStrings.SubscriptionNotRegisteredEarly.Put(response.SecCode, response.Board, response.Period), default);
			return;
		}

		var transactionId = pair.transId;

		//var index = 0;
		var candles = pair.candles;
		var dt = _candlePeriods[response.Period];

		foreach (var candle in response.Candles)
		{
			var time = candle.Date.ToDto();

			candles.Add(new TimeFrameCandleMessage
			{
				OriginalTransactionId = transactionId,
				SecurityId = secId,
				DataType = dt,
				OpenPrice = candle.Open.ToDecimal() ?? 0,
				HighPrice = candle.High.ToDecimal() ?? 0,
				LowPrice = candle.Low.ToDecimal() ?? 0,
				ClosePrice = candle.Close.ToDecimal() ?? 0,
				TotalVolume = candle.Volume?.ToDecimal() ?? 0,
				OpenTime = time,
				OpenInterest = candle.Oi == null || (long)candle.Oi == 0 ? null : (decimal)candle.Oi,
				State = CandleStates.Finished,
			});
		}

		switch (response.Status)
		{
			case CandleResponseStatus.Finished:
			case CandleResponseStatus.Done:
			case CandleResponseStatus.NotAvailable:
				break;
			default:
				return;
		}

		_candleTransactions.Remove(key);

		candles = candles.OrderBy(c => c.OpenTime).ToList();

		foreach (var candle in candles)
			await SendOutMessageAsync(candle, default);

		await SendSubscriptionFinishedAsync(transactionId, default);
	}

	private ValueTask OnMarketsResponse(MarketsResponse response)
	{
		//foreach (var market in response.Markets)
		//{
		//	switch (market.Name.Trim())
		//	{
		//		case "ММВБ":
		//			_exchangeBoards.Add(market.Id, ExchangeBoard.MicexEqbr);
		//			break;
		//		case "FORTS":
		//			_exchangeBoards.Add(market.Id, ExchangeBoard.Forts);
		//			break;
		//		default:
		//			var board = new ExchangeBoard { Code = market.Name };
		//			_exchangeBoards.Add(market.Id, board);
		//			break;
		//	}
		//}

		return default;
	}

	private async ValueTask OnNewsBodyResponse(NewsBodyResponse response)
	{
		await SendOutMessageAsync(new NewsMessage
		{
			Id = response.Id.To<string>(),
			Story = response.Text,
			ServerTime = CurrentTime
		}, default);
	}

	private async ValueTask OnNewsHeaderResponse(NewsHeaderResponse response)
	{
		await SendOutMessageAsync(new NewsMessage
		{
			Source = response.Source,
			Headline = response.Title,
			Id = response.Id.To<string>(),
			Story = response.Text,
			ServerTime = response.TimeStamp?.ToDto() ?? CurrentTime,
		}, default);
	}

	private async ValueTask OnQuotationsResponse(QuotationsResponse response)
	{
		foreach (var quote in response.Quotations)
		{
			var message = new Level1ChangeMessage
			{
				SecurityId = CreateId(quote.SecCode, quote.Board, quote.SecId),
				ServerTime = CurrentTime,
			}

			.TryAdd(Level1Fields.AccruedCouponIncome, quote.AccruedIntValue?.ToDecimal())
			.TryAdd(Level1Fields.OpenPrice, quote.Open?.ToDecimal())
			.TryAdd(Level1Fields.HighPrice, quote.High?.ToDecimal())
			.TryAdd(Level1Fields.LowPrice, quote.Low?.ToDecimal())
			.TryAdd(Level1Fields.ClosePrice, quote.ClosePrice?.ToDecimal())
			.TryAdd(Level1Fields.BidsCount, quote.BidsCount)
			.TryAdd(Level1Fields.BidsVolume, (decimal?)quote.BidsVolume)
			.TryAdd(Level1Fields.AsksCount, quote.AsksCount)
			.TryAdd(Level1Fields.AsksVolume, (decimal?)quote.AsksVolume)
			.TryAdd(Level1Fields.HighBidPrice, quote.HighBid?.ToDecimal())
			.TryAdd(Level1Fields.LowAskPrice, quote.LowAsk?.ToDecimal())
			.TryAdd(Level1Fields.Yield, quote.Yield?.ToDecimal())
			.TryAdd(Level1Fields.MarginBuy, quote.BuyDeposit?.ToDecimal())
			.TryAdd(Level1Fields.MarginSell, quote.SellDeposit?.ToDecimal())
			.TryAdd(Level1Fields.HistoricalVolatility, quote.Volatility?.ToDecimal())
			.TryAdd(Level1Fields.TheorPrice, quote.TheoreticalPrice?.ToDecimal())
			.TryAdd(Level1Fields.Change, quote.Change?.ToDecimal())
			.TryAdd(Level1Fields.Volume, (decimal?)quote.VolToday)
			.TryAdd(Level1Fields.StepPrice, quote.PointCost?.ToDecimal())
			.TryAdd(Level1Fields.OpenInterest, (decimal?)quote.OpenInterest)
			.TryAdd(Level1Fields.TradesCount, quote.TradesCount)
			.TryAdd(Level1Fields.BestBidPrice, quote.BestBidPrice?.ToDecimal())
			.TryAdd(Level1Fields.BestBidVolume, (decimal?)quote.BestBidVolume)
			.TryAdd(Level1Fields.BestAskPrice, quote.BestAskPrice?.ToDecimal())
			.TryAdd(Level1Fields.BestAskVolume, (decimal?)quote.BestAskVolume)
			.TryAdd(Level1Fields.LastTradePrice, quote.LastTradePrice?.ToDecimal())
			.TryAdd(Level1Fields.LastTradeVolume, (decimal?)quote.LastTradeVolume)
			;

			if (quote.Status != null)
				message.TryAdd(Level1Fields.State, quote.Status.Value.FromTransaq());

			if (quote.LastTradeTime != null)
				message.Add(Level1Fields.LastTradeTime, quote.LastTradeTime.Value.ToDto());

			await SendOutMessageAsync(message, default);
		}
	}

	private async ValueTask OnQuotesResponse(QuotesResponse response)
	{
		foreach (var group in response.Quotes.GroupBy(q => CreateId(q.SecCode, q.Board, q.SecId)))
		{
			var secId = group.Key;
			var isSnapshot = _quotes.TryAdd(secId);

			var bids = new List<QuoteChange>();
			var asks = new List<QuoteChange>();

			foreach (var quote in group)
			{
				var price = quote.Price.ToDecimal() ?? 0;

				if (price == 0)
					continue;

				var buy = quote.Buy;
				if (buy != null)
				{
					if (buy < 0)
						buy = 0;

					bids.Add(new QuoteChange(price, buy.Value));
				}

				var sell = quote.Sell;
				if (sell != null)
				{
					if (sell < 0)
						sell = 0;

					asks.Add(new QuoteChange(price, sell.Value));
				}
			}

			await SendOutMessageAsync(new QuoteChangeMessage
			{
				SecurityId = secId,
				Bids = bids.ToArray(),
				Asks = asks.ToArray(),
				ServerTime = CurrentTime,
				State = isSnapshot ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment,
			}, default);
		}
	}

	private async ValueTask OnSecInfoResponse(SecInfoResponse response)
	{
		var securityId = CreateId(response.SecCode, response.Market, response.SecId);

		await TrySuspendOutAsync(new SecurityMessage
		{
			SecurityId = securityId,
			ExpiryDate = response.MatDate?.ApplyMoscow().UtcDateTime,
			OptionType = response.PutCall?.FromTransaq(),
			FaceValue = (decimal?)response.FaceValue,
			Currency = response.CurrencyId.FromMicexCurrencyName(this.AddErrorLog),
		});

		var l1Msg = new Level1ChangeMessage
		{
			SecurityId = securityId,
			ServerTime = CurrentTime,
		}

		.TryAdd(Level1Fields.MinPrice, response.MinPrice?.ToDecimal())
		.TryAdd(Level1Fields.MaxPrice, response.MaxPrice?.ToDecimal());

		var marginBuy = response.BuyDeposit?.ToDecimal();

		if (marginBuy == null || marginBuy == 0m)
			marginBuy = response.BgoBuy?.ToDecimal();

		var marginSell = response.SellDeposit?.ToDecimal();

		if (marginSell == null || marginSell == 0m)
			marginSell = response.BgoC?.ToDecimal();

		l1Msg.TryAdd(Level1Fields.MarginBuy, marginBuy);
		l1Msg.TryAdd(Level1Fields.MarginSell, marginSell);

		await SendOutMessageAsync(l1Msg, default);
	}

	private async ValueTask OnSecuritiesResponse(SecuritiesResponse response)
	{
		foreach (var security in response.Securities)
		{
			var securityId = CreateId(security.SecCode, security.Board, security.SecId);

			if (security.OpMaskUseCredit)
				_useCreditSecurities.Add(securityId);

			_secIdsByMarket.TryAdd2((security.SecCode.ToLowerInvariant(), security.Market.To<int>()), securityId);

			var secName = security.ShortName.DecodeFromHtml();

			await TrySuspendOutAsync(new SecurityMessage
			{
				Name = secName,
				ShortName = secName,
				SecurityId = securityId,
				Multiplier = security.LotSize,
				PriceStep = security.MinStep?.ToDecimal(),
				Decimals = security.Decimals,
				SecurityType = security.Type.FromTransaq(),
				Currency = security.CurrencyId.FromMicexCurrencyName(this.AddErrorLog),
			});

			var state = security.Active ? SecurityStates.Trading : SecurityStates.Stoped;

			await SendOutMessageAsync(
				new Level1ChangeMessage
				{
					SecurityId = securityId,
					ServerTime = CurrentTime,
				}
				.Add(Level1Fields.State, state)
				.TryAdd(Level1Fields.StepPrice, security.PointCost?.ToDecimal()),
				default);
		}
	}

	private async ValueTask OnTicksResponse(TicksResponse response)
	{
		foreach (var tick in response.Ticks)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				TradeId = tick.TradeNo,
				ServerTime = tick.TradeTime.ToDto(),
				SecurityId = CreateId(tick.SecCode, tick.Board, tick.SecId),
				TradePrice = tick.Price.ToDecimal(),
				TradeVolume = tick.Quantity,
				OriginSide = tick.BuySell.FromTransaq(),
				OpenInterest = tick.OpenInterest.DefaultAsNull(),
				DataTypeEx = DataType.Ticks,
			}, default);
		}
	}

	private async ValueTask OnBoardsResponse(BoardsResponse response)
	{
		foreach (var board in response.Boards)
		{
			var code = ToStockSharp(board.Id);

			await TrySuspendOutAsync(new BoardMessage
			{
				Code = code,
				ExchangeCode = BoardCodes.Moex,
			});

			_boards.TryAdd2(board.Market, code);
		}
	}

	private ValueTask OnPitsResponse(PitsResponse response)
	{
		//foreach (var pit in response.Pits)
		//{
		//	RaiseNewMessage(new SecurityMessage
		//	{
		//		SecurityId = new SecurityId
		//		{
		//			SecurityCode = pit.SecCode,
		//			BoardCode = pit.Board.FixBoardName(),
		//		},
		//		VolumeStep = pit.LotSize,
		//		PriceStep = pit.MinStep,
		//		OriginalTransactionId = _secLookupId,
		//	});
		//}

		return default;
	}

	private async ValueTask OnMessagesResponse(MessagesResponse response)
	{
		foreach (var message in response.Messages)
		{
			await SendOutMessageAsync(new NewsMessage
			{
				Source = message.From,
				Headline = message.Text,
				Story = message.Text,
				ServerTime = message.Date?.ToDto() ?? CurrentTime,
				Priority = message.Urgent ? NewsPriorities.High : NewsPriorities.Regular
			}, default);
		}
	}

	private SecurityId CreateId(string secCode, int market, int nativeId)
	{
		//if (market == 0)
		//	throw new ArgumentNullException(nameof(market));

		return _secIdsByMarket.TryGetValue2((secCode.ToLowerInvariant(), market))
			?? CreateId(secCode, _boards.TryGetValue(market) ?? market.ToString(), nativeId);
	}

	private SecurityId CreateId(string secCode, string board, int nativeId)
	{
		if (secCode.IsEmpty())
			throw new ArgumentNullException(nameof(secCode));

		if (board.IsEmpty())
			throw new ArgumentNullException(nameof(board));

		var secId = new SecurityId
		{
			SecurityCode = secCode.DecodeFromHtml().ToUpperInvariant(),
			BoardCode = ToStockSharp(board),
			//NativeAsInt = nativeId,
		};

		if (!board.EqualsIgnoreCase(_forstCode))
			_secBoards.TryAdd2(secId, board);

		return secId;
	}

	private const string _futBoard = "FUT";
	private const string _optBoard = "OPT";
	private static readonly string _forstCode = BoardCodes.Forts;

	private static string ToStockSharp(string board)
	{
		if (board.EqualsIgnoreCase(_futBoard) || board.EqualsIgnoreCase(_optBoard))
			board = _forstCode;

		return board;
	}

	private string ToTransaq(SecurityId securityId, SecurityTypes? secType)
	{
		var board = _secBoards.TryGetValue(securityId);

		if (board != null)
			return board;

		if (securityId.BoardCode.EqualsIgnoreCase(_forstCode))
		{
			return secType == SecurityTypes.Option ? _optBoard : _futBoard;
		}

		return securityId.BoardCode;
	}

	private static string ToTransaq(string secCode)
	{
		if (Regex.IsMatch(secCode, "^(S|E)(I|U)\\w\\d(?:(S|E)(I|U)\\w\\d)?$"))
		{
			var sb = new StringBuilder(secCode);
			sb[1] = char.ToLowerInvariant(secCode[1]);

			if (secCode.Length == 8)
				sb[5] = char.ToLowerInvariant(secCode[5]);

			secCode = sb.ToString();
		}
		else if (Regex.IsMatch(secCode, "^[SE][IU]\\d{3,7}"))
		{
			var sb = new StringBuilder(secCode);
			sb[1] = char.ToLowerInvariant(secCode[1]);

			secCode = sb.ToString();
		}

		return secCode;
	}
}