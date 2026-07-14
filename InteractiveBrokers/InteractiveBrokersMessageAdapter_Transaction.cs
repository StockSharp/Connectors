namespace StockSharp.InteractiveBrokers;

partial class InteractiveBrokersMessageAdapter
{
	private readonly SynchronizedDictionary<string, SecurityId> _secIdByTradeIds = [];
	private readonly SynchronizedDictionary<long, string> _pnlAccounts = [];
	private readonly SynchronizedPairSet<long, long> _nativeOrderIds = [];
	private readonly SynchronizedDictionary<long, string> _orderCancelErrors = [];
	private readonly SynchronizedDictionary<long, string> _orderRegErrors = [];
	private readonly SynchronizedSet<string> _pfSubs = new(StringComparer.InvariantCultureIgnoreCase);
	private long _nextNativeOrderId;

	private long GetTransactionId(long nativeOrderId)
	{
		if (_nativeOrderIds.TryGetKey(nativeOrderId, out var transId))
			return transId;

		return nativeOrderId;
	}

	private long GetNextNativeOrderId(OrderRegisterMessage message)
	{
		var nextNativeOrderId = _nextNativeOrderId;

		_nativeOrderIds.Add(message.TransactionId, nextNativeOrderId);
		_messageRequests.Add(nextNativeOrderId, message.Type);

		_nextNativeOrderId++;
		return nextNativeOrderId;
	}

	private const string _pfGroupAll = "All";

	/// <inheritdoc />
	protected override ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		if (regMsg == null)
			throw new ArgumentNullException(nameof(regMsg));

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				var ibCon = (InteractiveBrokersOrderCondition)regMsg.Condition ?? throw new ArgumentException(LocalizedStrings.ConditionNotSpecified);

				if (!ibCon.IsOptionsExercise)
					break;

				return ExerciseOptions(regMsg, true, regMsg.Volume, regMsg.PortfolioName, ibCon.IsOptionsOverride, ibCon.ManualOrderTime, ibCon.CustomerAccount, ibCon.ProfessionalCustomer, cancellationToken);
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var version = Session.ServerVersion < ServerVersions.NotHeld ? ServerVersions.V27 : ServerVersions.SecIdType;

		var socket = Session;
		var security = (SecurityMessage)regMsg;

		socket
			.SendMessage(RequestMessages.RegisterOrder)
			.SendIfLess(ServerVersions.OrderContainer, s => s.SendVersion(version))
			.Send(GetNextNativeOrderId(regMsg));

		if (socket.ServerVersion >= ServerVersions.PlaceOrderConId)
			socket.SendContractId(security.SecurityId);

		socket
			.Send(GetSymbol(security))
			.SendSecurityType(security.SecurityType, security.UnderlyingSecurityType)
			.SendDate(security.ExpiryDate)
			.SendStrike(security.Strike)
			.SendOptionType(security.OptionType)
			.SendIfEqualOrMore(ServerVersions.V15, s => s.Send(security.Multiplier))
			.Send(GetExchange(security));

		if (socket.ServerVersion >= ServerVersions.V14)
			socket.Send(GetPrimaryExchange(security));

		socket
			.SendCurrency(security.Currency)
			.SendIfEqualOrMore(ServerVersions.V2, s => s.Send(GetLocalSymbol(security)))
			.SendIfEqualOrMore(ServerVersions.TradingClass, s => s.Send(regMsg.Class))
			.SendIfEqualOrMore(ServerVersions.SecIdType, s => s.SendSecurityId(regMsg.SecurityId));

		socket
			.SendOrderSide(regMsg.Side);

		if (socket.ServerVersion >= ServerVersions.FractionalPositions)
			socket.Send(regMsg.Volume);
		else
			socket.Send((int)regMsg.Volume);

		var condition = (InteractiveBrokersOrderCondition)regMsg.Condition ?? new InteractiveBrokersOrderCondition();

		socket
			.SendOrderType(regMsg.OrderType ?? OrderTypes.Limit, condition.ExtendedType);

		if (socket.ServerVersion < ServerVersions.OrderComboLegsPrice)
			socket.Send(regMsg.Price);
		else
			socket.Send(regMsg.Price == 0 ? null : regMsg.Price);

		if (socket.ServerVersion < ServerVersions.TrailingPercent)
			socket.Send(condition.StopPrice);
		else
			socket.Send(condition.StopPrice == 0 ? null : condition.StopPrice);

		socket
			.SendOrderExpiration(regMsg)
			.Send(condition.Oca.Group)
			.Send(regMsg.PortfolioName)
			.Send(condition.IsOpenOrClose ?? true ? "O" : "C")
			.Send((int)(condition.Origin ?? InteractiveBrokersOrderCondition.OrderOrigins.Customer))
			.Send(regMsg.Comment)
			.Send(condition.Transmit ?? true)
			.SendIfEqualOrMore(ServerVersions.V4, s => s.Send(condition.ParentId))
			.SendIfEqualOrMore(ServerVersions.V5, s =>
			{
				s
					.Send(condition.SplitVolume == false)
					.Send(condition.SweepToFill)
					.Send(regMsg.VisibleVolume)
					.Send((int)(condition.TriggerMethod ?? InteractiveBrokersOrderCondition.TriggerMethods.Default));

				if (s.ServerVersion < ServerVersions.V38)
				{
					//will never happen
					s.Send(/* ignoreRth */ false);
				}
				else
				{
					s.Send(condition.OutsideRth);
				}
			})
			.SendIfEqualOrMore(ServerVersions.V7, s => s.Send(condition.Hidden));

		if (socket.ServerVersion >= ServerVersions.V8 && regMsg.IsCombo())
		{
			// Send combo legs for BAG requests
			if (socket.ServerVersion >= ServerVersions.V8)
			{
				var innerIds = regMsg.ToCombo();

				socket.Send(innerIds.Count);

				foreach (var pair in innerIds)
				{
					var innerId = pair.Key;
					var weight = pair.Value;

					socket
						.SendContractId(innerId)
						.Send((int)weight.Abs())
						.SendOrderSide(weight >= 0 ? Sides.Buy : Sides.Sell)
						.Send(innerId.BoardCode);

					var shortSale = condition.Combo.ShortSales[innerId];

					socket
						.Send(shortSale.IsOpenOrClose)
						.SendIfEqualOrMore(ServerVersions.SShortComboLegs, s =>
						{
							s
							.Send((int)shortSale.Slot)
							.Send(shortSale.Location);
						})
						.SendIfEqualOrMore(ServerVersions.SShortXOld, s => s.Send(shortSale.ExemptCode));
				}
			}

			// Send order combo legs for BAG requests
			if (socket.ServerVersion >= ServerVersions.OrderComboLegsPrice)
			{
				var legs = condition.Combo.Legs.ToArray();

				socket.Send(legs.Length);

				foreach (var leg in legs)
					socket.Send(leg);
			}

			if (socket.ServerVersion >= ServerVersions.SmartComboRoutingParams)
			{
				var comboParams = condition.SmartRouting.ComboParams.ToArray();

				socket.Send(comboParams.Length);

				foreach (var comboParam in comboParams)
				{
					socket
						.Send(comboParam.Item1)
						.Send(comboParam.Item2);
				}
			}
		}

		if (socket.ServerVersion >= ServerVersions.V9)
		{
			// deprecated sharesAllocation field
			socket.Send(string.Empty);
		}

		if (socket.ServerVersion >= ServerVersions.V10)
		{
			socket.Send(condition.SmartRouting.DiscretionaryAmount);
		}

		if (socket.ServerVersion >= ServerVersions.V11)
			socket.Send(condition.GoodAfterTime);

		if (socket.ServerVersion >= ServerVersions.V12)
			socket.Send(regMsg.TillDate);

		if (socket.ServerVersion >= ServerVersions.V13)
		{
			var financialAdvisor = condition.FinancialAdvisor;

			socket
				.Send(financialAdvisor.Group)
				.SendFinancialAdvisor(financialAdvisor.Allocation)
				.Send(financialAdvisor.Percentage)
				.SendIfLess(ServerVersions.MinServerVerFaProfileDesupport,
					s => s.Send(string.Empty) /* send deprecated faProfile field */);
		}

		if (socket.ServerVersion >= ServerVersions.ModelsSupport)
		{
			socket.Send(regMsg.ClientCode /* model code */);
		}

		if (socket.ServerVersion >= ServerVersions.V18)
		{
			// institutional short sale slot fields.
			socket
				// 0 only for retail, 1 or 2 only for institution.
				.Send((int)condition.ShortSale.Slot)
				// only populate when shortSaleSlot = 2.
				.Send(condition.ShortSale.Location)
				.SendIfEqualOrMore(ServerVersions.SShortXOld, s => s.Send(condition.ShortSale.ExemptCode));
		}

		if (socket.ServerVersion >= ServerVersions.V19)
		{
			socket.Send((int)(condition.Oca.Type ?? 0));

			if (socket.ServerVersion < ServerVersions.V38)
			{
				// will never happen
				socket.Send(/* rthOnly */false);
			}

			socket
				.SendAgent(condition.Agent)
				.Send(condition.Clearing.SettlingFirm)
				.Send(condition.AllOrNone)
				.Send((int?)regMsg.MinOrderVolume)
				.Send(condition.PercentOffset)
				.Send(false)
				.Send(false)
				.Send((decimal?)null)
				.Send((int?)condition.AuctionStrategy)
				.Send(condition.StartingPrice)
				.Send(condition.StockRefPrice)
				.Send(condition.Delta);

			decimal? lower = null;
			decimal? upper = null;

			if (socket.ServerVersion == ServerVersions.V26)
			{
				// Volatility orders had specific watermark price attribs in server version 26
				if (condition.ExtendedType != InteractiveBrokersOrderCondition.ExtendedOrderTypes.Volatility)
				{
					lower = condition.StockRangeLower;
					upper = condition.StockRangeUpper;
				}
			}

			socket
				.Send(lower)
				.Send(upper);
		}

		if (socket.ServerVersion >= ServerVersions.V22)
		{
			socket.Send(condition.OverridePercentageConstraints);
		}

		if (socket.ServerVersion >= ServerVersions.V26)
		{
			var volatility = condition.Volatility;

			socket
				.Send(volatility.Volatility)
				.Send((int?)volatility.VolatilityTimeFrame);

			if (socket.ServerVersion < ServerVersions.V28)
			{
				socket.Send(volatility.OrderType == OrderTypes.Market);
			}
			else
			{
				socket
					.SendOrderType(volatility.OrderType ?? OrderTypes.Conditional, volatility.ExtendedOrderType)
					.Send(volatility.StopPrice);

				if (volatility.ExtendedOrderType != null)
				{
					if (socket.ServerVersion >= ServerVersions.DeltaNeutralConId)
					{
						socket
							.Send(volatility.ContractId)
							.Send(volatility.SettlingFirm)
							.Send(volatility.ClearingPortfolio)
							.Send(volatility.ClearingIntent);
					}

					if (socket.ServerVersion >= ServerVersions.DeltaNeutralOpenClose)
					{
						socket
							.Send(volatility.ShortSale.IsOpenOrClose)
							.Send(volatility.IsShortSale)
							.Send((int)volatility.ShortSale.Slot)
							.Send(volatility.ShortSale.Location);
					}
				}
			}

			socket.Send(volatility.ContinuousUpdate == true ? 1 : 0);

			if (socket.ServerVersion == ServerVersions.V26)
			{
				var isVol = condition.ExtendedType == InteractiveBrokersOrderCondition.ExtendedOrderTypes.Volatility;

				socket
					.Send(isVol ? condition.StockRangeLower : null)
					.Send(isVol ? condition.StockRangeUpper : null);
			}

			if (volatility.IsAverageBestPrice == null)
				socket.Send((int?)null);
			else
			{
				// 1 - Average of NBBO
				// 2 - NBB or the NBO depending on the action and right.
				socket.Send(volatility.IsAverageBestPrice == true ? 1 : 2);
			}
		}

		if (socket.ServerVersion >= ServerVersions.V30)
		{
			// TRAIL_STOP_LIMIT stop price
			socket.Send(condition.TrailStopPrice);
		}

		if (socket.ServerVersion >= ServerVersions.TrailingPercent)
		{
			socket.Send(condition.TrailStopVolumePercentage);
		}

		//Scale Orders require server version 35 or higher.
		if (socket.ServerVersion >= ServerVersions.SShortComboLegs)
		{
			if (socket.ServerVersion >= ServerVersions.ScaleOrders2)
			{
				socket
					.Send(condition.Scale.InitLevelSize)
					.Send(condition.Scale.SubsLevelSize);
			}
			else
			{
				socket
					.Send(string.Empty)
					.Send(condition.Scale.InitLevelSize);
			}

			socket.Send(condition.Scale.PriceIncrement);
		}

		if (socket.ServerVersion >= ServerVersions.ScaleOrders3 && condition.Scale.PriceIncrement > 0)
		{
			socket
				.Send(condition.Scale.PriceAdjustValue)
				.Send(condition.Scale.PriceAdjustInterval)
				.Send(condition.Scale.ProfitOffset)
				.Send(condition.Scale.AutoReset)
				.Send(condition.Scale.InitPosition)
				.Send(condition.Scale.InitFillQty)
				.Send(condition.Scale.RandomPercent);
		}

		if (socket.ServerVersion >= ServerVersions.ScaleTable)
		{
			socket
				.Send(condition.Scale.Table)
				.Send(condition.Active.Start)
				.Send(condition.Active.Stop);
		}

		if (socket.ServerVersion >= ServerVersions.HedgeOrders)
		{
			if (condition.Hedge.Type == null)
				socket.Send(string.Empty);
			else
			{
				switch (condition.Hedge.Type.Value)
				{
					case InteractiveBrokersOrderCondition.HedgeTypes.Delta:
						socket.Send("D");
						break;
					case InteractiveBrokersOrderCondition.HedgeTypes.Beta:
						socket.Send("B");
						break;
					case InteractiveBrokersOrderCondition.HedgeTypes.FX:
						socket.Send("F");
						break;
					case InteractiveBrokersOrderCondition.HedgeTypes.Pair:
						socket.Send("P");
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}

				socket.Send(condition.Hedge.Param);
			}
		}

		if (socket.ServerVersion >= ServerVersions.OptOutSmartRoute)
		{
			socket.Send(condition.SmartRouting.OptOutSmartRouting);
		}

		if (socket.ServerVersion >= ServerVersions.PtaOrders)
		{
			socket
				.Send(condition.Clearing.Portfolio)
				.SendIntent(condition.Clearing.Intent);
		}

		if (socket.ServerVersion >= ServerVersions.NotHeld)
			socket.Send(condition.SmartRouting.NotHeld);

		if (socket.ServerVersion >= ServerVersions.ScaleOrders2)
		{
			//if (contract.UnderlyingComponent != null)
			//{
			//	DeltaNeutralContract deltaNeutralContract = contract.DeltaNeutralContract;
			//	send(true);
			//	send(deltaNeutralContract.ContractId);
			//	send(deltaNeutralContract.Delta);
			//	send(deltaNeutralContract.Price);
			//}
			//else
			{
				socket.Send(false);
			}
		}

		if (socket.ServerVersion >= ServerVersions.AlgoOrders)
		{
			var algoStrategy = condition.Algo.Strategy;
			socket.Send(algoStrategy);

			var algoParams = condition.Algo.Params.ToArray();

			if (!algoStrategy.IsEmpty())
			{
				socket.Send(algoParams.Length);

				foreach (var param in algoParams)
				{
					socket
						.Send(param.Item1)
						.Send(param.Item2);
				}
			}
		}

		if (socket.ServerVersion >= ServerVersions.AlgoId)
		{
			socket.Send(condition.AlgoId);
		}

		if (socket.ServerVersion >= ServerVersions.WhatIfOrders)
		{
			socket.Send(condition.WhatIf);
		}

		// send orderMiscOptions parameter
		if (socket.ServerVersion >= ServerVersions.Linking)
		{
			socket.SendTagsNoCount(condition.MiscOptions);
		}

		if (socket.ServerVersion >= ServerVersions.OrderSolicited)
		{
			socket.Send(condition.Solicited);
		}

		if (socket.ServerVersion >= ServerVersions.RandomSizeAndPrice)
		{
			socket.Send(condition.RandomizeSize);
			socket.Send(condition.RandomizePrice);
		}

		if (socket.ServerVersion >= ServerVersions.PeggedToBenchmark)
		{
			if (condition.ExtendedType == InteractiveBrokersOrderCondition.ExtendedOrderTypes.PeggedBench)
			{
				socket.Send(condition.ReferenceContractId ?? 0);
				socket.Send(condition.IsPeggedChangeAmountDecrease ?? false);
				socket.Send(condition.PeggedChangeAmount ?? 0);
				socket.Send(condition.ReferenceChangeAmount ?? 0);
				socket.Send(condition.ReferenceExchange);
			}

			var conditions = condition.ExtraConditions.ToArray();
			socket.Send(conditions.Length);

			if (conditions.Length > 0)
			{
				foreach (var item in condition.ExtraConditions)
				{
					socket.Send((int)item.Type);
					socket.Send(item.IsConjunctionConnection ? "a" : "o");
				}

				socket.Send(condition.ConditionsIgnoreRth);
				socket.Send(condition.ConditionsCancelOrder);
			}

			socket.Send(condition.AdjustedOrderType);
			socket.Send(condition.TriggerPrice ?? 0);
			socket.Send(condition.LimitPriceOffset ?? 0);
			socket.Send(condition.AdjustedStopPrice ?? 0);
			socket.Send(condition.AdjustedStopLimitPrice ?? 0);
			socket.Send(condition.AdjustedTrailingAmount ?? 0);
			socket.Send(condition.AdjustableTrailingUnit ?? 0);
		}

		if (socket.ServerVersion >= ServerVersions.ExtOperator)
		{
			socket.Send(condition.ExtOperator);
		}

		if (socket.ServerVersion >= ServerVersions.SoftDollarTier)
		{
			socket.Send(condition.Tier.Name);
			socket.Send(condition.Tier.Value);
		}

		if (socket.ServerVersion >= ServerVersions.CashQty)
		{
			socket.Send(condition.CashQty);
		}

		if (socket.ServerVersion >= ServerVersions.DecisionMaker)
		{
			socket.Send(condition.Mifid2DecisionMaker);
			socket.Send(condition.Mifid2DecisionAlgo);
		}

		if (socket.ServerVersion >= ServerVersions.MifidExecution)
		{
			socket.Send(condition.Mifid2ExecutionTrader);
			socket.Send(condition.Mifid2ExecutionAlgo);
		}

		if (socket.ServerVersion >= ServerVersions.AutoPriceForHedge)
		{
			socket.Send(condition.DontUseAutoPriceForHedge);
		}

		if (socket.ServerVersion >= ServerVersions.OrderContainer)
		{
			socket.Send(condition.IsOmsContainer);
		}

		if (socket.ServerVersion >= ServerVersions.DPegOrders)
		{
			socket.Send(condition.DiscretionaryUpToLimitPrice);
		}

		if (socket.ServerVersion >= ServerVersions.PriceMgmtAlgo)
		{
			socket.Send(condition.UsePriceManagementAlgo);
		}

		if (socket.ServerVersion >= ServerVersions.Duration)
		{
			socket.Send(condition.Duration);
		}

		if (socket.ServerVersion >= ServerVersions.PostToAts)
		{
			socket.Send(condition.PostToAts);
		}

		if (socket.ServerVersion >= ServerVersions.AutoCancelParent)
		{
			socket.Send(condition.AutoCancelParent);
		}

		if (socket.ServerVersion >= ServerVersions.AdvancedOrderReject)
		{
			socket.Send(condition.AdvancedErrorOverride);
		}

		if (socket.ServerVersion >= ServerVersions.ManualOrderTime)
		{
			socket.Send(condition.ManualOrderTime);
		}

		if (socket.ServerVersion >= ServerVersions.PegBestPegMinOffsets)
		{
			if (security.SecurityId.BoardCode.EqualsIgnoreCase("IBKRATS"))
				socket.Send(condition.MinTradeQty);

			var sendMidOffsets = false;
			if (condition.ExtendedType == InteractiveBrokersOrderCondition.ExtendedOrderTypes.PeggedBench)
			{
				socket.Send(condition.MinCompeteSize);
				socket.Send(condition.CompeteAgainstBestOffset);

				if (condition.CompeteAgainstBestOffset is null)
					sendMidOffsets = true;
			}
			else if (condition.ExtendedType == InteractiveBrokersOrderCondition.ExtendedOrderTypes.PeggedMid)
			{
				sendMidOffsets = true;
			}

			if (sendMidOffsets)
			{
				socket.Send(condition.MidOffsetAtWhole);
				socket.Send(condition.MidOffsetAtHalf);
			}

			if (socket.ServerVersion >= ServerVersions.MinServerVerCustomerAccount)
				socket.Send(condition.CustomerAccount);

			if (socket.ServerVersion >= ServerVersions.MinServerVerProfessionalCustomer)
				socket.Send(condition.ProfessionalCustomer);

			if (socket.ServerVersion >= ServerVersions.MinServerVerRfqFields)
			{
				socket.Send(condition.ExternalUserId);
				socket.Send(condition.ManualOrderIndicator);
			}
		}

		return socket.SendAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
		=> RegisterOrderAsync(replaceMsg, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var nativeOrderId = _nativeOrderIds.TryGetValue2(cancelMsg.OriginalTransactionId) ?? cancelMsg.OriginalTransactionId;

		_messageRequests.TryAdd2(nativeOrderId, cancelMsg.Type);

		var condition = (InteractiveBrokersOrderCondition)cancelMsg.Condition;

		return Session
			.SendMessage(RequestMessages.CancelOrder)
			.SendVersion(ServerVersions.V1)
			.Send(nativeOrderId)
			.SendIfEqualOrMore(ServerVersions.ManualOrderTime, s => s.Send(string.Empty) /* Specify the time the order should be cancelled. An empty string will cancel the order immediately. */)
			.SendIfEqualOrMore(ServerVersions.MinServerVerRfqFields, s =>
			{
				s.Send(condition.ExtOperator);
				s.Send(condition.ExternalUserId);
				s.Send(condition.ManualOrderIndicator);
			})
			.SendAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (statusMsg == null)
			throw new ArgumentNullException(nameof(statusMsg));

		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		if (!statusMsg.IsSubscribe)
			return;

		_messageRequests.Add(statusMsg.TransactionId, statusMsg.Type);

		//await RequestOpenOrders(cancellationToken);

		await RequestAllOpenOrders(cancellationToken);
		//await RequestCompletedOrders(true, cancellationToken);

		//if (ClientId == 0)
		//	await RequestAutoOpenOrders(true, cancellationToken);

		await RequestMyTrades(statusMsg.TransactionId, new MyTradeFilter(), cancellationToken);

		// this method sends orders
		await SubscribePosition(cancellationToken);
	}

	private ValueTask SubscribePortfolio(string pfName, CancellationToken cancellationToken)
	{
		if (_pfSubs.TryAdd(pfName))
			return SubscribePortfolio(pfName, true, cancellationToken);

		return default;
	}

	private async ValueTask ProcessPortfolioLookup(PortfolioLookupMessage pfMsg, CancellationToken cancellationToken)
	{
		if (pfMsg == null)
			throw new ArgumentNullException(nameof(pfMsg));

		if (!pfMsg.IsSubscribe)
		{
			if (_pfRequests.Count > 0)
			{
				await UnSubscribePosition(cancellationToken);
				await UnSubscribeAccountSummary(_pfRequests.GetAndRemove(_pfGroupAll), cancellationToken);
			}

			foreach (var pfName in _pfSubs.CopyAndClear())
				await SubscribePortfolio(pfName, false, cancellationToken);

			return;
		}

		// отправляется автоматически
		//await RequestPortfolios(cancellationToken);

		_pfRequests[_pfGroupAll] = pfMsg.TransactionId;
		_messageRequests.Add(pfMsg.TransactionId, pfMsg.Type);
		await SubscribeAccountSummary(pfMsg.TransactionId, _pfGroupAll, Enumerator.GetValues<AccountSummaryTag>(), cancellationToken);
	}

	/// <summary>
	/// Exercises an options contract.
	/// This function is affected by a TWS setting which specifies if an exercise request must be finalized.
	/// </summary>
	/// <param name="message">this structure contains a description of the contract to be exercised.  If no multiplier is specified, a default of 100 is assumed.</param>
	/// <param name="isExercise">this can have two values: 1 = specifies exercise 2 = specifies lapse.</param>
	/// <param name="volume">the number of contracts to be exercised.</param>
	/// <param name="portfolioName">specifies whether your setting will override the system's natural action. For example, if your action is "exercise" and the option is not in-the-money, by natural action the option would not exercise. If you have override set to "yes" the natural action would be overridden and the out-of-the money option would be exercised. Values are: 0 = no 1 = yes.</param>
	/// <param name="isOverride">specifies whether your setting will override the system's natural action. For example, if your action is "exercise" and the option is not in-the-money, by natural action the option would not exercise. If you have override set to "yes" the natural action would be overridden and the out-of-the money option would be exercised. Values are: 0 = no 1 = yes.</param>
	/// <param name="manualOrderTime">Manual Order Time.</param>
	/// <param name="customerAccount">Customer Account.</param>
	/// <param name="professionalCustomer">Professional Customer.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask ExerciseOptions(OrderRegisterMessage message, bool isExercise, decimal volume, string portfolioName, bool isOverride, DateTime? manualOrderTime, string customerAccount, bool? professionalCustomer, CancellationToken cancellationToken)
	{
		var socket = Session;

		socket
			.SendMessage(RequestMessages.ExerciseOptions)
			.SendVersion(ServerVersions.V2)
			.Send(GetNextNativeOrderId(message))
			.SendIfEqualOrMore(ServerVersions.TradingClass, s => s.SendContractId(message.SecurityId));

		var security = (SecurityMessage)message;

		return socket
			.Send(GetSymbol(security))
			.SendSecurityType(security.SecurityType, security.UnderlyingSecurityType)
			.SendDate(security.ExpiryDate)
			.SendStrike(security.Strike)
			.SendOptionType(security.OptionType)
			.Send(security.Multiplier)
			.Send(GetExchange(security))
			.SendCurrency(security.Currency)
			.Send(GetPrimaryExchange(security))
			.SendIfEqualOrMore(ServerVersions.TradingClass, s => s.Send(message.Class))
			.Send(isExercise ? 1 : 2)
			.Send((int)volume)
			.Send(portfolioName)
			.Send(isOverride)
			.SendIfEqualOrMore(ServerVersions.ManualOrderTime, s => s.Send(manualOrderTime))
			.SendIfEqualOrMore(ServerVersions.MinServerVerCustomerAccount, s => s.Send(customerAccount))
			.SendIfEqualOrMore(ServerVersions.MinServerVerProfessionalCustomer, s => s.Send(professionalCustomer))
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Call this function to start getting account values, portfolio, and last update time information.
	/// </summary>
	/// <param name="isSubscribe">If set to <see langword="true" />, the client will start receiving account and portfolio updates. If set to <see langword="false" />, the client will stop receiving this information.</param>
	/// <param name="portfolioName">the account code for which to receive account and portfolio updates.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask SubscribePortfolio(string portfolioName, bool isSubscribe, CancellationToken cancellationToken)
	{
		if (portfolioName.IsEmpty())
			throw new ArgumentNullException(nameof(portfolioName));

		var socket = Session;

		socket
			.SendMessage(RequestMessages.RequestAccountData)
			.SendVersion(ServerVersions.V2)
			.Send(isSubscribe);

		if (socket.ServerVersion >= ServerVersions.V9)
			socket.Send(portfolioName);

		return socket.SendAsync(cancellationToken);
	}

	/// <summary>
	/// When this method is called, the execution reports that meet the filter criteria are downloaded to the client via the execDetails() method.
	/// </summary>
	/// <param name="requestId"></param>
	/// <param name="filter">the filter criteria used to determine which execution reports are returned.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask RequestMyTrades(long requestId, MyTradeFilter filter, CancellationToken cancellationToken)
	{
		if (filter == null)
			throw new ArgumentNullException(nameof(filter));

		var socket = Session;

		socket
			.SendMessage(RequestMessages.RequestTrades)
			.SendVersion(ServerVersions.V3);

		if (socket.ServerVersion >= ServerVersions.ExecDataChain)
			socket.Send(requestId);

		if (socket.ServerVersion >= ServerVersions.V9)
		{
			socket
				.Send(filter.ClientId)
				.Send(filter.Portfolio)
				.Send(filter.Time)
				.Send(filter.Symbol)
				.SendSecurityType(filter.SecurityType, filter.UnderlyingSecurityType)
				.Send(filter.BoardCode)
				.SendTradeSide(filter.Side);
		}

		return socket.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Call this method to request the open orders that were placed from this client. Each open order will be fed back through the openOrder() and orderStatus() functions on the EWrapper. The client with a clientId of "0" will also receive the TWS-owned open orders. These orders will be associated with the client and a new orderId will be generated. This association will persist over multiple API and TWS sessions.
	/// </summary>
	private ValueTask RequestOpenOrders(CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestOpenOrders)
			.SendVersion(ServerVersions.V1)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Call this method to request that newly created TWS orders be implicitly associated with the client. When a new TWS order is created, the order will be associated with the client and fed back through the openOrder() and orderStatus() methods on the EWrapper. TWS orders can only be bound to clients with a clientId of “0”.
	/// </summary>
	/// <param name="autoBind">If set to <see langword="true" />, newly created TWS orders will be implicitly associated with the client. If set to <see langword="false" />, no association will be made.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask RequestAutoOpenOrders(bool autoBind, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestAutoOpenOrders)
			.SendVersion(ServerVersions.V1)
			.Send(autoBind)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Call this method to request the open orders that were placed from all clients and also from TWS. Each open order will be fed back through the openOrder() and orderStatus() functions on the EWrapper. No association is made between the returned orders and the requesting client.
	/// </summary>
	private ValueTask RequestAllOpenOrders(CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestAllOpenOrders)
			.SendVersion(ServerVersions.V1)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Call this method to request the list of managed accounts. The list will be returned by the managedAccounts() function on the EWrapper. This request can only be made when connected to a Financial Advisor (FA) account.
	/// </summary>
	private ValueTask RequestPortfolios(CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestPortfolios)
			.SendVersion(ServerVersions.V1)
			.SendAsync(cancellationToken);
	}

	private ValueTask SubscribePosition(CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.SubscribePosition)
			.SendVersion(ServerVersions.V1)
			.SendAsync(cancellationToken);
	}

	private ValueTask UnSubscribePosition(CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.UnSubscribePosition)
			.SendVersion(ServerVersions.V1)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests positions for account and/or model.
	/// </summary>
	/// <param name="requestId">Request's identifier.</param>
	/// <param name="account">If an account Id is provided, only the account's positions belonging to the specified model will be delivered.</param>
	/// <param name="modelCode">The code of the model's positions we are interested in.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask SubscribePositionMulti(long requestId, string account, string modelCode, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestPositionsMulti)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.Send(account)
			.Send(modelCode)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Cancels positions request for account and/or model.
	/// </summary>
	/// <param name="requestId">The identifier of the request to be canceled.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask UnSubscribePositionMulti(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.CancelPositionsMulti)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests account updates for account and/or model.
	/// </summary>
	/// <param name="requestId">Identifier to label the request.</param>
	/// <param name="account">Account values can be requested for a particular account.</param>
	/// <param name="modelCode">Values can also be requested for a model.</param>
	/// <param name="ledgerAndNlv">Returns light-weight request; only currency positions as opposed to account values and currency positions.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask SubscribePotfrolioMulti(long requestId, string account, string modelCode, bool ledgerAndNlv, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestAccountUpdatesMulti)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.Send(account)
			.Send(modelCode)
			.Send(ledgerAndNlv)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Cancels account updates request for account and/or model.
	/// </summary>
	/// <param name="requestId"></param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask UnSubscribePotfrolioMulti(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.CancelAccountUpdatesMulti)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	private ValueTask SubscribeAccountSummary(long requestId, string group, IEnumerable<AccountSummaryTag> tags, CancellationToken cancellationToken)
	{
		if (tags == null)
			throw new ArgumentNullException(nameof(tags));

		return Session
			.SendMessage(RequestMessages.SubscribeAccountSummary)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.Send(group)

			// $LEDGER — Single flag to relay all cash balance tags*, only in base currency.
			// $LEDGER:CURRENCY — Single flag to relay all cash balance tags*, only in the specified currency.
			// $LEDGER:ALL — Single flag to relay all cash balance tags* in all currencies.
			.SendAccountTags(tags)
			.SendAsync(cancellationToken);
	}

	private ValueTask UnSubscribeAccountSummary(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.UnSubscribeAccountSummary)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestGlobalCancel)
			.SendVersion(ServerVersions.V1)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Creates subscription for real time daily PnL and unrealized PnL updates.
	/// </summary>
	/// <param name="requestId"></param>
	/// <param name="account">Account for which to receive PnL updates.</param>
	/// <param name="modelCode">Specify to request PnL updates for a specific model.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask SubscribePnL(long requestId, string account, string modelCode, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.ReqPnL)
			.Send(requestId)
			.Send(account)
			.Send(modelCode)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Cancels subscription for real time updated daily PnL.
	/// </summary>
	/// <param name="requestId"></param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask UnSubscribePnL(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.CancelPnL)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests real time updates for daily PnL of individual positions.
	/// </summary>
	/// <param name="requestId"></param>
	/// <param name="account">Account for which to receive PnL updates.</param>
	/// <param name="modelCode">Specify to request PnL updates for a specific model.</param>
	/// <param name="contractId">Contract ID (conId) of contract to receive daily PnL updates for.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask SubscribePnLSingle(long requestId, string account, string modelCode, long contractId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.ReqPnLSingle)
			.Send(requestId)
			.Send(account)
			.Send(modelCode)
			.Send(contractId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Cancels real time subscription for a positions daily PnL information.
	/// </summary>
	/// <param name="requestId"></param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask UnSubscribePnLSingle(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.CancelPnLSingle)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests completed orders.
	/// </summary>
	/// <param name="apiOnly">Request only API orders.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask RequestCompletedOrders(bool apiOnly, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.ReqCompletedOrders)
			.Send(apiOnly)
			.SendAsync(cancellationToken);
	}

	private ValueTask RequestUserInfo(UserRequestMessage message, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestUserInfo)
			.Send(message.TransactionId)
			.SendAsync(cancellationToken);
	}

	private async ValueTask ReadAccountDownloadEnd(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		var account = await socket.ReadStringAsync(cancellationToken);

		if (!account.IsEmpty())
		{
			await SendOutMessageAsync(new PortfolioMessage { PortfolioName = account }, cancellationToken);

			await SubscribePortfolio(account, cancellationToken);
		}

		//return str.IsEmpty() ? null : str;
	}

	private async ValueTask ReadAccountSummaryEnd(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		var requestId = await socket.ReadIntAsync(cancellationToken);
		await SendSubscriptionOnlineAsync(requestId, cancellationToken);
	}

	private static long FixId(long transactionId)
	{
		//Handle the 2^31-1 == 0 bug
		if (transactionId == int.MaxValue)
			return 0;

		// TODO
		if (transactionId < 0)
			return int.MaxValue + transactionId;

		return transactionId;
	}

	private async ValueTask ReadOrderStatus(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/orderstatus.htm

		var version = socket.ServerVersion >= ServerVersions.MarketCapPrice ? (ServerVersions)int.MaxValue : await socket.ReadVersionAsync(cancellationToken);
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var transactionId = GetTransactionId(FixId(requestId));

		var status = await socket.ReadOrderStatusAsync(cancellationToken);
		/* filled */await socket.ReadDecimalAsync(cancellationToken);
		var balance = await socket.ReadDecimalAsync(cancellationToken);
		var avgPrice = await socket.ReadDecimalAsync(cancellationToken);
		var permId = version >= ServerVersions.V2 ? await socket.ReadIntAsync(cancellationToken) : (int?)null;
		var parentId = version >= ServerVersions.V3 ? await socket.ReadIntAsync(cancellationToken) : (int?)null;
		var lastTradePrice = version >= ServerVersions.V4 ? await socket.ReadDecimalAsync(cancellationToken) : (decimal?)null;
		var clientId = version >= ServerVersions.V5 ? await socket.ReadIntAsync(cancellationToken) : (int?)null;
		var whyHeld = version >= ServerVersions.V6 ? await socket.ReadStringAsync(cancellationToken) : null;
		var mktCapPrice = socket.ServerVersion >= ServerVersions.MarketCapPrice ? await socket.ReadDecimalAsync(cancellationToken) : (decimal?)null;

		var statusId = CurrOrderStatus;

		var execMsg = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = statusId ?? transactionId,
			TransactionId = statusId == null ? transactionId : 0,
			Balance = balance,
			HasOrderInfo = true,
			AveragePrice = avgPrice,
		};

		execMsg.FillStatus(status);

		if (execMsg.OrderState == OrderStates.Pending)
			execMsg.OrderState = OrderStates.Active;

		//if (permId != null)
		//	execMsg.SetPermId(permId.Value);

		if (parentId != null)
			execMsg.Condition = new InteractiveBrokersOrderCondition { ParentId = parentId.Value };

		//if (lastTradePrice != null)
		//	execMsg.SetLastTradePrice(lastTradePrice.Value);

		//if (clientId != null)
		//	execMsg.SetClientId(clientId.Value);

		//if (whyHeld != null)
		//	execMsg.SetWhyHeld(whyHeld);

		if (execMsg.OrderState == OrderStates.Failed)
			execMsg.Error = new InvalidOperationException(_orderRegErrors.TryGetValue(execMsg.OriginalTransactionId));

		await SendOutMessageAsync(execMsg, cancellationToken);
	}

	private async ValueTask ReadAccountValue(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/updateaccountvalue.htm

		var version = await socket.ReadVersionAsync(cancellationToken);
		var name = await socket.ReadStringAsync(cancellationToken);
		var value = await socket.ReadStringAsync(cancellationToken);
		var currency = await socket.ReadStringAsync(cancellationToken);
		var port = version >= ServerVersions.V2 ? await socket.ReadStringAsync(cancellationToken) : null;

		if (port == null || currency == "BASE")
			return;

		var pfMsg = this.CreatePortfolioChangeMessage(port);

		switch (name)
		{
			case "CashBalance":
				pfMsg.Add(PositionChangeTypes.CurrentValue, value.To<decimal>());
				break;
			case "Currency":
			case "RealCurrency":
				if (!currency.IsEmpty())
					pfMsg.TryAdd(PositionChangeTypes.Currency, ToCurrency(currency));
				break;
			case "RealizedPnL":
				pfMsg.Add(PositionChangeTypes.RealizedPnL, value.To<decimal>());
				break;
			case "UnrealizedPnL":
				pfMsg.Add(PositionChangeTypes.UnrealizedPnL, value.To<decimal>());
				break;
			case "NetLiquidation":
				pfMsg.Add(PositionChangeTypes.CurrentPrice, value.To<decimal>());
				break;
			case "Leverage-S":
				pfMsg.Add(PositionChangeTypes.Leverage, value.To<decimal>());
				break;
			case "AccountOrGroup":
				break;
		}

		await SendOutMessageAsync(pfMsg, cancellationToken);
	}

	private async ValueTask ReadPortfolioValue(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/updateportfolio.htm

		var version = await socket.ReadVersionAsync(cancellationToken);
		var contractId = version >= ServerVersions.V6 ? await socket.ReadIntAsync(cancellationToken) : (int?)null;

		var symbol = await socket.ReadStringAsync(cancellationToken);
		var type = await socket.ReadStringAsync(cancellationToken);
		var expiryDate = await socket.ReadStringAsync(cancellationToken);
		var strike = await socket.ReadStrikeAsync(cancellationToken);
		var optionType = await socket.ReadOptionTypeAsync(cancellationToken);
		var multiplier = version >= ServerVersions.V7 ? await socket.ReadNullDecimalAsync(cancellationToken) : null;
		var primaryExch = version >= ServerVersions.V7 ? await socket.ReadStringAsync(cancellationToken) : null;
		var currency = await socket.ReadStringAsync(cancellationToken);
		var localSymbol = version >= ServerVersions.V2 ? await socket.ReadStringAsync(cancellationToken) : symbol;
		var secClass = version >= ServerVersions.V8 ? await socket.ReadStringAsync(cancellationToken) : null;

		var position = await socket.ReadDecimalAsync(cancellationToken);
		var marketPrice = await socket.ReadDecimalAsync(cancellationToken);
		var marketValue = await socket.ReadDecimalAsync(cancellationToken);

		var averagePrice = 0m;
		var unrealizedPnL = 0m;
		var realizedPnL = 0m;
		if (version >= ServerVersions.V3)
		{
			averagePrice = await socket.ReadDecimalAsync(cancellationToken);
			unrealizedPnL = await socket.ReadDecimalAsync(cancellationToken);
			realizedPnL = await socket.ReadDecimalAsync(cancellationToken);
		}

		var portfolio = version >= ServerVersions.V4 ? await socket.ReadStringAsync(cancellationToken) : null;

		string exchange = null;

		if (version == ServerVersions.V6 && socket.ServerVersion == ServerVersions.PtaOrders)
			exchange = await socket.ReadStringAsync(cancellationToken);

		var secId = new SecurityId
		{
			SecurityCode = GetSecurityCode(symbol, type, currency, localSymbol, expiryDate),
			BoardCode = GetBoardCode(exchange),
			InteractiveBrokers = contractId,
		};

		await SendOutMessageAsync(new SecurityMessage
		{
			SecurityId = secId,
			SecurityType = type.ToSecurityType(this, out var underlyingSecurityType),
			UnderlyingSecurityType = underlyingSecurityType,
			ExpiryDate = expiryDate.ReadNullDate(this),
			Strike = strike,
			OptionType = optionType,
			Currency = ToCurrency(currency),
			Multiplier = multiplier ?? 0,
			Class = secClass,
			PrimaryId = new SecurityId
			{
				SecurityCode = localSymbol,
				BoardCode = GetBoardCode(primaryExch),
			}
		}, cancellationToken);

		if (portfolio.IsEmpty())
			return;

		await SendOutMessageAsync(
			this
				.CreatePositionChangeMessage(portfolio, secId)
					.Add(PositionChangeTypes.CurrentValue, position)
					.Add(PositionChangeTypes.CurrentPrice, marketPrice)
					.Add(PositionChangeTypes.AveragePrice, averagePrice)
					.Add(PositionChangeTypes.UnrealizedPnL, unrealizedPnL)
					.Add(PositionChangeTypes.RealizedPnL, realizedPnL), cancellationToken);

		// TODO
		//pos.SetMarketValue(marketValue);
	}

	private async ValueTask ReadAccountUpdateTime(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		/*var timeStamp = */await socket.ReadStringAsync(cancellationToken);
		//updateAccountTime(timeStamp);
	}

	private async ValueTask ReadOpenOrder(IBSocket socket, CancellationToken cancellationToken)
	{
		var version = socket.ServerVersion < ServerVersions.OrderContainer ? await socket.ReadVersionAsync(cancellationToken) : socket.ServerVersion;

		var decoder = new OrderDecoder(socket, version, GetSecurityCode, GetBoardCode, ex => this.AddErrorLog(ex));

		// read order id
		await decoder.ReadOrderId(cancellationToken);

		// read contract fields
		await decoder.ReadContractFields(cancellationToken);

		// read order fields
		await decoder.ReadAction(cancellationToken);
		await decoder.ReadTotalQuantity(cancellationToken);
		await decoder.ReadOrderType(cancellationToken);
		await decoder.ReadLmtPrice(cancellationToken);
		await decoder.ReadAuxPrice(cancellationToken);
		await decoder.ReadTif(cancellationToken);
		await decoder.ReadOcaGroup(cancellationToken);
		await decoder.ReadAccount(cancellationToken);
		await decoder.ReadOpenClose(cancellationToken);
		await decoder.ReadOrigin(cancellationToken);
		await decoder.ReadOrderRef(cancellationToken);
		await decoder.ReadClientId(cancellationToken);
		await decoder.ReadPermId(cancellationToken);
		await decoder.ReadOutsideRth(cancellationToken);
		await decoder.ReadHidden(cancellationToken);
		await decoder.ReadDiscretionaryAmount(cancellationToken);
		await decoder.ReadGoodAfterTime(cancellationToken);
		await decoder.SkipSharesAllocation(cancellationToken);
		await decoder.ReadFaParams(cancellationToken);
		await decoder.ReadModelCode(cancellationToken);
		await decoder.ReadGoodTillDate(cancellationToken);
		await decoder.ReadRule80A(cancellationToken);
		await decoder.ReadPercentOffset(cancellationToken);
		await decoder.ReadSettlingFirm(cancellationToken);
		await decoder.ReadShortSaleParams(cancellationToken);
		await decoder.ReadAuctionStrategy(cancellationToken);
		await decoder.ReadBoxOrderParams(cancellationToken);
		await decoder.ReadPegToStkOrVolOrderParams(cancellationToken);
		await decoder.ReadDisplaySize(cancellationToken);
		await decoder.ReadOldStyleOutsideRth(cancellationToken);
		await decoder.ReadBlockOrder(cancellationToken);
		await decoder.ReadSweepToFill(cancellationToken);
		await decoder.ReadAllOrNone(cancellationToken);
		await decoder.ReadMinQty(cancellationToken);
		await decoder.ReadOcaType(cancellationToken);
		await decoder.SkipETradeOnly(cancellationToken);
		await decoder.SkipFirmQuoteOnly(cancellationToken);
		await decoder.SkipNbboPriceCap(cancellationToken);
		await decoder.ReadParentId(cancellationToken);
		await decoder.ReadTriggerMethod(cancellationToken);
		await decoder.ReadVolOrderParams(true, cancellationToken);
		await decoder.ReadTrailParams(cancellationToken);
		await decoder.ReadBasisPoints(cancellationToken);
		await decoder.ReadComboLegs(cancellationToken);
		await decoder.ReadSmartComboRoutingParams(cancellationToken);
		await decoder.ReadScaleOrderParams(cancellationToken);
		await decoder.ReadHedgeParams(cancellationToken);
		await decoder.ReadOptOutSmartRouting(cancellationToken);
		await decoder.ReadClearingParams(cancellationToken);
		await decoder.ReadNotHeld(cancellationToken);
		await decoder.ReadDeltaNeutral(cancellationToken);
		await decoder.ReadAlgoParams(cancellationToken);
		await decoder.ReadSolicited(cancellationToken);
		await decoder.ReadWhatIfInfoAndCommission(cancellationToken);
		await decoder.ReadVolRandomizeFlags(cancellationToken);
		await decoder.ReadPegToBenchParams(cancellationToken);
		await decoder.ReadConditions(cancellationToken);
		await decoder.ReadAdjustedOrderParams(cancellationToken);
		await decoder.ReadSoftDollarTier(cancellationToken);
		await decoder.ReadCashQty(cancellationToken);
		await decoder.ReadDontUseAutoPriceForHedge(cancellationToken);
		await decoder.ReadIsOmsContainer(cancellationToken);
		await decoder.ReadDiscretionaryUpToLimitPrice(cancellationToken);
		await decoder.ReadUsePriceMgmtAlgo(cancellationToken);
		await decoder.ReadDuration(cancellationToken);
		await decoder.ReadPostToAts(cancellationToken);
		await decoder.ReadAutoCancelParent(ServerVersions.AutoCancelParent, cancellationToken);
		await decoder.ReadPegBestPegMidOrderAttributes(cancellationToken);
		await decoder.ReadCustomerAccount(cancellationToken);
		await decoder.ReadProfessionalCustomer(cancellationToken);
		await decoder.ReadBondAccruedInterest(cancellationToken);

		var orderMsg = decoder.Order;

		if (orderMsg.OrderState == OrderStates.Pending)
			orderMsg.OrderState = OrderStates.Active;

		orderMsg.OriginalTransactionId = GetTransactionId(FixId(orderMsg.OriginalTransactionId));

		if (orderMsg.OrderState == OrderStates.Active || orderMsg.OrderState == OrderStates.Done)
			orderMsg.OrderId = orderMsg.OriginalTransactionId;

		orderMsg.ServerTime = DateTime.UtcNow;

		await SendOutMessageAsync(decoder.Security, cancellationToken);
		await SendOutMessageAsync(orderMsg, cancellationToken);
	}

	private async ValueTask ReadNextOrderId(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		_nextNativeOrderId = await socket.ReadIntAsync(cancellationToken);
	}

	private async ValueTask ReadManagedAccounts(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/managedaccounts.htm

		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		var names = await socket.ReadStringAsync(cancellationToken);
		//managedAccounts(accountsList);

		foreach (var name in names.SplitByComma(true))
		{
			await SendOutMessageAsync(new PortfolioMessage { PortfolioName = GetBoardCode(name) }, cancellationToken);

			await SubscribePortfolio(name, cancellationToken);
		}
	}

	private async ValueTask ReadMyTrade(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/execdetails.htm

		var version = socket.ServerVersion;

		if (version < ServerVersions.LastLiquidity)
			version = await socket.ReadVersionAsync(cancellationToken);

		/* requestId */
		if (version >= ServerVersions.V7)
			await socket.ReadIntAsync(cancellationToken);

		// http://www.interactivebrokers.com/en/software/api/apiguide/java/execution.htm

		var transactionId = GetTransactionId(FixId(await socket.ReadIntAsync(cancellationToken)));

		//Read Contract Fields
		var contractId = version >= ServerVersions.V5 ? await socket.ReadIntAsync(cancellationToken) : (int?)null;

		var symbol = await socket.ReadStringAsync(cancellationToken);
		var type = await socket.ReadStringAsync(cancellationToken);
		var expiryDate = await socket.ReadStringAsync(cancellationToken);
		var strike = await socket.ReadStrikeAsync(cancellationToken);
		var optionType = await socket.ReadOptionTypeAsync(cancellationToken);
		var multiplier = version >= ServerVersions.V9 ? await socket.ReadNullDecimalAsync(cancellationToken) : null;
		var exchange = await socket.ReadStringAsync(cancellationToken);
		var currency = await socket.ReadStringAsync(cancellationToken);
		var localSymbol = await socket.ReadStringAsync(cancellationToken);
		var secClass = version >= ServerVersions.V10 ? await socket.ReadStringAsync(cancellationToken) : null;

		var tradeId = await socket.ReadStringAsync(cancellationToken);
		var time = (await socket.ReadStringAsync(cancellationToken)).ReadDateTime(this, out _);
		var portfolio = await socket.ReadStringAsync(cancellationToken);
		var execExchange = await socket.ReadStringAsync(cancellationToken);
		var side = await socket.ReadTradeSideAsync(cancellationToken);
		var volume = await socket.ReadDecimalAsync(cancellationToken);
		var price = await socket.ReadDecimalAsync(cancellationToken);

		var permId = version >= ServerVersions.V2 ? await socket.ReadIntAsync(cancellationToken) : (int?)null;
		var clientId = version >= ServerVersions.V3 ? await socket.ReadIntAsync(cancellationToken) : (int?)null;
		var liquidation = version >= ServerVersions.V4 ? await socket.ReadIntAsync(cancellationToken) : (int?)null;

		var cumulativeQuantity = version >= ServerVersions.V6 ? await socket.ReadDecimalAsync(cancellationToken) : (decimal?)null;
		var averagePrice = version >= ServerVersions.V6 ? await socket.ReadDecimalAsync(cancellationToken) : (decimal?)null;

		var orderRef = version >= ServerVersions.V8 ? await socket.ReadStringAsync(cancellationToken) : null;

		var evRule = version >= ServerVersions.V9 ? await socket.ReadStringAsync(cancellationToken) : null;
		var evMultiplier = version >= ServerVersions.V9 ? await socket.ReadDecimalAsync(cancellationToken) : (decimal?)null;

		var modelCode = socket.ServerVersion >= ServerVersions.ModelsSupport ? await socket.ReadStringAsync(cancellationToken) : null;

		var lastLiquidity = socket.ServerVersion >= ServerVersions.LastLiquidity ? await socket.ReadIntAsync(cancellationToken) : (int?)null;

		var pendingPriceRevision = socket.ServerVersion >= ServerVersions.MinServerVerPendingPriceRevision ? await socket.ReadBoolAsync(cancellationToken) : (bool?)null;

		var secId = new SecurityId
		{
			SecurityCode = GetSecurityCode(symbol, type, currency, localSymbol, expiryDate),
			BoardCode = GetBoardCode(exchange),
			InteractiveBrokers = contractId,
		};

		await SendOutMessageAsync(new SecurityMessage
		{
			SecurityId = secId,
			SecurityType = type.ToSecurityType(this, out var underlyingSecurityType),
			UnderlyingSecurityType = underlyingSecurityType,
			ExpiryDate = expiryDate.ReadNullDate(this),
			Strike = strike,
			OptionType = optionType,
			Currency = ToCurrency(currency),
			Multiplier = multiplier ?? 0,
			Class = secClass,
			PrimaryId = new SecurityId
			{
				SecurityCode = localSymbol,
				BoardCode = GetBoardCode(null),
			}
		}, cancellationToken);

		// заявка была создана руками
		if (transactionId == 0)
			return;

		_secIdByTradeIds[tradeId] = secId;

		var execMsg = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = transactionId,
			TradeStringId = tradeId,
			Side = side ?? default,
			TradePrice = price,
			TradeVolume = volume,
			PortfolioName = GetBoardCode(portfolio),
			ServerTime = time ?? CurrentTime,
			SecurityId = secId,
			ClientCode = modelCode,
			AveragePrice = averagePrice,
		};

		//if (permId != null)
		//	execMsg.SetPermId(permId.Value);

		//if (clientId != null)
		//	execMsg.SetClientId(clientId.Value);

		//if (liquidation != null)
		//	execMsg.SetLiquidation(liquidation.Value);

		//if (cumulativeQuantity != null)
		//	execMsg.SetCumulativeQuantity(cumulativeQuantity.Value);

		if (orderRef != null)
			execMsg.Comment = orderRef;

		//if (evRule != null)
		//	execMsg.SetEvRule(evRule);

		//if (evMultiplier != null)
		//	execMsg.SetEvMultiplier(evMultiplier.Value);

		switch (lastLiquidity)
		{
			case null:
				break;
			case 1:
				execMsg.OriginSide = side?.Invert();
				break;
			case 2:
			case 3:
				execMsg.OriginSide = side;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(lastLiquidity), lastLiquidity, LocalizedStrings.InvalidValue);
		}

		await SendOutMessageAsync(execMsg, cancellationToken);
	}

	private async ValueTask ReadMyTradeEnd(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/execdetailsend.htm

		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		/* requestId */await socket.ReadIntAsync(cancellationToken);
		//executionDataEnd(requestId);
	}

	private async ValueTask ReadCommissionReport(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		var tradeId = await socket.ReadStringAsync(cancellationToken);
		var value = await socket.ReadDecimalAsync(cancellationToken);
		var currency = await socket.ReadStringAsync(cancellationToken);
		var pnl = await socket.ReadNullDecimalAsync(cancellationToken);
		var yield = await socket.ReadNullDecimalAsync(cancellationToken);
		var redemptionDate = await socket.ReadNullDateAsync(cancellationToken);

		if (!_secIdByTradeIds.TryGetValue(tradeId, out var secId))
			return;

		// TODO
		//SendOutMessage(new ExecutionMessage
		//{
		//	DataTypeEx = DataType.Trade,
		//	TradeStringId = tradeId,
		//	Commission = value,
		//	SecurityId = secId.Value,
		//});
	}

	private async ValueTask ReadPosition(IBSocket socket, CancellationToken cancellationToken)
	{
		var version = await socket.ReadVersionAsync(cancellationToken);

		var account = await socket.ReadStringAsync(cancellationToken);
		var contractId = await socket.ReadIntAsync(cancellationToken);
		var symbol = await socket.ReadStringAsync(cancellationToken);
		var type = await socket.ReadStringAsync(cancellationToken);
		var expiryDate = await socket.ReadStringAsync(cancellationToken);
		var strike = await socket.ReadStrikeAsync(cancellationToken);
		var optionType = await socket.ReadOptionTypeAsync(cancellationToken);
		var multiplier = await socket.ReadNullDecimalAsync(cancellationToken);
		var exchange = await socket.ReadStringAsync(cancellationToken);
		var currency = await socket.ReadStringAsync(cancellationToken);
		var localSymbol = await socket.ReadStringAsync(cancellationToken);
		var secClass = version >= ServerVersions.V2 ? await socket.ReadStringAsync(cancellationToken) : null;

		var pos = await socket.ReadDecimalAsync(cancellationToken);

		decimal? avgCost = null;
		if (version >= ServerVersions.V3)
			avgCost = await socket.ReadDecimalAsync(cancellationToken);

		var secId = new SecurityId
		{
			SecurityCode = GetSecurityCode(symbol, type, currency, localSymbol, expiryDate),
			BoardCode = GetBoardCode(exchange),
			InteractiveBrokers = contractId,
		};

		await SendOutMessageAsync(new SecurityMessage
		{
			SecurityId = secId,
			SecurityType = type.ToSecurityType(this, out var underlyingSecurityType),
			UnderlyingSecurityType = underlyingSecurityType,
			ExpiryDate = expiryDate.ReadNullDate(this),
			Strike = strike,
			OptionType = optionType,
			Currency = ToCurrency(currency),
			Multiplier = multiplier ?? 0,
			Class = secClass,
			PrimaryId = new SecurityId
			{
				SecurityCode = localSymbol,
				BoardCode = GetBoardCode(null),
			}
		}, cancellationToken);

		await SendOutMessageAsync(this
			.CreatePositionChangeMessage(account, secId)
				.Add(PositionChangeTypes.CurrentValue, pos)
				.TryAdd(PositionChangeTypes.AveragePrice, avgCost), cancellationToken);
	}

	private async ValueTask ReadPositionEnd(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var version = */await socket.ReadVersionAsync(cancellationToken);
	}

	private async ValueTask ReadAccountSummary(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		/*var requestId = */await socket.ReadIntAsync(cancellationToken);

		var account = await socket.ReadStringAsync(cancellationToken);
		var tag = await socket.ReadStringAsync(cancellationToken);
		var value = await socket.ReadStringAsync(cancellationToken);
		var currency = await socket.ReadStringAsync(cancellationToken);

		var msg = this.CreatePortfolioChangeMessage(account);

		if (!currency.IsEmpty())
			msg.TryAdd(PositionChangeTypes.Currency, ToCurrency(currency));

		switch (tag.ToAccountTag())
		{
			case AccountSummaryTag.TotalCashValue:
				msg.Add(PositionChangeTypes.CurrentValue, value.To<decimal>());
				break;
			case AccountSummaryTag.SettledCash:
				msg.Add(PositionChangeTypes.BlockedValue, value.To<decimal>());
				break;
			case AccountSummaryTag.AccruedCash:
				msg.Add(PositionChangeTypes.VariationMargin, value.To<decimal>());
				break;
			case AccountSummaryTag.InitMarginReq:
				msg.Add(PositionChangeTypes.BeginValue, value.To<decimal>());
				break;
			case AccountSummaryTag.Leverage:
				msg.Add(PositionChangeTypes.Leverage, value.To<decimal>());
				break;
		}

		await SendOutMessageAsync(msg, cancellationToken);
	}

	private async ValueTask ReadPositionMulti(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var version = */await socket.ReadIntAsync(cancellationToken);
		/*var requestId = */await socket.ReadIntAsync(cancellationToken);
		var account = await socket.ReadStringAsync(cancellationToken);

		var contractId = await socket.ReadIntAsync(cancellationToken);
		var symbol = await socket.ReadStringAsync(cancellationToken);
		var type = await socket.ReadStringAsync(cancellationToken);
		var expiryDate = await socket.ReadStringAsync(cancellationToken);
		var strike = await socket.ReadStrikeAsync(cancellationToken);
		var optionType = await socket.ReadOptionTypeAsync(cancellationToken);
		var multiplier = await socket.ReadNullDecimalAsync(cancellationToken);
		var exchange = await socket.ReadStringAsync(cancellationToken);
		var currency = await socket.ReadStringAsync(cancellationToken);
		var localSymbol = await socket.ReadStringAsync(cancellationToken);
		var secClass = await socket.ReadStringAsync(cancellationToken);

		var secId = new SecurityId
		{
			InteractiveBrokers = contractId,
			SecurityCode = GetSecurityCode(symbol, type, currency, localSymbol, expiryDate),
			BoardCode = GetBoardCode(exchange),
		};

		var secMsg = new SecurityMessage
		{
			SecurityId = secId,
			Strike = strike,
			OptionType = optionType,
			Multiplier = multiplier,
			Currency = ToCurrency(currency),
			Class = secClass,
			ExpiryDate = expiryDate.ReadNullDate(this),
			SecurityType = type.ToSecurityType(this, out var underlyingSecurityType),
			UnderlyingSecurityType = underlyingSecurityType,
			PrimaryId = new SecurityId
			{
				SecurityCode = localSymbol,
				BoardCode = GetBoardCode(null),
			}
		};

		await SendOutMessageAsync(secMsg, cancellationToken);

		var pos = await socket.ReadDecimalAsync(cancellationToken);
		var avgCost = await socket.ReadDecimalAsync(cancellationToken);
		var modelCode = await socket.ReadStringAsync(cancellationToken);

		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetBoardCode(account),
			SecurityId = secId,
			ClientCode = modelCode
		}
		.TryAdd(PositionChangeTypes.AveragePrice, avgCost)
		.TryAdd(PositionChangeTypes.CurrentValue, pos, true), cancellationToken);

		//eWrapper.positionMulti(requestId, account, modelCode, contract, pos, avgCost);
	}

	private async ValueTask ReadPositionMultiEnd(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var version = */await socket.ReadIntAsync(cancellationToken);
		/*var requestId = */await socket.ReadIntAsync(cancellationToken);
		//eWrapper.positionMultiEnd(requestId);
	}

	private async ValueTask ReadAccountUpdateMulti(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var version = */await socket.ReadIntAsync(cancellationToken);
		/*var requestId = */await socket.ReadIntAsync(cancellationToken);
		var account = await socket.ReadStringAsync(cancellationToken);
		var modelCode = await socket.ReadStringAsync(cancellationToken);
		var key = await socket.ReadStringAsync(cancellationToken);
		var value = await socket.ReadStringAsync(cancellationToken);
		var currency = await socket.ReadStringAsync(cancellationToken);

		if (!account.IsEmpty())
		{
			await SendOutMessageAsync(new PortfolioMessage { PortfolioName = GetBoardCode(account) }, cancellationToken);
			await SubscribePortfolio(account, cancellationToken);
		}
		//eWrapper.accountUpdateMulti(requestId, account, modelCode, key, value, currency);
	}

	private async ValueTask ReadAccountUpdateMultiEnd(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var version = */await socket.ReadIntAsync(cancellationToken);
		/*var requestId = */await socket.ReadIntAsync(cancellationToken);
		//eWrapper.accountUpdateMultiEnd(requestId);
	}

	private void OnProcessOrderCancelled(long id)
	{
		//SendOutMessage(new ExecutionMessage
		//{
		//	HasOrderInfo = true,
		//	DataTypeEx = DataType.Transactions,
		//	OriginalTransactionId = GetTransactionId(id),
		//	OrderState = OrderStates.Done
		//});
	}

	private async ValueTask OnProcessOrderErrorAsync(long id, string errorMsg, CancellationToken cancellationToken)
	{
		if (_messageRequests.TryGetValue(id, out var type) && type == MessageTypes.OrderCancel)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = GetTransactionId(id),
				OrderState = OrderStates.Failed,
				Error = new InvalidOperationException(errorMsg),
				HasOrderInfo = true,
			}, cancellationToken);
		}
		else
		{
			_orderRegErrors[id] = errorMsg;

			if (_nativeOrderIds.TryGetKey(id, out var transId))
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = transId,
					OrderState = OrderStates.Failed,
					Error = new InvalidOperationException(errorMsg),
					HasOrderInfo = true,
				}, cancellationToken);
			}
		}
	}

	private long? CurrOrderStatus => _messageRequests.SyncGet(d => d.FirstOrDefault(p => p.Value == MessageTypes.OrderStatus).Key.DefaultAsNull());

	private async ValueTask ReadOpenOrderEnd(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var version = */await socket.ReadVersionAsync(cancellationToken);

		var id = CurrOrderStatus;

		if (id != null)
		{
			_messageRequests.Remove(id.Value);
			await SendSubscriptionOnlineAsync(id.Value, cancellationToken);
		}
	}

	private async ValueTask ReadPnLSingle(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var pos = await socket.ReadDecimalAsync(cancellationToken);
		var dailyPnL = await socket.ReadDecimalAsync(cancellationToken);
		var unrealizedPnL = (decimal?)null;
		var realizedPnL = (decimal?)null;

		if (socket.ServerVersion >= ServerVersions.UnrealPnL)
		{
			unrealizedPnL = await socket.ReadDecimalAsync(cancellationToken);
		}

		if (socket.ServerVersion >= ServerVersions.RealPnL)
		{
			realizedPnL = await socket.ReadDecimalAsync(cancellationToken);
		}

		var value = await socket.ReadDecimalAsync(cancellationToken);

		await SendOutMessageAsync(new PositionChangeMessage
		{
			ServerTime = CurrentTime,
			OriginalTransactionId = requestId,
		}
		.Add(PositionChangeTypes.CurrentValue, pos)
		.Add(PositionChangeTypes.CurrentPrice, value)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, unrealizedPnL, true)
		.Add(PositionChangeTypes.RealizedPnL, realizedPnL ?? dailyPnL), cancellationToken);
	}

	private async ValueTask ReadPnL(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var dailyPnL = await socket.ReadDecimalAsync(cancellationToken);
		var unrealizedPnL = (decimal?)null;
		var realizedPnL = (decimal?)null;

		if (socket.ServerVersion >= ServerVersions.UnrealPnL)
		{
			unrealizedPnL = await socket.ReadDecimalAsync(cancellationToken);
		}

		if (socket.ServerVersion >= ServerVersions.RealPnL)
		{
			realizedPnL = await socket.ReadDecimalAsync(cancellationToken);
		}

		var account = _pnlAccounts.TryGetValue(requestId);

		await SendOutMessageAsync(new PositionChangeMessage
		{
			SecurityId = SecurityId.Money,
			PortfolioName = GetBoardCode(account),
		}
		.TryAdd(PositionChangeTypes.UnrealizedPnL, unrealizedPnL, true)
		.Add(PositionChangeTypes.RealizedPnL, realizedPnL ?? dailyPnL), cancellationToken);
	}

	private async ValueTask ReadOrderBound(IBSocket socket, CancellationToken cancellationToken)
	{
		var orderId = await socket.ReadLongAsync(cancellationToken);
		var apiClientId = await socket.ReadIntAsync(cancellationToken);
		var apiOrderId = await socket.ReadIntAsync(cancellationToken);

		//eWrapper.orderBound(orderId, apiClientId, apiOrderId);
	}

	private async ValueTask ReadCompletedOrder(IBSocket socket, CancellationToken cancellationToken)
	{
		var decoder = new OrderDecoder(socket, (ServerVersions)int.MaxValue, GetSecurityCode, GetBoardCode, ex => this.AddErrorLog(ex));

		// read contract fields
		await decoder.ReadContractFields(cancellationToken);

		// read order fields
		await decoder.ReadAction(cancellationToken);
		await decoder.ReadTotalQuantity(cancellationToken);
		await decoder.ReadOrderType(cancellationToken);
		await decoder.ReadLmtPrice(cancellationToken);
		await decoder.ReadAuxPrice(cancellationToken);
		await decoder.ReadTif(cancellationToken);
		await decoder.ReadOcaGroup(cancellationToken);
		await decoder.ReadAccount(cancellationToken);
		await decoder.ReadOpenClose(cancellationToken);
		await decoder.ReadOrigin(cancellationToken);
		await decoder.ReadOrderRef(cancellationToken);
		await decoder.ReadPermId(cancellationToken);
		await decoder.ReadOutsideRth(cancellationToken);
		await decoder.ReadHidden(cancellationToken);
		await decoder.ReadDiscretionaryAmount(cancellationToken);
		await decoder.ReadGoodAfterTime(cancellationToken);
		await decoder.ReadFaParams(cancellationToken);
		await decoder.ReadModelCode(cancellationToken);
		await decoder.ReadGoodTillDate(cancellationToken);
		await decoder.ReadRule80A(cancellationToken);
		await decoder.ReadPercentOffset(cancellationToken);
		await decoder.ReadSettlingFirm(cancellationToken);
		await decoder.ReadShortSaleParams(cancellationToken);
		await decoder.ReadBoxOrderParams(cancellationToken);
		await decoder.ReadPegToStkOrVolOrderParams(cancellationToken);
		await decoder.ReadDisplaySize(cancellationToken);
		await decoder.ReadSweepToFill(cancellationToken);
		await decoder.ReadAllOrNone(cancellationToken);
		await decoder.ReadMinQty(cancellationToken);
		await decoder.ReadOcaType(cancellationToken);
		await decoder.ReadTriggerMethod(cancellationToken);
		await decoder.ReadVolOrderParams(false, cancellationToken);
		await decoder.ReadTrailParams(cancellationToken);
		await decoder.ReadComboLegs(cancellationToken);
		await decoder.ReadSmartComboRoutingParams(cancellationToken);
		await decoder.ReadScaleOrderParams(cancellationToken);
		await decoder.ReadHedgeParams(cancellationToken);
		await decoder.ReadClearingParams(cancellationToken);
		await decoder.ReadNotHeld(cancellationToken);
		await decoder.ReadDeltaNeutral(cancellationToken);
		await decoder.ReadAlgoParams(cancellationToken);
		await decoder.ReadSolicited(cancellationToken);
		await decoder.ReadOrderStatus(cancellationToken);
		await decoder.ReadVolRandomizeFlags(cancellationToken);
		await decoder.ReadPegToBenchParams(cancellationToken);
		await decoder.ReadConditions(cancellationToken);
		await decoder.ReadStopPriceAndLmtPriceOffset(cancellationToken);
		await decoder.ReadCashQty(cancellationToken);
		await decoder.ReadDontUseAutoPriceForHedge(cancellationToken);
		await decoder.ReadIsOmsContainer(cancellationToken);
		await decoder.ReadAutoCancelDate(cancellationToken);
		await decoder.ReadFilledQuantity(cancellationToken);
		await decoder.ReadRefFuturesConId(cancellationToken);
		await decoder.ReadAutoCancelParent(cancellationToken);
		await decoder.ReadShareholder(cancellationToken);
		await decoder.ReadImbalanceOnly(cancellationToken);
		await decoder.ReadRouteMarketableToBbo(cancellationToken);
		await decoder.ReadParentPermId(cancellationToken);
		await decoder.ReadCompletedTime(cancellationToken);
		await decoder.ReadCompletedStatus(cancellationToken);
		await decoder.ReadPegBestPegMidOrderAttributes(cancellationToken);
		await decoder.ReadCustomerAccount(cancellationToken);
		await decoder.ReadProfessionalCustomer(cancellationToken);

		await SendOutMessageAsync(decoder.Security, cancellationToken);
		await SendOutMessageAsync(decoder.Order, cancellationToken);
	}

	private ValueTask ReadCompletedOrdersEnd(IBSocket socket, CancellationToken cancellationToken)
	{
		return default;
	}

	private async ValueTask ReadUserInfoEvent(IBSocket socket, CancellationToken cancellationToken)
	{
		var reqId = await socket.ReadLongAsync(cancellationToken);
		var whiteBrandingId = await socket.ReadStringAsync(cancellationToken);

		await SendOutMessageAsync(new UserInfoMessage
		{
			OriginalTransactionId = reqId,
			DisplayName = whiteBrandingId,
		}, cancellationToken);
	}
}