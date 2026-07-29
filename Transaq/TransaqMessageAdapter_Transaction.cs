namespace StockSharp.Transaq;

partial class TransaqMessageAdapter
{
	private class OrderInfo(long transactionId, OrderTypes type, bool repo, bool rps)
	{
		public long TransactionId { get; } = transactionId;
		public OrderTypes Type { get; } = type;
		public bool Repo { get; } = repo;
		public bool Rps { get; } = rps;
	}

	private readonly SynchronizedDictionary<long, OrderInfo> _orders = [];
	private readonly SynchronizedSet<string> _unions = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedSet<string> _pfSubs = new(StringComparer.InvariantCultureIgnoreCase);

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var expDate = regMsg.TillDate.EnsureToday(null)?.To(destination: TimeHelper.Moscow);

		NewBaseOrderMessage command;

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
			{
				command = new NewOrderMessage
				{
					ByMarket = regMsg.OrderType == OrderTypes.Market,
					Quantity = regMsg.Volume.To<int>(),
					Unfilled = regMsg.TimeInForce.ToTransaq(),
					BuySell = regMsg.Side.ToTransaq(),
					Price = (double)(regMsg.OrderType == OrderTypes.Market ? 0 : regMsg.Price),
					ExpDate = expDate,
					BrokerRef = regMsg.Comment,
					Hidden = (int)(regMsg.Volume - (regMsg.VisibleVolume ?? regMsg.Volume)),
					UseCredit = _useCreditSecurities.Contains(regMsg.SecurityId),
				};

				break;
			}
			case OrderTypes.Conditional:
			{
				var cond = (TransaqOrderCondition)regMsg.Condition;

				if (cond.IsRepo)
				{
					var repo = cond.RepoInfo;

					if (repo.IsModified)
					{
						command = new NewMRepoOrderMessage
						{
							BuySell = regMsg.Side.ToTransaq(),
							CpFirmId = repo.Partner,
							MatchRef = repo.MatchRef,
							BrokerRef = regMsg.Comment,
							Price = (double)regMsg.Price,
							Quantity = regMsg.Volume.To<int>(),
							SettleCode = repo.SettleCode,
							RefundRate = (int?)repo.RefundRate,
							Value = (double?)repo.Value,
							Term = (int?)repo.Term,
							Rate = (int?)repo.Rate,
							StartDiscount = (int?)repo.StartDiscount,
							LowerDiscount = (int?)repo.LowerDiscount,
							UpperDiscount = (int?)repo.UpperDiscount,
							BlockSecurities = repo.BlockSecurities,
						};
					}
					else
					{
						command = new NewRepoOrderMessage
						{
							BuySell = regMsg.Side.ToTransaq(),
							CpFirmId = repo.Partner,
							MatchRef = repo.MatchRef,
							BrokerRef = regMsg.Comment,
							Price = (double)regMsg.Price,
							Quantity = regMsg.Volume.To<int>(),
							SettleCode = repo.SettleCode,
							RefundRate = (int?)repo.RefundRate,
							Rate = (int?)repo.Rate,
						};
					}
				}
				else if (cond.IsNtm)
				{
					var rps = cond.NtmInfo;

					command = new NewRpsOrderMessage
					{
						BuySell = regMsg.Side.ToTransaq(),
						CpFirmId = rps.Partner,
						MatchRef = rps.MatchRef,
						BrokerRef = regMsg.Comment,
						Price = (double)regMsg.Price,
						Quantity = regMsg.Volume.To<int>(),
						SettleCode = rps.SettleCode,
					};
				}
				else
				{
					switch (cond.Type)
					{
						case TransaqOrderConditionTypes.Algo:
						{
							command = new NewCondOrderMessage
							{
								ByMarket = regMsg.OrderType == OrderTypes.Market,
								Quantity = regMsg.Volume.To<int>(),
								BuySell = regMsg.Side.ToTransaq(),
								Price = (double)regMsg.Price,
								CondType = cond.AlgoType ?? TransaqAlgoOrderConditionTypes.None,
								CondValue = (double)(cond.AlgoValue ?? 0m),
								ValidAfterType = cond.AlgoValidAfterType ?? TransaqAlgoOrderValidTypes.TillCancelled,
								ValidAfter = cond.AlgoValidAfter,
								ValidBeforeType = cond.AlgoValidBeforeType ?? TransaqAlgoOrderValidTypes.TillCancelled,
								ValidBefore = cond.AlgoValidBefore,
								ExpDate = expDate,
								BrokerRef = regMsg.Comment,
								Hidden = (int)(regMsg.Volume - (regMsg.VisibleVolume ?? regMsg.Volume)),
								UseCredit = _useCreditSecurities.Contains(regMsg.SecurityId)
							};

							break;
						}
						default:
						{
							if (!cond.CheckConditionUnitType())
								throw new InvalidOperationException(LocalizedStrings.UnsupportedType.Put(cond));

							var stopOrder = new NewStopOrderMessage
							{
								BuySell = regMsg.Side.ToTransaq(),
								LinkedOrderNo = cond.LinkedOrderId.To<string>(),
								ExpDate = expDate,
								ValidFor = expDate?.ToUniversalTime(),
							};

							switch (cond.Type)
							{
								case TransaqOrderConditionTypes.StopLoss:
									stopOrder.StopLoss = TransaqHelper.CreateStopLoss(cond, regMsg.Comment);
									break;

								case TransaqOrderConditionTypes.TakeProfit:
									stopOrder.TakeProfit = TransaqHelper.CreateTakeProfit(cond, regMsg.Comment);
									break;

								case TransaqOrderConditionTypes.TakeProfitStopLoss:
									stopOrder.StopLoss = TransaqHelper.CreateStopLoss(cond, regMsg.Comment);
									stopOrder.TakeProfit = TransaqHelper.CreateTakeProfit(cond, regMsg.Comment);
									break;
							}

							command = stopOrder;
							break;
						}
					}
				}

				break;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		if (_unions.Contains(regMsg.PortfolioName))
			command.Union = regMsg.PortfolioName;
		else
			command.Client = regMsg.PortfolioName;

		command.SecCode = ToTransaq(regMsg.SecurityId.SecurityCode);
		command.Board = ToTransaq(regMsg.SecurityId, regMsg.SecurityType);

		var result = await SendCommand(command, cancellationToken);

		_orders.Add(result.TransactionId, new OrderInfo(regMsg.TransactionId, regMsg.OrderType ?? OrderTypes.Limit, command is NewRepoOrderMessage, command is NewRpsOrderMessage));
	}

	private KeyValuePair<long, OrderInfo>? TryGetOrderInfo(long transactionId)
	{
		using (_orders.EnterScope())
			return _orders.Where(p => p.Value.TransactionId == transactionId).FirstOr();
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var pair = TryGetOrderInfo(cancelMsg.OriginalTransactionId);

		if (pair == null)
			throw new InvalidOperationException(LocalizedStrings.NoSystemId.Put(cancelMsg.OriginalTransactionId));

		BaseCommandMessage command;

		var transId = pair.Value.Key;
		var info = pair.Value.Value;

		switch (info.Type)
		{
			case OrderTypes.Limit:
			case OrderTypes.Market:
				command = new CancelOrderMessage { TransactionId = transId };
				break;
			case OrderTypes.Conditional:
				if (info.Repo || info.Rps)
					command = new CancelNegDealMessage { TransactionId = transId };
				else
					command = new CancelStopOrderMessage { TransactionId = transId };

				break;
			//case OrderTypes.Repo:
			//case OrderTypes.ExtRepo:
			//case OrderTypes.Rps:
			//	command = new CancelNegDealMessage { TransactionId = transId };
			//	break;
			//case OrderTypes.Execute:
			//	command = new CancelReportMessage { TransactionId = transId };
			//	break;
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(info.Type, cancelMsg.OriginalTransactionId));
		}

		await SendCommand(command, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var pair = TryGetOrderInfo(replaceMsg.OriginalTransactionId);

		if (pair == null)
			throw new InvalidOperationException(LocalizedStrings.NoSystemId.Put(replaceMsg.OriginalTransactionId));

		var transId = pair.Value.Key;
		var info = pair.Value.Value;

		var result = await SendCommand(new MoveOrderMessage
		{
			TransactionId = transId,
			Price = (double)replaceMsg.Price,
			Quantity = (int)replaceMsg.Volume,
			MoveFlag = replaceMsg.Volume != replaceMsg.OldOrderVolume ? MoveOrderFlag.ChangeQuantity : MoveOrderFlag.DontChangeQuantity,
		}, cancellationToken);

		_orders.Add(result.TransactionId, new OrderInfo(replaceMsg.TransactionId, info.Type, info.Repo, info.Rps));
	}

	private ValueTask OnClientLimitsResponse(ClientLimitsResponse response)
	{
		return default;
	}

	private async ValueTask OnPortfolioResponse(PortfolioResponse response)
	{
		string pfName = null;

		if (!response.Client.IsEmpty())
		{
			pfName = response.Client;

			await TrySuspendOutAsync(new PortfolioMessage
			{
				PortfolioName = response.Client,
			});
		}

		if (!response.Union.IsEmpty())
		{
			pfName = response.Union;

			_unions.Add(response.Union);

			await TrySuspendOutAsync(new PortfolioMessage
			{
				PortfolioName = response.Union,
			});
		}

		if (pfName.IsEmpty())
			return;

		var time = CurrentTime;

		//foreach (var money in response.Money)
		//{
		//	SendOutMessage(new PositionChangeMessage
		//	{
		//		PortfolioName = pfName,
		//		SecurityId = SecurityId.Money,
		//		ServerTime = time,
		//	}
		//	.Add(PositionChangeTypes.BeginValue, (decimal)money.OpenBalance)
		//	.Add(PositionChangeTypes.CurrentValue, (decimal)money.Balance)
		//	.TryAdd(PositionChangeTypes.CurrentPrice, money.Price?.ToDecimal(), true)
		//	.TryAdd(PositionChangeTypes.BlockedValue, (decimal)(money.Buying + money.Selling))
		//	.TryAdd(PositionChangeTypes.VariationMargin, money.Cover?.ToDecimal(), true)
		//	.TryAdd(PositionChangeTypes.RealizedPnL, money.PnLIncome?.ToDecimal(), true)
		//	.TryAdd(PositionChangeTypes.UnrealizedPnL, money.UnrealizedPnL?.ToDecimal(), true))
		//	;
		//}

		foreach (var security in response.Securities)
		{
			var secId = CreateId(security.SecCode, security.Market, security.SecId);

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = pfName,
				SecurityId = secId,
				ServerTime = time,
			}
			.Add(PositionChangeTypes.BeginValue, (decimal)security.OpenBalance)
			.Add(PositionChangeTypes.CurrentValue, (decimal)security.Balance)
			.TryAdd(PositionChangeTypes.CurrentPrice, security.Price?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, (decimal)(security.Buying + security.Selling))
			.TryAdd(PositionChangeTypes.VariationMargin, security.Cover?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.RealizedPnL, security.PnLIncome?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, security.UnrealizedPnL?.ToDecimal(), true),
			default);

			foreach (var part in security.ValueParts)
			{
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = pfName,
					SecurityId = secId,
					LimitType = part.Register.ToPositionLimit(this.AddErrorLog),
					ServerTime = time,
				}
				.Add(PositionChangeTypes.BeginValue, (decimal)part.OpenBalance)
				.Add(PositionChangeTypes.CurrentValue, (decimal)part.Balance)
				.TryAdd(PositionChangeTypes.BlockedValue, (decimal)(part.Buying + part.Selling)),
				default);
			}
		}
	}

	private async ValueTask OnClientResponse(ClientResponse response)
	{
		if (response.Remove)
			return;

		if (!response.Id.IsEmpty())
		{
			await TrySuspendOutAsync(new PortfolioMessage
			{
				PortfolioName = response.Id,
			});
		}

		if (!response.Union.IsEmpty())
		{
			_unions.Add(response.Union);

			await TrySuspendOutAsync(new PortfolioMessage
			{
				PortfolioName = response.Union,
				Currency = response.Currency.ToCurrency(this.AddErrorLog),
			});
		}

		if (!response.FortsAcc.IsEmpty())
		{
			await TrySuspendOutAsync(new PortfolioMessage
			{
				PortfolioName = response.FortsAcc,
			});
		}

		//if (response.MlIntraDay != null)
		//{
		//	SendOutMessage(
		//		this
		//			.CreatePortfolioChangeMessage(response.Id)
		//				.Add(PositionChangeTypes.Leverage, response.MlIntraDay.Value));
		//}

		//if (MicexRegisters)
		//    SendCommand(new RequestLeverageControlMessage { Client = response.Id });
	}

	private async ValueTask OnLeverageControlResponse(LeverageControlResponse response)
	{
		if (response.LeverageFact != null)
		{
			await SendOutMessageAsync(
				this
					.CreatePortfolioChangeMessage(response.Client)
						.Add(PositionChangeTypes.Leverage, (decimal)response.LeverageFact.Value),
				default);
		}
	}

	private ValueTask OnMarketOrdResponse(MarketOrdResponse response)
	{
		return default;
	}

	private async ValueTask OnOrdersResponse(OrdersResponse response)
	{
		foreach (var order in response.Orders.Cast<TransaqBaseOrder>().Concat(response.StopOrders))
		{
			var execMsg = new ExecutionMessage
			{
				SecurityId = CreateId(order.SecCode, order.Board, order.SecId),
				Side = order.BuySell.FromTransaq(),
				PortfolioName = order.Client.IsEmpty() ? order.Union : order.Client,
				ExpiryDate = order.ExpDate?.ApplyMoscow().UtcDateTime,
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
			};

			var usualOrder = order as TransaqOrder;

			if (usualOrder != null)
			{
				var price = usualOrder.Price?.ToDecimal() ?? 0;

				execMsg.OrderId = usualOrder.OrderNo;
				execMsg.Balance = usualOrder.Balance;
				execMsg.ServerTime = (usualOrder.WithdrawTime ?? usualOrder.Time ?? usualOrder.AcceptTime ?? DateTime.MinValue).ToDto();
				execMsg.SystemComment = usualOrder.Result;
				execMsg.OrderPrice = price;
				execMsg.OrderVolume = usualOrder.Quantity;
				execMsg.OrderType = price == 0 ? OrderTypes.Market : OrderTypes.Limit;
				execMsg.Commission = usualOrder.MaxCommission?.ToDecimal();

				if (usualOrder.ConditionType != TransaqAlgoOrderConditionTypes.None)
				{
					execMsg.OrderType = OrderTypes.Conditional;

					execMsg.Condition = new TransaqOrderCondition
					{
						AlgoType = usualOrder.ConditionType,
						AlgoValue = usualOrder.ConditionValue.To<decimal>(),

						AlgoValidAfter = usualOrder.ValidAfter?.ToDto(),
						AlgoValidBefore = usualOrder.ValidBefore?.ToDto(),
					};
				}

				execMsg.Comment = usualOrder.BrokerRef;
			}
			else
			{
				var stopOrder = (TransaqStopOrder)order;

				execMsg.OrderId = stopOrder.ActiveOrderNo;
				execMsg.OrderType = OrderTypes.Conditional;
				execMsg.ServerTime = stopOrder.AcceptTime?.ToDto() ?? DateTime.MinValue;

				var stopCond = new TransaqOrderCondition
				{
					Type = stopOrder.StopLoss != null && stopOrder.TakeProfit != null ? TransaqOrderConditionTypes.TakeProfitStopLoss : (stopOrder.StopLoss != null ? TransaqOrderConditionTypes.StopLoss : TransaqOrderConditionTypes.TakeProfit),
					ValidFor = stopOrder.ValidBefore?.ToDto(),
					LinkedOrderId = stopOrder.LinkedOrderNo,
				};

				if (stopOrder.StopLoss != null)
				{
					stopCond.StopLossActivationPrice = stopOrder.StopLoss.ActivationPrice?.ToDecimal();
					stopCond.StopLossOrderPrice = stopOrder.StopLoss.OrderPrice;
					stopCond.StopLossByMarket = stopOrder.StopLoss.OrderPrice == null;
					stopCond.StopLossVolume = stopOrder.StopLoss.Quantity;
					//stopCond.StopLossUseCredit = stopOrder.StopLoss.UseCredit.To<bool>();

					if (stopOrder.StopLoss.GuardTime != null)
						stopCond.StopLossProtectionTime = (int)stopOrder.StopLoss.GuardTime.Value.TimeOfDay.TotalMinutes;

					execMsg.Comment = stopOrder.StopLoss.BrokerRef;
				}

				if (stopOrder.TakeProfit != null)
				{
					stopCond.TakeProfitActivationPrice = stopOrder.TakeProfit.ActivationPrice?.ToDecimal();
					stopCond.TakeProfitByMarket = stopOrder.TakeProfit.GuardSpread == null;
					stopCond.TakeProfitVolume = stopOrder.TakeProfit.Quantity;
					//stopCond.TakeProfitUseCredit = stopOrder.TakeProfit.UseCredit.To<bool>();

					if (stopOrder.TakeProfit.GuardTime != null)
						stopCond.TakeProfitProtectionTime = (int)stopOrder.TakeProfit.GuardTime.Value.TimeOfDay.TotalMinutes;

					stopCond.TakeProfitCorrection = stopOrder.TakeProfit.Correction;
					stopCond.TakeProfitProtectionSpread = stopOrder.TakeProfit.GuardSpread;

					execMsg.Comment = stopOrder.TakeProfit.BrokerRef;
				}
			}

			execMsg.OrderState = order.Status.ToStockSharpState();
			//execMsg.OrderStatus = order2.Status.ToStockSharpStatus();

			if (order.Status != TransaqOrderStatus.cancelled)
			{
				if (execMsg.OrderState == OrderStates.Failed && usualOrder != null)
					execMsg.Error = new InvalidOperationException(usualOrder.Result);
			}

			using (_orders.EnterScope())
			{
				if (_orders.TryGetValue(order.TransactionId, out var info))
				{
					execMsg.OriginalTransactionId = info.TransactionId;
				}
				else
				{
					execMsg.TransactionId = order.TransactionId;

					// если заявка пришла от терминала, то просто идентификатор транзакции ассоциируем как стокшарповский
					_orders.Add(order.TransactionId, new OrderInfo(execMsg.TransactionId, order is TransaqStopOrder ? OrderTypes.Conditional : OrderTypes.Limit, false, false));
				}
			}

			await TrySuspendOutAsync(execMsg);

			//if (order.Condition != null && order.DerivedOrder == null && order2.ConditionType != TransaqAlgoOrderConditionTypes.None && order2.OrderNo != 0)
			//	AddDerivedOrder(security, order2.OrderNo, order, (stopOrder, limitOrder) => stopOrder.DerivedOrder = limitOrder);
		}
	}

	private ValueTask OnOvernightResponse(OvernightResponse response)
	{
		return default;
	}

	private async ValueTask OnPositionsResponse(PositionsResponse response)
	{
		var time = CurrentTime;

		static string GetPortfolioName(Position pos)
		{
			var pfName = pos.Client.IsEmpty() ? pos.Union : pos.Client;

			if (pfName.IsEmpty())
				throw new ArgumentException(nameof(pos));

			return pfName;
		}

		foreach (var pos in response.MoneyPositions)
		{
			await SendOutMessageAsync(
				new PositionChangeMessage
				{
					SecurityId = SecurityId.Money,
					PortfolioName = GetPortfolioName(pos),
					ServerTime = time,
					LimitType = pos.Register.ToPositionLimit(this.AddErrorLog),
				}
				.TryAdd(PositionChangeTypes.Currency, pos.Currency.ToCurrency(this.AddErrorLog))
				.TryAdd(PositionChangeTypes.BeginValue, pos.SaldoIn?.ToDecimal(), true)
				.TryAdd(PositionChangeTypes.CurrentValue, pos.Saldo?.ToDecimal(), true)
				.TryAdd(PositionChangeTypes.BlockedValue, pos.OrdBuy?.ToDecimal() ?? 0 + pos.OrdBuyCond?.ToDecimal() ?? 0)
				.TryAdd(PositionChangeTypes.Commission, pos.Commission?.ToDecimal()),
				default);
		}

		foreach (var pos in response.SecPositions)
		{
			await SendOutMessageAsync(
				new PositionChangeMessage
				{
					PortfolioName = GetPortfolioName(pos),
					SecurityId = CreateId(pos.SecCode, pos.Market, pos.SecId),
					DepoName = pos.Register,
					ServerTime = time,
					LimitType = pos.Register.ToPositionLimit(this.AddErrorLog),
				}
				.TryAdd(PositionChangeTypes.CurrentPrice, pos.Amount?.ToDecimal(), true)
				.TryAdd(PositionChangeTypes.BeginValue, pos.SaldoIn?.ToDecimal(), true)
				.TryAdd(PositionChangeTypes.CurrentValue, pos.Saldo?.ToDecimal(), true)
				.TryAdd(PositionChangeTypes.BlockedValue, pos.SaldoMin?.ToDecimal(), true),
				default);
		}

		foreach (var pos in response.FortsMoneys)
		{
			await SendOutMessageAsync(
				new PositionChangeMessage
				{
					SecurityId = SecurityId.Money,
					PortfolioName = GetPortfolioName(pos),
					ServerTime = time,
				}
				.TryAdd(PositionChangeTypes.BeginValue, pos.Free?.ToDecimal(), true)
				.TryAdd(PositionChangeTypes.CurrentValue, pos.Current?.ToDecimal(), true)
				.TryAdd(PositionChangeTypes.BlockedValue, pos.Blocked?.ToDecimal(), true)
				.TryAdd(PositionChangeTypes.VariationMargin, pos.VarMargin?.ToDecimal(), true),
				default);
		}

		foreach (var pos in response.FortsPositions)
		{
			foreach (var market in pos.Markets)
			{
				await SendOutMessageAsync(
					new PositionChangeMessage
					{
						SecurityId = CreateId(pos.SecCode, market.Id, pos.SecId),
						PortfolioName = GetPortfolioName(pos),
						ServerTime = time,
					}
					.TryAdd(PositionChangeTypes.BeginValue, pos.StartNet?.ToDecimal(), true)
					.TryAdd(PositionChangeTypes.CurrentValue, pos.TotalNet?.ToDecimal(), true)
					.TryAdd(PositionChangeTypes.VariationMargin, pos.VarMargin?.ToDecimal(), true),
					default);
			}
		}

		//foreach (var fortsCollaterals in response.FortsCollateralses)
		//{

		//}

		//foreach (var spotLimit in response.SpotLimits)
		//{

		//}
	}

	private async ValueTask OnTradesResponse(TradesResponse response)
	{
		foreach (var trade in response.Trades)
		{
			await TrySuspendOutAsync(new ExecutionMessage
			{
				SecurityId = CreateId(trade.SecCode, trade.Board, trade.SecId),
				OrderId = trade.OrderNo,
				TradeId = trade.TradeNo,
				TradePrice = trade.Price.ToDecimal(),
				Side = trade.BuySell.FromTransaq(),
				ServerTime = trade.Time.ToDto(),
				Comment = trade.BrokerRef,
				TradeVolume = trade.Quantity,
				PortfolioName = trade.Client,
				DataTypeEx = DataType.Transactions,
				Commission = trade.Commission?.ToDecimal(),
			});
		}
	}

	private async ValueTask OnUnionResponse(UnionResponse response)
	{
		_unions.Add(response.Id);

		await TrySuspendOutAsync(new PortfolioMessage
		{
			PortfolioName = response.Id,
		});
	}

	private async ValueTask TrySuspendOutAsync(PortfolioMessage pfMsg)
	{
		await TrySuspendOutAsync<PortfolioMessage>(pfMsg);

		var pfName = pfMsg.PortfolioName;

		if (!_pfSubs.TryAdd(pfName))
			return;

		if (!_unions.Contains(pfName))
			return;

		//SendCommand(new RequestLeverageControlMessage { Client = pfName });
		_ = SendCommand(new RequestMcPortfolioMessage
		{
			Union = pfName,

			Asset = true,
			Currency = true,
			Depo = true,
			Money = true,
			Registers = true,
		}, default);
	}
}