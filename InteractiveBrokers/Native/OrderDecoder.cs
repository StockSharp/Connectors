namespace StockSharp.InteractiveBrokers.Native;

class OrderDecoder
{
	private readonly IBSocket _socket;
	private readonly ServerVersions _msgVersion;
	private readonly Func<string, string, string, string, string, string> _getCode;
	private readonly Func<string, string> _getBoard;
	private readonly Action<Exception> _errorHandler;
	private readonly InteractiveBrokersOrderCondition _condition;

	public OrderDecoder(IBSocket socket, ServerVersions msgVersion, Func<string, string, string, string, string, string> getCode, Func<string, string> getBoard, Action<Exception> errorHandler)
	{
		_socket = socket ?? throw new ArgumentNullException(nameof(socket));
		_msgVersion = msgVersion;
		_getCode = getCode ?? throw new ArgumentNullException(nameof(getCode));
		_getBoard = getBoard ?? throw new ArgumentNullException(nameof(getBoard));
		_errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));

		Order = new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			Condition = _condition = new InteractiveBrokersOrderCondition()
		};

		Security = new SecurityMessage();
	}

	public ExecutionMessage Order { get; }
	public SecurityMessage Security { get; }

	public async ValueTask ReadOrderId(CancellationToken cancellationToken)
	{
		Order.OriginalTransactionId = await _socket.ReadIntAsync(cancellationToken);
	}

	public async ValueTask ReadAction(CancellationToken cancellationToken)
	{
		Order.Side = await _socket.ReadOrderSideAsync(cancellationToken);
	}

	public async ValueTask ReadContractFields(CancellationToken cancellationToken)
	{
		int? contractId = null;
		if (_msgVersion >= ServerVersions.V17)
			contractId = await _socket.ReadIntAsync(cancellationToken);

		var symbol = await _socket.ReadStringAsync(cancellationToken);
		var type = await _socket.ReadStringAsync(cancellationToken);
		var expiry = await _socket.ReadStringAsync(cancellationToken);

		Security.SecurityType = type.ToSecurityType(_socket, out var underlyingSecurityType);
		Security.UnderlyingSecurityType = underlyingSecurityType;
		Security.ExpiryDate = expiry.ReadNullDate(_socket);
		Security.Strike = (await _socket.ReadDecimalAsync(cancellationToken)).DefaultAsNull();
		Security.OptionType = await _socket.ReadOptionTypeAsync(cancellationToken);

		if (_msgVersion >= ServerVersions.V32)
			Security.Multiplier = await _socket.ReadNullDecimalAsync(cancellationToken);

		var exchange = await _socket.ReadStringAsync(cancellationToken);
		var currency = await _socket.ReadStringAsync(cancellationToken);

		Security.Currency = currency.FromMicexCurrencyName(_errorHandler);

		string localSymbol = null;
		if (_msgVersion >= ServerVersions.V2)
			localSymbol = await _socket.ReadStringAsync(cancellationToken);

		if (_msgVersion >= ServerVersions.V32)
			Security.Class = await _socket.ReadStringAsync(cancellationToken);

		Order.SecurityId = Security.SecurityId = new SecurityId
		{
			SecurityCode = _getCode(symbol, type, currency, localSymbol, expiry),
			BoardCode = _getBoard(exchange),
			InteractiveBrokers = contractId,
		};

		Security.PrimaryId = new SecurityId
		{
			SecurityCode = localSymbol,
			BoardCode = _getBoard(null),
		};
	}

	public async ValueTask ReadTotalQuantity(CancellationToken cancellationToken)
	{
		Order.OrderVolume = _socket.ServerVersion >= ServerVersions.FractionalPositions ? await _socket.ReadDecimalAsync(cancellationToken) : await _socket.ReadIntAsync(cancellationToken);
	}

	public async ValueTask ReadOrderType(CancellationToken cancellationToken)
	{
		var (orderType, extendedType) = await _socket.ReadOrderTypeAsync(cancellationToken);
		Order.OrderType = orderType;
		_condition.ExtendedType = extendedType;
	}

	public async ValueTask ReadLmtPrice(CancellationToken cancellationToken)
	{
		if (_msgVersion < ServerVersions.V29)
		{
			Order.OrderPrice = await _socket.ReadDecimalAsync(cancellationToken);
		}
		else
		{
			Order.OrderPrice = await _socket.ReadNullDecimalAsync(cancellationToken) ?? 0;
		}
	}

	public async ValueTask ReadAuxPrice(CancellationToken cancellationToken)
	{
		if (_msgVersion < ServerVersions.V30)
		{
			_condition.StopPrice = await _socket.ReadDecimalAsync(cancellationToken);
		}
		else
		{
			_condition.StopPrice = await _socket.ReadNullDecimalAsync(cancellationToken);
		}
	}

	public async ValueTask ReadTif(CancellationToken cancellationToken)
	{
		var expiration = await _socket.ReadStringAsync(cancellationToken);

		switch (expiration)
		{
			case "DAY":
			case "OVERNIGHT + DAY":
				Order.TimeInForce = TimeInForce.PutInQueue;
				break;
			case "GTC":
				//Order.ExpiryDate = DateTime.MaxValue;
				break;
			case "IOC":
				Order.TimeInForce = TimeInForce.CancelBalance;
				break;
			case "FOK":
				Order.TimeInForce = TimeInForce.MatchOrCancel;
				break;
			case "GTD":
				break;
			case "OPG":
				_condition.IsMarketOnOpen = true;
				break;
			default:
				throw new InvalidOperationException(LocalizedStrings.UnsupportedType.Put(expiration));
		}
	}

	public async ValueTask ReadOcaGroup(CancellationToken cancellationToken)
	{
		_condition.Oca.Group = await _socket.ReadStringAsync(cancellationToken);
	}

	public async ValueTask ReadAccount(CancellationToken cancellationToken)
	{
		Order.PortfolioName = await _socket.ReadStringAsync(cancellationToken);
	}

	public async ValueTask ReadOpenClose(CancellationToken cancellationToken)
	{
		_condition.IsOpenOrClose = await _socket.ReadStringAsync(cancellationToken) == "O";
	}

	public async ValueTask ReadOrigin(CancellationToken cancellationToken)
	{
		_condition.Origin = (InteractiveBrokersOrderCondition.OrderOrigins)await _socket.ReadIntAsync(cancellationToken);
	}

	public async ValueTask ReadOrderRef(CancellationToken cancellationToken)
	{
		Order.Comment = await _socket.ReadStringAsync(cancellationToken);
	}

	public async ValueTask ReadClientId(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V3)
		{
			/*var clientId = */await _socket.ReadIntAsync(cancellationToken);
		}
	}

	public async ValueTask ReadPermId(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V4)
		{
			/*var permId = */await _socket.ReadIntAsync(cancellationToken);
		}
	}

	public async ValueTask ReadOutsideRth(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V4)
		{
			if (_msgVersion < ServerVersions.V18)
			{
				// will never happen
				/* order.ignoreRth = */
				await _socket.ReadBoolAsync(cancellationToken);
			}
			else
			{
				_condition.OutsideRth = await _socket.ReadBoolAsync(cancellationToken);
			}
		}
	}

	public async ValueTask ReadHidden(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V4)
		{
			_condition.Hidden = await _socket.ReadIntAsync(cancellationToken) == 1;
		}
	}

	public async ValueTask ReadDiscretionaryAmount(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V4)
		{
			_condition.SmartRouting.DiscretionaryAmount = await _socket.ReadDecimalAsync(cancellationToken);
		}
	}

	public async ValueTask ReadGoodAfterTime(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V5)
		{
			_condition.GoodAfterTime = (await _socket.ReadStringAsync(cancellationToken)).ReadDateTime(_socket, out _);
		}
	}

	public async ValueTask SkipSharesAllocation(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V6)
		{
			// skip deprecated sharesAllocation field
			await _socket.ReadStringAsync(cancellationToken);
		}
	}

	public async ValueTask ReadFaParams(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V7)
		{
			_condition.FinancialAdvisor.Group = await _socket.ReadStringAsync(cancellationToken);
			_condition.FinancialAdvisor.Allocation = await _socket.ReadFinancialAdvisorAsync(cancellationToken);
			_condition.FinancialAdvisor.Percentage = await _socket.ReadStringAsync(cancellationToken);

			if (_socket.ServerVersion < ServerVersions.MinServerVerFaProfileDesupport)
				await _socket.ReadStringAsync(cancellationToken); // skip deprecated faProfile field
		}
	}

	public async ValueTask ReadModelCode(CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion >= ServerVersions.ModelsSupport)
		{
			Order.ClientCode = await _socket.ReadStringAsync(cancellationToken);
		}
	}

	public async ValueTask ReadGoodTillDate(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V8)
		{
			Order.ExpiryDate = (await _socket.ReadStringAsync(cancellationToken)).ReadDateTime(_socket, out _);
		}
	}

	public async ValueTask ReadRule80A(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			_condition.Agent = await _socket.ReadAgentAsync(cancellationToken);
		}
	}

	public async ValueTask ReadPercentOffset(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			_condition.PercentOffset = await _socket.ReadNullDecimalAsync(cancellationToken);
		}
	}

	public async ValueTask ReadSettlingFirm(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			_condition.Clearing.SettlingFirm = await _socket.ReadStringAsync(cancellationToken);
		}
	}

	public async ValueTask ReadShortSaleParams(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			_condition.ShortSale.Slot = (InteractiveBrokersOrderCondition.ShortSaleSlots)await _socket.ReadIntAsync(cancellationToken);
			_condition.ShortSale.Location = await _socket.ReadStringAsync(cancellationToken);
			if (_socket.ServerVersion == ServerVersions.SShortXOld)
			{
				_condition.ShortSale.ExemptCode = await _socket.ReadIntAsync(cancellationToken); // exemptCode
			}
			else if (_msgVersion >= ServerVersions.V23)
			{
				_condition.ShortSale.ExemptCode = await _socket.ReadIntAsync(cancellationToken);
			}
		}
	}

	public async ValueTask ReadAuctionStrategy(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			_condition.AuctionStrategy = (InteractiveBrokersOrderCondition.AuctionStrategies)await _socket.ReadIntAsync(cancellationToken);
		}
	}

	public async ValueTask ReadBoxOrderParams(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			_condition.StartingPrice = await _socket.ReadNullDecimalAsync(cancellationToken);
			_condition.StockRefPrice = await _socket.ReadNullDecimalAsync(cancellationToken);
			_condition.Delta = await _socket.ReadNullDecimalAsync(cancellationToken);
		}
	}

	public async ValueTask ReadPegToStkOrVolOrderParams(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			_condition.StockRangeLower = await _socket.ReadNullDecimalAsync(cancellationToken);
			_condition.StockRangeUpper = await _socket.ReadNullDecimalAsync(cancellationToken);
		}
	}

	public async ValueTask ReadDisplaySize(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			Order.VisibleVolume = await _socket.ReadNullIntAsync(cancellationToken);
		}
	}

	public async ValueTask ReadOldStyleOutsideRth(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			if (_msgVersion < ServerVersions.V18)
			{
				// will never happen
				/* order.rthOnly = */
				await _socket.ReadBoolAsync(cancellationToken);
			}
		}
	}

	public async ValueTask ReadBlockOrder(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			_condition.SplitVolume = await _socket.ReadBoolAsync(cancellationToken);
		}
	}

	public async ValueTask ReadSweepToFill(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			_condition.SweepToFill = await _socket.ReadBoolAsync(cancellationToken);
		}
	}

	public async ValueTask ReadAllOrNone(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			_condition.AllOrNone = await _socket.ReadBoolAsync(cancellationToken);
		}
	}

	public async ValueTask ReadMinQty(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			Order.MinVolume = await _socket.ReadNullIntAsync(cancellationToken);
		}
	}

	public async ValueTask ReadOcaType(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			_condition.Oca.Type = (InteractiveBrokersOrderCondition.OcaTypes)await _socket.ReadIntAsync(cancellationToken);
		}
	}

	public async ValueTask SkipETradeOnly(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			/*_condition.SmartRouting.ETradeOnly = */await _socket.ReadBoolAsync(cancellationToken);
		}
	}

	public async ValueTask SkipFirmQuoteOnly(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			/*_condition.SmartRouting.FirmQuoteOnly = */await _socket.ReadBoolAsync(cancellationToken);
		}
	}

	public async ValueTask SkipNbboPriceCap(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V9)
		{
			/*_condition.SmartRouting.NbboPriceCap = */await _socket.ReadNullDecimalAsync(cancellationToken);
		}
	}

	public async ValueTask ReadParentId(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V10)
		{
			_condition.ParentId = await _socket.ReadIntAsync(cancellationToken);
		}
	}

	public async ValueTask ReadTriggerMethod(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V10)
		{
			_condition.TriggerMethod = (InteractiveBrokersOrderCondition.TriggerMethods)await _socket.ReadIntAsync(cancellationToken);
		}
	}

	public async ValueTask ReadVolOrderParams(bool readOpenOrderAttribs, CancellationToken cancellationToken)
	{
		if (_msgVersion < ServerVersions.V11)
			return;

		_condition.Volatility.Volatility = await _socket.ReadNullDecimalAsync(cancellationToken);
		_condition.Volatility.VolatilityTimeFrame = await _socket.ReadVolatilityTypeAsync(cancellationToken);

		if (_msgVersion == ServerVersions.V11)
		{
			if (!await _socket.ReadBoolAsync(cancellationToken))
				_condition.Volatility.ExtendedOrderType = null;
			else
				_condition.Volatility.OrderType = OrderTypes.Market;
		}
		else
		{
			// msgVersion 12 and up
			var (volOrdertype, volExtendedType) = await _socket.ReadOrderTypeAsync(cancellationToken);

			_condition.Volatility.OrderType = volOrdertype;
			_condition.Volatility.ExtendedOrderType = volExtendedType;

			_condition.Volatility.StopPrice = await _socket.ReadNullDecimalAsync(cancellationToken);

			if (volExtendedType != null)
			{
				if (_msgVersion >= ServerVersions.V27)
				{
					_condition.Volatility.ContractId = await _socket.ReadIntAsync(cancellationToken);

					if (readOpenOrderAttribs)
					{
						_condition.Volatility.SettlingFirm = await _socket.ReadStringAsync(cancellationToken);
						_condition.Volatility.ClearingPortfolio = await _socket.ReadStringAsync(cancellationToken);
						_condition.Volatility.ClearingIntent = await _socket.ReadStringAsync(cancellationToken);
					}
				}

				if (_msgVersion >= ServerVersions.V31)
				{
					if (readOpenOrderAttribs)
					{
						var isOpenOrCloseStr = await _socket.ReadStringAsync(cancellationToken);
						_condition.Volatility.ShortSale.IsOpenOrClose = isOpenOrCloseStr == "?" ? null : isOpenOrCloseStr.To<int>() == 1;
					}

					_condition.Volatility.IsShortSale = await _socket.ReadBoolAsync(cancellationToken);
					_condition.Volatility.ShortSale.Slot = (InteractiveBrokersOrderCondition.ShortSaleSlots)await _socket.ReadIntAsync(cancellationToken);
					_condition.Volatility.ShortSale.Location = await _socket.ReadStringAsync(cancellationToken);
				}
			}
		}

		_condition.Volatility.ContinuousUpdate = await _socket.ReadBoolAsync(cancellationToken);

		if (_socket.ServerVersion == ServerVersions.V26)
		{
			_condition.StockRangeLower = await _socket.ReadDecimalAsync(cancellationToken);
			_condition.StockRangeUpper = await _socket.ReadDecimalAsync(cancellationToken);
		}

		_condition.Volatility.IsAverageBestPrice = await _socket.ReadBoolAsync(cancellationToken);
	}

	public async ValueTask ReadTrailParams(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V13)
		{
			_condition.TrailStopPrice = await _socket.ReadNullDecimalAsync(cancellationToken);
		}
		if (_msgVersion >= ServerVersions.V30)
		{
			_condition.TrailStopVolumePercentage = await _socket.ReadNullDecimalAsync(cancellationToken);
		}
	}

	public async ValueTask ReadBasisPoints(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V14)
		{
			_condition.Combo.BasisPoints = await _socket.ReadNullDecimalAsync(cancellationToken);
			_condition.Combo.BasisPointsType = await _socket.ReadNullIntAsync(cancellationToken);
		}
	}
   
	public async ValueTask ReadComboLegs(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V14)
		{
			_condition.Combo.LegsDescription = await _socket.ReadStringAsync(cancellationToken);
		}

		if (_msgVersion >= ServerVersions.V29)
		{
			var contractLegsCount = await _socket.ReadIntAsync(cancellationToken);
			if (contractLegsCount > 0)
			{
				//contract.ComboLegs = new List<ComboLeg>(comboLegsCount);
				for (var i = 0; i < contractLegsCount; ++i)
				{
					/*var conId = */await _socket.ReadIntAsync(cancellationToken);
					/*var ratio = */await _socket.ReadIntAsync(cancellationToken);
					/*var action = */await _socket.ReadStringAsync(cancellationToken);
					/*var exchange = */await _socket.ReadStringAsync(cancellationToken);
					/*var openClose = */await _socket.ReadIntAsync(cancellationToken);
					/*var shortSaleSlot = */await _socket.ReadIntAsync(cancellationToken);
					/*var designatedLocation = */await _socket.ReadStringAsync(cancellationToken);
					/*var exemptCode = */await _socket.ReadIntAsync(cancellationToken);

					//ComboLeg comboLeg = new ComboLeg(conId, ratio, action, exchange, openClose,
					//	shortSaleSlot, designatedLocation, exemptCode);
					//contract.ComboLegs.Add(comboLeg);
				}
			}

			var orderLegsCount = await _socket.ReadIntAsync(cancellationToken);
			if (orderLegsCount > 0)
			{
				//order.OrderComboLegs = new List<OrderComboLeg>(orderLegsCount);
				for (var i = 0; i < orderLegsCount; ++i)
				{
					/*var price = */await _socket.ReadNullDecimalAsync(cancellationToken);

					//OrderComboLeg orderComboLeg = new OrderComboLeg(price);
					//order.OrderComboLegs.Add(orderComboLeg);
				}
			}
		}
	}

	public async ValueTask ReadSmartComboRoutingParams(CancellationToken cancellationToken)
	{
		if (_msgVersion < ServerVersions.V26)
			return;

		var count = await _socket.ReadIntAsync(cancellationToken);
		if (count <= 0)
			return;

		var routeParams = new List<Tuple<string, string>>(count);

		for (var i = 0; i < count; ++i)
			routeParams.Add(Tuple.Create(await _socket.ReadStringAsync(cancellationToken), await _socket.ReadStringAsync(cancellationToken)));

		_condition.SmartRouting.ComboParams = routeParams;
	}

	public async ValueTask ReadScaleOrderParams(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V15)
		{
			if (_msgVersion >= ServerVersions.V20)
			{
				_condition.Scale.InitLevelSize = await _socket.ReadNullIntAsync(cancellationToken);
				_condition.Scale.SubsLevelSize = await _socket.ReadNullIntAsync(cancellationToken);
			}
			else
			{
				/* int notSuppScaleNumComponents = */
				await _socket.ReadNullIntAsync(cancellationToken);
				_condition.Scale.InitLevelSize = await _socket.ReadNullIntAsync(cancellationToken);
			}
			_condition.Scale.PriceIncrement = await _socket.ReadNullDecimalAsync(cancellationToken);
		}

		if (_msgVersion >= ServerVersions.V28 && _condition.Scale.PriceIncrement > 0)
		{
			_condition.Scale.PriceAdjustValue = await _socket.ReadNullDecimalAsync(cancellationToken);
			_condition.Scale.PriceAdjustInterval = await _socket.ReadNullIntAsync(cancellationToken);
			_condition.Scale.ProfitOffset = await _socket.ReadNullDecimalAsync(cancellationToken);
			_condition.Scale.AutoReset = await _socket.ReadBoolAsync(cancellationToken);
			_condition.Scale.InitPosition = await _socket.ReadNullIntAsync(cancellationToken);
			_condition.Scale.InitFillQty = await _socket.ReadNullIntAsync(cancellationToken);
			_condition.Scale.RandomPercent = await _socket.ReadBoolAsync(cancellationToken);
		}
	}

	public async ValueTask ReadHedgeParams(CancellationToken cancellationToken)
	{
		if (_msgVersion < ServerVersions.HistoricalData)
			return;

		_condition.Hedge.Type = await _socket.ReadHedgeTypeAsync(cancellationToken);

		if (_condition.Hedge.Type != null)
		{
			_condition.Hedge.Param = await _socket.ReadStringAsync(cancellationToken);
		}
	}

	public async ValueTask ReadOptOutSmartRouting(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V25)
		{
			_condition.SmartRouting.OptOutSmartRouting = await _socket.ReadBoolAsync(cancellationToken);
		}
	}

	public async ValueTask ReadClearingParams(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V19)
		{
			_condition.Clearing.Portfolio = await _socket.ReadStringAsync(cancellationToken);
			_condition.Clearing.Intent = await _socket.ReadIntentAsync(cancellationToken);
		}
	}

	public async ValueTask ReadNotHeld(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.V22)
		{
			_condition.SmartRouting.NotHeld = await _socket.ReadBoolAsync(cancellationToken);
		}
	}

	public async ValueTask ReadDeltaNeutral(CancellationToken cancellationToken)
	{
		if (_msgVersion < ServerVersions.V20)
			return;

		if (!await _socket.ReadBoolAsync(cancellationToken))
			return;

		//DeltaNeutralContract deltaNeutralContract = new DeltaNeutralContract();
		/*deltaNeutralContract.ConId = */await _socket.ReadIntAsync(cancellationToken);
		/*deltaNeutralContract.Delta = */await _socket.ReadDecimalAsync(cancellationToken);
		/*deltaNeutralContract.Price = */await _socket.ReadDecimalAsync(cancellationToken);
		//contract.DeltaNeutralContract = deltaNeutralContract;
	}

	public async ValueTask ReadAlgoParams(CancellationToken cancellationToken)
	{
		if (_msgVersion < ServerVersions.V21)
			return;

		_condition.Algo.Strategy = await _socket.ReadStringAsync(cancellationToken);

		if (_condition.Algo.Strategy.IsEmpty())
			return;

		var count = await _socket.ReadIntAsync(cancellationToken);
		if (count <= 0)
			return;

		var algoParams = new List<Tuple<string, string>>(count);

		for (var i = 0; i < count; ++i)
		{
			algoParams.Add(Tuple.Create(await _socket.ReadStringAsync(cancellationToken), await _socket.ReadStringAsync(cancellationToken)));
		}

		_condition.Algo.Params = algoParams;
	}

	public async ValueTask ReadSolicited(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.CurrentTime)
		{
			_condition.Solicited = await _socket.ReadBoolAsync(cancellationToken);
		}
	}

	public async ValueTask ReadWhatIfInfoAndCommission(CancellationToken cancellationToken)
	{
		if (_msgVersion < ServerVersions.V16)
			return;

		_condition.WhatIf = await _socket.ReadBoolAsync(cancellationToken);

		await ReadOrderStatus(cancellationToken);

		if (_socket.ServerVersion >= ServerVersions.WhatIfExtFields)
		{
			/*orderState.InitMarginBefore = */await _socket.ReadStringAsync(cancellationToken);
			/*orderState.MaintMarginBefore = */await _socket.ReadStringAsync(cancellationToken);
			/*orderState.EquityWithLoanBefore = */await _socket.ReadStringAsync(cancellationToken);
			/*orderState.InitMarginChange = */await _socket.ReadStringAsync(cancellationToken);
			/*orderState.MaintMarginChange = */await _socket.ReadStringAsync(cancellationToken);
			/*orderState.EquityWithLoanChange = */await _socket.ReadStringAsync(cancellationToken);
		}

		/*orderState.InitMarginAfter = */await _socket.ReadStringAsync(cancellationToken);
		/*orderState.MaintMarginAfter = */await _socket.ReadStringAsync(cancellationToken);
		/*orderState.EquityWithLoanAfter = */await _socket.ReadStringAsync(cancellationToken);
		Order.Commission = await _socket.ReadNullDecimalAsync(cancellationToken);
		/*orderState.MinCommission = */await _socket.ReadNullDecimalAsync(cancellationToken);
		/*orderState.MaxCommission = */await _socket.ReadNullDecimalAsync(cancellationToken);
		Order.CommissionCurrency = await _socket.ReadStringAsync(cancellationToken);
		/*orderState.WarningText = */await _socket.ReadStringAsync(cancellationToken);
	}

	public async ValueTask ReadOrderStatus(CancellationToken cancellationToken)
	{
		Order.FillStatus(await _socket.ReadOrderStatusAsync(cancellationToken));
	}

	public async ValueTask ReadVolRandomizeFlags(CancellationToken cancellationToken)
	{
		if (_msgVersion >= ServerVersions.RealTimeBars)
		{
			_condition.RandomizeSize = await _socket.ReadBoolAsync(cancellationToken);
			_condition.RandomizePrice = await _socket.ReadBoolAsync(cancellationToken);
		}
	}

	public async ValueTask ReadPegToBenchParams(CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion < ServerVersions.PeggedToBenchmark)
			return;

		if (_condition.ExtendedType == InteractiveBrokersOrderCondition.ExtendedOrderTypes.PeggedBench)
		{
			_condition.ReferenceContractId = await _socket.ReadIntAsync(cancellationToken);
			_condition.IsPeggedChangeAmountDecrease = await _socket.ReadBoolAsync(cancellationToken);
			_condition.PeggedChangeAmount = await _socket.ReadNullDecimalAsync(cancellationToken);
			_condition.ReferenceChangeAmount = await _socket.ReadNullDecimalAsync(cancellationToken);
			_condition.ReferenceExchange = await _socket.ReadStringAsync(cancellationToken);
		}
	}

	public async ValueTask ReadConditions(CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion < ServerVersions.PeggedToBenchmark)
			return;

		var count = await _socket.ReadIntAsync(cancellationToken);
		if (count <= 0)
			return;

		var conditions = new List<InteractiveBrokersOrderCondition.ExtraOrderCondition>();

		for (var i = 0; i < count; i++)
		{
			var extraConditionType = (InteractiveBrokersOrderCondition.PeggedOrderConditionTypes)await _socket.ReadIntAsync(cancellationToken);
			var extraCondition = InteractiveBrokersOrderCondition.ExtraOrderCondition.Create(extraConditionType);
			extraCondition.IsConjunctionConnection = await _socket.ReadStringAsync(cancellationToken) == "a";
			conditions.Add(extraCondition);
		}

		_condition.ExtraConditions = conditions;

		_condition.ConditionsIgnoreRth = await _socket.ReadBoolAsync(cancellationToken);
		_condition.ConditionsCancelOrder = await _socket.ReadBoolAsync(cancellationToken);

	}

	public async ValueTask ReadAdjustedOrderParams(CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion < ServerVersions.PeggedToBenchmark)
			return;

		_condition.AdjustedOrderType = await _socket.ReadStringAsync(cancellationToken);
		_condition.TriggerPrice = await _socket.ReadNullDecimalAsync(cancellationToken);
		await ReadStopPriceAndLmtPriceOffset(cancellationToken);
		_condition.AdjustedStopPrice = await _socket.ReadNullDecimalAsync(cancellationToken);
		_condition.AdjustedStopLimitPrice = await _socket.ReadNullDecimalAsync(cancellationToken);
		_condition.AdjustedTrailingAmount = await _socket.ReadNullDecimalAsync(cancellationToken);
		_condition.AdjustableTrailingUnit = await _socket.ReadIntAsync(cancellationToken);
	}

	public async ValueTask ReadStopPriceAndLmtPriceOffset(CancellationToken cancellationToken)
	{
		_condition.TrailStopPrice = await _socket.ReadNullDecimalAsync(cancellationToken);
		_condition.LimitPriceOffset = await _socket.ReadNullDecimalAsync(cancellationToken);
	}

	public async ValueTask ReadSoftDollarTier(CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion >= ServerVersions.SoftDollarTier)
		{
			_condition.Tier = new SoftDollarTier
			{
				Name = await _socket.ReadStringAsync(cancellationToken),
				Value = await _socket.ReadStringAsync(cancellationToken),
				DisplayName = await _socket.ReadStringAsync(cancellationToken)
			};
		}
	}

	public async ValueTask ReadCashQty(CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion >= ServerVersions.CashQty)
		{
			_condition.CashQty = await _socket.ReadNullDecimalAsync(cancellationToken);
		}
	}

	public async ValueTask ReadDontUseAutoPriceForHedge(CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion >= ServerVersions.AutoPriceForHedge)
		{
			_condition.DontUseAutoPriceForHedge = await _socket.ReadBoolAsync(cancellationToken);
		}
	}

	public async ValueTask ReadIsOmsContainer(CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion >= ServerVersions.OrderContainer)
		{
			_condition.IsOmsContainer = await _socket.ReadBoolAsync(cancellationToken);
		}
	}

	public async ValueTask ReadDiscretionaryUpToLimitPrice(CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion >= ServerVersions.DPegOrders)
		{
			_condition.DiscretionaryUpToLimitPrice = await _socket.ReadBoolAsync(cancellationToken);
		}
	}

	public async ValueTask ReadAutoCancelDate(CancellationToken cancellationToken)
	{
		_condition.AutoCancelDate = await _socket.ReadStringAsync(cancellationToken);
	}

	public async ValueTask ReadFilledQuantity(CancellationToken cancellationToken)
	{
		Order.Balance = Order.OrderVolume - await _socket.ReadNullDecimalAsync(cancellationToken);
	}

	public async ValueTask ReadRefFuturesConId(CancellationToken cancellationToken)
	{
		_condition.RefFuturesContractId = await _socket.ReadIntAsync(cancellationToken);
	}

	public async ValueTask ReadAutoCancelParent(CancellationToken cancellationToken)
	{
		await ReadAutoCancelParent(_socket.Adapter.MaxVersion, cancellationToken);
	}

	public async ValueTask ReadAutoCancelParent(ServerVersions minVersion, CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion >= minVersion)
		{
			_condition.AutoCancelParent = await _socket.ReadBoolAsync(cancellationToken);
		}
	}

	public async ValueTask ReadShareholder(CancellationToken cancellationToken)
	{
		_condition.Shareholder = await _socket.ReadStringAsync(cancellationToken);
	}

	public async ValueTask ReadImbalanceOnly(CancellationToken cancellationToken)
	{
		_condition.ImbalanceOnly = await _socket.ReadBoolAsync(cancellationToken);
	}

	public async ValueTask ReadRouteMarketableToBbo(CancellationToken cancellationToken)
	{
		_condition.RouteMarketableToBbo = await _socket.ReadBoolAsync(cancellationToken);
	}

	public async ValueTask ReadParentPermId(CancellationToken cancellationToken)
	{
		_condition.ParentPermId = await _socket.ReadLongAsync(cancellationToken);
	}

	public async ValueTask ReadCompletedTime(CancellationToken cancellationToken)
	{
		Order.ServerTime = await _socket.ReadDateAsync(cancellationToken);
	}

	public async ValueTask ReadCompletedStatus(CancellationToken cancellationToken)
	{
		Order.FillStatus(await _socket.ReadOrderStatusAsync(cancellationToken));
	}

	public async ValueTask ReadUsePriceMgmtAlgo(CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion >= ServerVersions.PriceMgmtAlgo)
		{
			_condition.UsePriceManagementAlgo = await _socket.ReadBoolAsync(cancellationToken);
		}
	}

	public async ValueTask ReadDuration(CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion >= ServerVersions.Duration)
		{
			_condition.Duration = await _socket.ReadNullIntAsync(cancellationToken);
		}
	}

	public async ValueTask ReadPostToAts(CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion >= ServerVersions.PostToAts)
		{
			_condition.PostToAts = await _socket.ReadNullIntAsync(cancellationToken);
		}
	}

	public async ValueTask ReadPegBestPegMidOrderAttributes(CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion >= ServerVersions.PegBestPegMinOffsets)
		{
			_condition.MinTradeQty = await _socket.ReadNullIntAsync(cancellationToken);
			_condition.MinCompeteSize = await _socket.ReadNullIntAsync(cancellationToken);
			_condition.CompeteAgainstBestOffset = await _socket.ReadNullDecimalAsync(cancellationToken);
			_condition.MidOffsetAtWhole = await _socket.ReadNullDecimalAsync(cancellationToken);
			_condition.MidOffsetAtHalf = await _socket.ReadNullDecimalAsync(cancellationToken);
		}
	}

	public async ValueTask ReadCustomerAccount(CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion >= ServerVersions.MinServerVerCustomerAccount)
		{
			_condition.CustomerAccount = await _socket.ReadStringAsync(cancellationToken);
		}
	}

	public async ValueTask ReadProfessionalCustomer(CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion >= ServerVersions.MinServerVerProfessionalCustomer)
		{
			_condition.ProfessionalCustomer = await _socket.ReadBoolAsync(cancellationToken);
		}
	}

	public async ValueTask ReadBondAccruedInterest(CancellationToken cancellationToken)
	{
		if (_socket.ServerVersion >= ServerVersions.MinServerVerBondAccruedInterest)
		{
			_condition.BondAccruedInterest = await _socket.ReadStringAsync(cancellationToken);
		}
	}
}