namespace StockSharp.Binance;

public partial class BinanceMessageAdapter
{
	private const string _isolatedPortfolioSuffix = "_isolated_";

	private string PortfolioName => nameof(Binance) + "_" + Key.ToId();

	private string GetPortfolioName(BinanceSections section, string symbol)
	{
		if(symbol.IsEmptyOrWhiteSpace())
			return PortfolioName + "_" + section;

		CheckSectionSymbol(section, symbol);

		return PortfolioName + "_" + BinanceSections.Margin + _isolatedPortfolioSuffix + symbol.ToUpperInvariant();
	}

	private static string TryGetSymbolFromIsolatedPortfolioName(string portfolioName)
	{
		var idx = portfolioName.LastIndexOf(_isolatedPortfolioSuffix, StringComparison.InvariantCulture);
		if(idx < 0)
			return null;

		return portfolioName[(idx + _isolatedPortfolioSuffix.Length)..];
	}

	private static string GetClientId(string clientId)
	{
		var idx = clientId.LastIndexOf('-');

		if (idx != -1)
			clientId = clientId[(idx + 1)..];

		return clientId;
	}

	private static string GetClientId(BinanceSections section, long transactionId)
	{
		var prefix = section switch
		{
			BinanceSections.Spot or BinanceSections.Margin => "x-QDU3JFD9-",
			BinanceSections.Futures or BinanceSections.FuturesCoin => "x-an4PdtVk-",
			_ => throw new ArgumentOutOfRangeException(nameof(section), section, LocalizedStrings.InvalidValue)
		};

		return $"{prefix}{transactionId}";
	}

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var (section, symbol) = regMsg.SecurityId.ToNative();
		var condition = (BinanceOrderCondition)regMsg.Condition;

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				if (!condition.IsWithdraw)
					break;

				var withdrawId = await _httpClient.Withdraw(symbol, regMsg.Volume, condition.WithdrawInfo, regMsg.Comment, cancellationToken);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OrderStringId = withdrawId,
					ServerTime = CurrentTime,
					OriginalTransactionId = regMsg.TransactionId,
					OrderState = OrderStates.Done,
					HasOrderInfo = true,
				}, cancellationToken);

				//ProcessPortfolioLookup(null);
				return;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		if (section == BinanceSections.Spot && regMsg.MarginMode is not null)
			section = BinanceSections.Margin;

		var isolatedSymbol = TryGetSymbolFromIsolatedPortfolioName(regMsg.PortfolioName);
		var isolated = false;

		if (!isolatedSymbol.IsEmptyOrWhiteSpace())
		{
			CheckSectionSymbol(section, isolatedSymbol);

			section = BinanceSections.Margin;

			if(!isolatedSymbol.EqualsIgnoreCase(symbol))
				throw new InvalidOperationException($"invalid symbol for isolated margin account. pf={regMsg.PortfolioName}, symbol={symbol}");

			isolated = true;
		}

		var price = regMsg.OrderType == OrderTypes.Market || regMsg.Price == 0 ? (decimal?)null : regMsg.Price;

		if (condition?.Type != BinanceOrderConditionTypes.OCO)
		{
			var type = regMsg.OrderType.ToNative(section, regMsg.Price, condition, regMsg.PostOnly, out var isTif);
			var tif = isTif ? regMsg.TimeInForce.ToNative(section, regMsg.PostOnly) : null;

			await _httpClient.RegisterOrder(section, symbol, regMsg.Side.ToNative(), type, tif, price,
				regMsg.Volume, GetClientId(section, regMsg.TransactionId), condition?.StopPrice,
				regMsg.VisibleVolume, regMsg.PositionEffect == null ? null : (regMsg.PositionEffect.Value == OrderPositionEffects.CloseOnly),
				null, condition?.Trigger?.ToNative(), null, isolated,
				cancellationToken);
		}
		else
		{
			if(section != BinanceSections.Spot && section != BinanceSections.Margin)
				throw new InvalidOperationException($"OCO is not supported on section={section}");

			if (price == null)
				throw new InvalidOperationException("price cannot be null for OCO");

			if(condition.StopPrice == null)
				throw new InvalidOperationException("stop price cannot be null for OCO");

			var tif = regMsg.TimeInForce.ToNative(section, regMsg.PostOnly);
			var limitClientId = GetClientId(section, regMsg.TransactionId);
			var stopClientId = GetClientId(section, TransactionIdGenerator.GetNextId());

			await _httpClient.RegisterOCO(
				symbol, regMsg.Side.ToNative(), price.Value, regMsg.Volume,
				condition.StopPrice.Value, condition.ClosePositionPrice,
				limitClientId, stopClientId,
				regMsg.VisibleVolume,
				section == BinanceSections.Spot ? null : isolated,
				tif,
				cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		//if (cancelMsg.OrderId == null)
		//	throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OrderTransactionId));

		var (section, symbol) = cancelMsg.SecurityId.ToNative();

		if (section == BinanceSections.Spot && cancelMsg.MarginMode is not null)
			section = BinanceSections.Margin;

		var isolatedSymbol = TryGetSymbolFromIsolatedPortfolioName(cancelMsg.PortfolioName);
		var isolated = false;

		if (!isolatedSymbol.IsEmptyOrWhiteSpace())
		{
			if (section != BinanceSections.Spot && section != BinanceSections.Margin)
				throw new InvalidOperationException($"invalid portfolio/section. pf={cancelMsg.PortfolioName}, section={section}");

			section = BinanceSections.Margin;

			if(!isolatedSymbol.EqualsIgnoreCase(symbol))
				throw new InvalidOperationException($"invalid symbol for isolated margin account. pf={cancelMsg.PortfolioName}, symbol={symbol}");

			isolated = true;
		}

		await _httpClient.CancelOrder(section, symbol, cancelMsg.OrderId, cancelMsg.OriginalTransactionId == 0 ? null : GetClientId(section, cancelMsg.OriginalTransactionId), GetClientId(section, cancelMsg.TransactionId), isolated, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (lookupMsg == null)
			throw new ArgumentNullException(nameof(lookupMsg));

		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		if (!lookupMsg.IsSubscribe)
			return;

		var time = CurrentTime;

		foreach (var section in NonMarginSections)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = GetPortfolioName(section, null),
				BoardCode = section.ToBoard(),
				OriginalTransactionId = lookupMsg.TransactionId
			}, cancellationToken);

			try
			{
				var account = await _httpClient.GetAccount(section, cancellationToken);

				await SendOutMessageAsync(new PositionChangeMessage
				{
					SecurityId = SecurityId.Money,
					PortfolioName = GetPortfolioName(section, null),
					ServerTime = time,
				}
				.TryAdd(PositionChangeTypes.Leverage, (decimal?)account.MarginLevel)
				.TryAdd(PositionChangeTypes.CommissionTaker, (decimal?)account.TakerCommissionRate)
				.TryAdd(PositionChangeTypes.CommissionMaker, (decimal?)account.MakerCommissionRate), cancellationToken);

				if (section.IsCommonFutures())
				{
					foreach (var position in account.Assets)
					{
						await SendOutMessageAsync(new PositionChangeMessage
						{
							PortfolioName = GetPortfolioName(section, null),
							SecurityId = position.Symbol.IsEmpty(position.Asset).ToStockSharp(section),
							ServerTime = time,
							OriginalTransactionId = lookupMsg.TransactionId,
						}
						.TryAdd(PositionChangeTypes.BeginValue, (decimal?)position.PositionInitialMargin, true)
						.TryAdd(PositionChangeTypes.CurrentValue, (decimal?)position.MarginBalance, true)
						.TryAdd(PositionChangeTypes.Leverage, (decimal?)position.Leverage)
						.TryAdd(PositionChangeTypes.BlockedValue, (decimal?)position.OpenOrderInitialMargin)
						.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal?)position.UnrealizedProfit, true)
						, cancellationToken);
					}

					foreach (var position in await _httpClient.GetPositions(section, cancellationToken))
					{
						await SendOutMessageAsync(new PositionChangeMessage
						{
							PortfolioName = GetPortfolioName(section, null),
							SecurityId = position.Symbol.ToStockSharp(section),
							ServerTime = time,
							OriginalTransactionId = lookupMsg.TransactionId,
							Side = position.Side.EqualsIgnoreCase("both") ? null : position.Side?.ToSide(),
						}
						.TryAdd(PositionChangeTypes.CurrentValue, (decimal)position.Amount, true)
						.TryAdd(PositionChangeTypes.AveragePrice, (decimal?)position.EntryPrice)
						.TryAdd(PositionChangeTypes.Leverage, (decimal?)position.Leverage, true)
						.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal?)position.UnrealizedProfit, true)
						.TryAdd(PositionChangeTypes.LiquidationPrice, (decimal?)position.LiquidationPrice)
						, cancellationToken);
					}
				}
				else
				{
					foreach (var balance in account.Balances ?? account.UserAssets)
					{
						//if (balance.Free == 0 && balance.Locked == 0 && balance.Borrowed == 0)
						//	continue;

						await SendOutMessageAsync(new PositionChangeMessage
						{
							PortfolioName = GetPortfolioName(section, null),
							SecurityId = balance.Asset.ToStockSharp(section),
							ServerTime = time,
							OriginalTransactionId = lookupMsg.TransactionId,
						}
						.TryAdd(PositionChangeTypes.CurrentValue, (decimal?)balance.Free, true)
						.TryAdd(PositionChangeTypes.BlockedValue, (decimal?)balance.Locked, true), cancellationToken);
					}
				}

				if (section != BinanceSections.Margin)
					continue;

				var isolatedAccounts = await _httpClient.EnsureGetIsolatedAccounts(cancellationToken);
				this.AddVerboseLog("got {0} isolated accounts", isolatedAccounts.Assets?.Length);

				if (isolatedAccounts.Assets == null)
					continue;

				foreach (var ia in isolatedAccounts.Assets)
				{
					this.AddVerboseLog("isolated account {0}, created={1}, enabled={2}, tradeEnabled={3}, level={4}, levelStatus={5}", ia.Symbol, ia.IsolatedCreated, ia.Enabled, ia.TradeEnabled, ia.MarginLevel, ia.MarginLevelStatus);

					if (ia.Enabled != true)
						continue;

					var pfName = GetPortfolioName(BinanceSections.Margin, ia.Symbol);

					if (!lookupMsg.IsHistoryOnly())
						await SubscribeAccount(BinanceSections.Margin, ia.Symbol, cancellationToken);

					await SendOutMessageAsync(new PortfolioMessage
					{
						PortfolioName = pfName,
						BoardCode = section.ToBoard(),
						OriginalTransactionId = lookupMsg.TransactionId
					}, cancellationToken);

					await SendOutMessageAsync(new PositionChangeMessage
					{
						SecurityId = SecurityId.Money,
						PortfolioName = pfName,
						ServerTime = time,
					}
					.TryAdd(PositionChangeTypes.Leverage, (decimal?)ia.MarginLevel), cancellationToken);

					async ValueTask sendBalance(Balance b)
					{
						await SendOutMessageAsync(new PositionChangeMessage
						{
							PortfolioName = pfName,
							SecurityId = b.Asset.ToStockSharp(section),
							ServerTime = time,
							OriginalTransactionId = lookupMsg.TransactionId,
						}
						.TryAdd(PositionChangeTypes.CurrentValue, (decimal?)b.Free, true)
						.TryAdd(PositionChangeTypes.BlockedValue, (decimal?)b.Locked, true), cancellationToken);
					}

					await sendBalance(ia.BaseAsset);
					await sendBalance(ia.QuoteAsset);
				}
			}
			catch (Exception ex)
			{
				if (!cancellationToken.IsCancellationRequested)
					this.AddErrorLog(ex);
			}
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (statusMsg == null)
			throw new ArgumentNullException(nameof(statusMsg));

		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		if (!statusMsg.IsSubscribe)
			return;

		foreach (var sec in NonMarginSections)
		{
			try
			{
				var res = new List<(BinanceSections section, string isolatedSymbol)> { (sec, null) };

				if (sec == BinanceSections.Margin)
				{
					var isolatedAccounts = await _httpClient.EnsureGetIsolatedAccounts(cancellationToken);

					if (isolatedAccounts.Assets is not null)
						res.AddRange(isolatedAccounts.Assets.Select(a => (section: BinanceSections.Margin, isolatedSymbol: a.Symbol)));
				}

				foreach (var (section, isolatedSymbol) in res)
				{
					var orders = await _httpClient.GetRecentOrders(section, isolatedSymbol, null, cancellationToken);

					foreach (var order in orders)
					{
						if (!long.TryParse(GetClientId(order.ClientId), out var transId))
							transId = TransactionIdGenerator.GetNextId();

						var orderType = order.Type.ToOrderType(out var postOnly, out var condition);

						if (condition != null && order.StopPrice > 0)
							condition.StopPrice = (decimal)order.StopPrice;

						var orderState = order.Status.ToOrderState();

						await SendOutMessageAsync(new ExecutionMessage
						{
							DataTypeEx = DataType.Transactions,
							HasOrderInfo = true,
							SecurityId = order.Symbol.ToStockSharp(section),
							ServerTime = order.Time,
							PortfolioName = GetPortfolioName(section, isolatedSymbol),
							Side = order.Side.ToSide(),
							OrderVolume = (decimal)order.OrigQuantity,
							VisibleVolume = order.IcebergQuantity > 0 ? (decimal)order.IcebergQuantity : null,
							Balance = order.GetBalance(),
							OrderPrice = (decimal)order.Price,
							OrderType = orderType,
							OrderState = orderState,
							Condition = condition,
							TimeInForce = order.TimeInForce.ToTimeInForce(out var postOnly2),
							PostOnly = postOnly ?? postOnly2,
							OrderId = order.Id > 0 ? order.Id : null,
							TransactionId = transId,
							OriginalTransactionId = statusMsg.TransactionId,
							MarginMode = section == BinanceSections.Margin ? MarginModes.Isolated : null,
						}, cancellationToken);
					}

					var myTrades = await _httpClient.GetRecentTrades(section, null, cancellationToken);

					foreach (var mt in myTrades)
					{
						await SendOutMessageAsync(new ExecutionMessage
						{
							SecurityId = mt.Symbol.ToStockSharp(section),
							DataTypeEx = DataType.Transactions,
							OrderId = mt.OrderId,
							ServerTime = mt.Time,
							TradeId = mt.TradeId,
							TradePrice = mt.Price,
							TradeVolume = mt.Qty,
							Commission = mt.Commission,
							CommissionCurrency = mt.CommissionAsset
						}, cancellationToken);
					}
				}
			}
			catch (Exception ex)
			{
				if (!cancellationToken.IsCancellationRequested)
					this.AddErrorLog(ex);
			}
		}

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private ValueTask SessionOnNewExecutionReport(BinanceSections section, string isolatedSymbol, ExecutionReport report, CancellationToken cancellationToken)
	{
		var otype = report.OriginalOrderType?.IsStopOrderType() == true ? report.OriginalOrderType : report.OrderType;
		var orderType = otype.ToOrderType(out var postOnly, out var condition);

		// when stop is triggered, binance sends exec report with EXPIRED state (canceled order), but then immediately sends exec report with NEW state but with same orderid/clientorderid, so it's the same order
		if(report.IsStopTriggered())
		{
			this.AddVerboseLog("detected stop trigger, ignoring");
			return default;
		}

		if (condition == null && !report.Trigger.IsEmpty())
			condition = new BinanceOrderCondition();

		if (condition != null)
		{
			var stopPrice = report.FutStopPrice ?? report.StopPrice;

			if (stopPrice != null)
				condition.StopPrice = (decimal)stopPrice.Value;

			condition.Trigger = report.Trigger.ToTrigger();
		}

		long transId = 0;

		if (!long.TryParse(GetClientId(report.ClientId), out var originTransId))
		{
			transId = TransactionIdGenerator.GetNextId();
		}

		var orderState = report.OrderStatus.ToOrderState();
		var hasTrade = report.TradeId > 0;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = report.Symbol.ToStockSharp(section),
			ServerTime = report.TransactionTime ?? report.EventTime,
			PortfolioName = GetPortfolioName(section, isolatedSymbol),
			Side = report.Side.ToSide(),
			OrderVolume = (decimal)report.Quantity,
			VisibleVolume = report.IcebergQuantity > 0 ? (decimal)report.IcebergQuantity : null,
			Balance = (decimal)(report.Quantity - report.CumulativeQuantity),
			OrderPrice = (decimal?)report.Price ?? 0M,
			OrderType = orderType,
			OrderState = orderState,
			Error = orderState == OrderStates.Failed ? new InvalidOperationException(report.RejectReason) : null,
			Condition = condition,
			TimeInForce = report.TimeInForce.ToTimeInForce(out var postOnly2),
			PostOnly = postOnly ?? postOnly2,
			OrderId = report.OrderId > 0 ? report.OrderId : null,
			TransactionId = transId,
			OriginalTransactionId = originTransId,

			TradeId = hasTrade ? report.TradeId : null,
			TradePrice = hasTrade ? (decimal?)report.LastPrice : null,
			TradeVolume = hasTrade ? (decimal?)report.LastQuantity : null,

			Commission = (decimal?)report.Commission,
			CommissionCurrency = report.CommissionAsset,

			AveragePrice = (decimal?)report.AveragePrice,

			PositionEffect = report.IsReduceOnly == null ? null : (report.IsReduceOnly.Value ? OrderPositionEffects.CloseOnly : OrderPositionEffects.Default),
		}, cancellationToken);
	}

	private async ValueTask SessionOnAccountUpdated(BinanceSections section, string isolatedSymbol, AccountUpdate account, CancellationToken cancellationToken)
	{
		var time = account.LastAccountUpdate ?? account.EventTime;
		var futData = account.FuturesData;

		if (account.Balances != null)
		{
			foreach (var balance in account.Balances)
			{
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = GetPortfolioName(section, isolatedSymbol),
					SecurityId = balance.Asset.ToStockSharp(section),
					ServerTime = time,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, (decimal)balance.Free, true)
				.TryAdd(PositionChangeTypes.BlockedValue, (decimal)balance.Locked, true), cancellationToken);
			}
		}
		else if (futData != null)
		{
			foreach (var balance in futData.Balances)
			{
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = GetPortfolioName(section, isolatedSymbol),
					SecurityId = balance.Asset.ToStockSharp(section),
					ServerTime = time,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, (decimal)balance.Balance, true), cancellationToken);
			}

			foreach (var position in futData.Positions)
			{
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = GetPortfolioName(section, isolatedSymbol),
					SecurityId = position.Symbol.ToStockSharp(section),
					ServerTime = time,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, (decimal)position.Amount, true)
				.TryAdd(PositionChangeTypes.AveragePrice, (decimal?)position.EntryPrice, true)
				.TryAdd(PositionChangeTypes.RealizedPnL, (decimal?)position.RealizedPnL, true)
				.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal?)position.UnrealizedPnL, true)
				, cancellationToken);
			}
		}
	}

	internal static void CheckSectionSymbol(BinanceSections section, string isolatedSymbol)
	{
		if(section != BinanceSections.Margin && !isolatedSymbol.IsEmptyOrWhiteSpace())
			throw new ArgumentException($"isolatedSymbol is not empty ({isolatedSymbol}) for section={section}");
	}
}
