namespace StockSharp.Jupiter;

public partial class JupiterMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"Post-only is not supported by Jupiter swap and Perps APIs.");
		if (regMsg.TimeInForce is not null)
			throw new NotSupportedException(
				"Jupiter does not expose a configurable time in force.");
		if (!regMsg.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"Jupiter order identity is an on-chain public key or " +
				"transaction signature.");
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"Jupiter order volume must be positive.");

		if (market.Kind == JupiterMarketKinds.Spot)
			await RegisterSpotOrderAsync(regMsg, market, volume,
				cancellationToken);
		else
			await RegisterPerpetualOrderAsync(regMsg, market, volume,
				cancellationToken);
	}

	private async ValueTask RegisterSpotOrderAsync(OrderRegisterMessage message,
		JupiterMarket market, decimal volume,
		CancellationToken cancellationToken)
	{
		if (message.OrderType is not (null or OrderTypes.Market))
			throw new NotSupportedException(
				"Jupiter spot swaps are immediate market orders.");
		if (message.Condition is not null)
			throw new NotSupportedException(
				"Jupiter spot swaps do not use an order condition.");
		var baseUnits = volume.ToRawAmount(market.BaseToken,
			DateTime.UtcNow).ToString(CultureInfo.InvariantCulture);
		var isSell = message.Side == Sides.Sell;
		var order = await ApiClient.GetSwapOrderAsync(new()
		{
			InputMint = isSell
				? market.BaseToken.Mint
				: market.QuoteToken.Mint,
			OutputMint = isSell
				? market.QuoteToken.Mint
				: market.BaseToken.Mint,
			Amount = baseUnits,
			SwapMode = isSell
				? JupiterSwapModes.ExactInput
				: JupiterSwapModes.ExactOutput,
			Taker = Signer.WalletAddress,
		}, cancellationToken);
		ValidateSwapQuote(order, market, isSell
			? JupiterSwapModes.ExactInput
			: JupiterSwapModes.ExactOutput);
		if (order.Transaction.IsEmpty() || order.RequestId.IsEmpty())
			throw new InvalidDataException(
				"Jupiter returned no executable swap transaction.");
		if (!order.Taker.IsEmpty() && order.Taker != Signer.WalletAddress)
			throw new InvalidDataException(
				"Jupiter swap transaction belongs to another wallet.");

		var signed = Signer.SignTransaction(order.Transaction);
		var result = await ApiClient.ExecuteSwapAsync(new()
		{
			RequestId = order.RequestId,
			SignedTransaction = signed,
		}, cancellationToken);
		if (result.Status == JupiterExecutionStatuses.Failed ||
			result.Code != 0 || !result.Error.IsEmpty() ||
			result.Signature.IsEmpty())
			throw new JupiterApiException(result.Error ??
				$"Jupiter swap execution failed with code {result.Code}.");
		var signature = result.Signature.NormalizeSignature();
		var executedBase = (isSell
			? result.InputAmount
			: result.OutputAmount).FromRawAmount(market.BaseToken,
				DateTime.UtcNow);
		var executedQuote = (isSell
			? result.OutputAmount
			: result.InputAmount).FromRawAmount(market.QuoteToken,
				DateTime.UtcNow);
		if (executedBase <= 0 || executedQuote <= 0)
			throw new InvalidDataException(
				"Jupiter returned non-positive executed swap amounts.");
		var commission = GetSpotCommission(order, result, market);
		var tracked = new TrackedOrder
		{
			TransactionId = message.TransactionId,
			Market = market,
			Kind = JupiterTrackedOrderKinds.SpotSwap,
			OrderId = signature,
			Signature = signature,
			Side = message.Side,
			Volume = executedBase,
			Price = executedQuote / executedBase,
			SubmittedTime = CurrentTime,
			State = OrderStates.Done,
			IsTradeSent = true,
			Commission = commission.Amount,
			CommissionCurrency = commission.Currency,
		};
		RememberOrder(tracked);
		await SendTrackedOrderAsync(tracked, message.TransactionId,
			cancellationToken);
		await SendTrackedTradeAsync(tracked, message.TransactionId,
			cancellationToken);
	}

	private static (decimal? Amount, string Currency) GetSpotCommission(
		JupiterSwapOrder order, JupiterSpotExecuteResponse result,
		JupiterMarket market)
	{
		if (order.FeeMint.IsEmpty())
			return (null, null);
		string gross;
		string net;
		JupiterToken token;
		if (order.FeeMint == order.InputMint)
		{
			gross = result.TotalInputAmount;
			net = result.InputAmount;
			token = order.InputMint == market.BaseToken.Mint
				? market.BaseToken
				: market.QuoteToken;
		}
		else if (order.FeeMint == order.OutputMint)
		{
			gross = result.OutputAmount;
			net = result.TotalOutputAmount;
			token = order.OutputMint == market.BaseToken.Mint
				? market.BaseToken
				: market.QuoteToken;
		}
		else
			return (null, null);
		if (!BigInteger.TryParse(gross, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var grossAmount) ||
			!BigInteger.TryParse(net, NumberStyles.Integer,
				CultureInfo.InvariantCulture, out var netAmount))
			throw new InvalidDataException(
				"Jupiter returned invalid swap fee amounts.");
		var fee = BigInteger.Abs(grossAmount - netAmount).ToString(
			CultureInfo.InvariantCulture).FromRawAmount(token, DateTime.UtcNow);
		return fee > 0 ? (fee, token.Symbol) : (null, null);
	}

	private async ValueTask RegisterPerpetualOrderAsync(
		OrderRegisterMessage message, JupiterMarket market, decimal volume,
		CancellationToken cancellationToken)
	{
		var condition = message.Condition as JupiterOrderCondition ?? new();
		if (condition.Leverage is < 1 or > 100)
			throw new InvalidOperationException(
				"Jupiter Perps leverage must be between one and 100.");
		if (market.PerpetualAsset is not JupiterPerpetualAssets asset)
			throw new InvalidDataException(
				"Jupiter perpetual market has no native asset mapping.");
		var orderType = message.OrderType ?? OrderTypes.Market;
		if (orderType is not (OrderTypes.Market or OrderTypes.Limit or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));

		switch (condition.Action)
		{
			case JupiterOrderActions.Open:
				await OpenPerpetualPositionAsync(message, market, asset,
					volume, orderType, condition, cancellationToken);
				break;
			case JupiterOrderActions.Close:
				await ClosePerpetualPositionAsync(message, market, volume,
					orderType, condition, cancellationToken);
				break;
			case JupiterOrderActions.TakeProfit:
			case JupiterOrderActions.StopLoss:
				await CreatePerpetualTriggerAsync(message, market, volume,
					orderType, condition, cancellationToken);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(condition.Action),
					condition.Action, null);
		}
	}

	private async ValueTask OpenPerpetualPositionAsync(
		OrderRegisterMessage message, JupiterMarket market,
		JupiterPerpetualAssets asset, decimal volume, OrderTypes orderType,
		JupiterOrderCondition condition,
		CancellationToken cancellationToken)
	{
		if (orderType == OrderTypes.Conditional)
			throw new InvalidOperationException(
				"Use the take-profit or stop-loss Jupiter action for a " +
				"conditional Perps request.");
		if (orderType == OrderTypes.Limit && message.Price <= 0)
			throw new InvalidOperationException(
				"Jupiter Perps limit price must be positive.");
		if (orderType == OrderTypes.Limit &&
			(condition.TakeProfitPrice is not null ||
				condition.StopLossPrice is not null))
			throw new InvalidOperationException(
				"Jupiter Perps limit orders cannot attach TP/SL requests.");
		ValidateTriggerPrice(condition.TakeProfitPrice, "take-profit");
		ValidateTriggerPrice(condition.StopLossPrice, "stop-loss");
		var referencePrice = orderType == OrderTypes.Limit
			? message.Price
			: await GetPerpetualMarkPriceAsync(market, cancellationToken);
		var sizeUsd = volume * referencePrice;
		var inputAmount = await CalculateCollateralAmountAsync(
			condition.CollateralToken, sizeUsd, condition.Leverage,
			cancellationToken);
		var side = message.Side.ToJupiter();
		var slippage = GetPerpetualSlippageBasisPoints().ToString(
			CultureInfo.InvariantCulture);

		if (orderType == OrderTypes.Limit)
		{
			var response = await ApiClient.CreateLimitOrderAsync(new()
			{
				Asset = asset,
				InputToken = condition.CollateralToken,
				InputTokenAmount = inputAmount,
				Side = side,
				TriggerPrice = message.Price.ToMicroUsd(),
				SizeUsd = sizeUsd.ToMicroUsd(),
				WalletAddress = Signer.WalletAddress,
			}, cancellationToken);
			if (response.Transaction.IsEmpty() || response.RequestId.IsEmpty())
				throw new InvalidDataException(
					"Jupiter Perps returned an incomplete limit order.");
			var signature = await SignAndExecutePerpetualAsync(
				JupiterPerpetualTransactionActions.CreateLimitOrder,
				response.Transaction, cancellationToken);
			var tracked = new TrackedOrder
			{
				TransactionId = message.TransactionId,
				Market = market,
				Kind = JupiterTrackedOrderKinds.PerpetualLimit,
				OrderId = response.RequestId.NormalizePublicKey(),
				Signature = signature,
				PositionId = response.PositionId,
				Side = message.Side,
				Volume = GetPerpetualVolume(response.Quote, volume,
					message.Price),
				Price = message.Price,
				SubmittedTime = CurrentTime,
				State = OrderStates.Active,
				Commission = GetPerpetualCommission(response.Quote),
				CommissionCurrency = "USD",
			};
			RememberOrder(tracked);
			await SendTrackedOrderAsync(tracked, message.TransactionId,
				cancellationToken);
			return;
		}

		var attached = new List<JupiterPerpetualAttachedRequest>();
		if (condition.TakeProfitPrice is decimal takeProfit)
			attached.Add(new()
			{
				ReceiveToken = condition.ReceiveToken,
				TriggerPrice = takeProfit.ToMicroUsd(),
				Type = JupiterPerpetualRequestTypes.TakeProfit,
			});
		if (condition.StopLossPrice is decimal stopLoss)
			attached.Add(new()
			{
				ReceiveToken = condition.ReceiveToken,
				TriggerPrice = stopLoss.ToMicroUsd(),
				Type = JupiterPerpetualRequestTypes.StopLoss,
			});
		var increase = await ApiClient.CreatePositionAsync(new()
		{
			Asset = asset,
			InputToken = condition.CollateralToken,
			InputTokenAmount = inputAmount,
			Side = side,
			MaximumSlippageBasisPoints = slippage,
			SizeUsd = sizeUsd.ToMicroUsd(),
			WalletAddress = Signer.WalletAddress,
			TakeProfitStopLoss = attached.Count == 0 ? null : [.. attached],
		}, cancellationToken);
		if (increase.Transaction.IsEmpty())
			throw new InvalidDataException(
				"Jupiter Perps returned no position transaction.");
		var transactionSignature = await SignAndExecutePerpetualAsync(
			JupiterPerpetualTransactionActions.IncreasePosition,
			increase.Transaction, cancellationToken);
		var price = GetPerpetualPrice(increase.Quote, referencePrice);
		var marketOrder = new TrackedOrder
		{
			TransactionId = message.TransactionId,
			Market = market,
			Kind = JupiterTrackedOrderKinds.PerpetualMarket,
			OrderId = transactionSignature,
			Signature = transactionSignature,
			PositionId = increase.PositionId,
			Side = message.Side,
			Volume = GetPerpetualVolume(increase.Quote, volume, price),
			Price = price,
			SubmittedTime = CurrentTime,
			State = OrderStates.Done,
			IsTradeSent = true,
			Commission = GetPerpetualCommission(increase.Quote),
			CommissionCurrency = "USD",
		};
		RememberOrder(marketOrder);
		await SendTrackedOrderAsync(marketOrder, message.TransactionId,
			cancellationToken);
		await SendTrackedTradeAsync(marketOrder, message.TransactionId,
			cancellationToken);
	}

	private async ValueTask ClosePerpetualPositionAsync(
		OrderRegisterMessage message, JupiterMarket market, decimal volume,
		OrderTypes orderType, JupiterOrderCondition condition,
		CancellationToken cancellationToken)
	{
		if (orderType != OrderTypes.Market)
			throw new InvalidOperationException(
				"Jupiter Perps position reductions are market operations.");
		var positionId = condition.PositionId.NormalizePublicKey();
		var price = await GetPerpetualMarkPriceAsync(market,
			cancellationToken);
		var response = await ApiClient.DecreasePositionAsync(new()
		{
			PositionId = positionId,
			ReceiveToken = condition.ReceiveToken,
			SizeUsd = condition.IsEntirePosition
				? null
				: (volume * price).ToMicroUsd(),
			IsEntirePosition = condition.IsEntirePosition ? true : null,
			MaximumSlippageBasisPoints =
				GetPerpetualSlippageBasisPoints().ToString(
					CultureInfo.InvariantCulture),
		}, cancellationToken);
		if (response.Transaction.IsEmpty())
			throw new InvalidDataException(
				"Jupiter Perps returned no position-reduction transaction.");
		var signature = await SignAndExecutePerpetualAsync(
			JupiterPerpetualTransactionActions.DecreasePosition,
			response.Transaction, cancellationToken);
		var executionPrice = GetPerpetualPrice(response.Quote, price);
		var tracked = new TrackedOrder
		{
			TransactionId = message.TransactionId,
			Market = market,
			Kind = JupiterTrackedOrderKinds.PerpetualClose,
			OrderId = signature,
			Signature = signature,
			PositionId = response.PositionId ?? positionId,
			Side = message.Side,
			Volume = GetPerpetualVolume(response.Quote, volume,
				executionPrice),
			Price = executionPrice,
			SubmittedTime = CurrentTime,
			State = OrderStates.Done,
			IsTradeSent = true,
			Commission = GetPerpetualCommission(response.Quote),
			CommissionCurrency = "USD",
		};
		RememberOrder(tracked);
		await SendTrackedOrderAsync(tracked, message.TransactionId,
			cancellationToken);
		await SendTrackedTradeAsync(tracked, message.TransactionId,
			cancellationToken);
	}

	private async ValueTask CreatePerpetualTriggerAsync(
		OrderRegisterMessage message, JupiterMarket market, decimal volume,
		OrderTypes orderType, JupiterOrderCondition condition,
		CancellationToken cancellationToken)
	{
		if (orderType is not (OrderTypes.Limit or OrderTypes.Conditional))
			throw new InvalidOperationException(
				"Jupiter TP/SL requests require a limit or conditional " +
				"order type.");
		if (message.Price <= 0)
			throw new InvalidOperationException(
				"Jupiter TP/SL trigger price must be positive.");
		var positionId = condition.PositionId.NormalizePublicKey();
		var requestType = condition.Action == JupiterOrderActions.TakeProfit
			? JupiterPerpetualRequestTypes.TakeProfit
			: JupiterPerpetualRequestTypes.StopLoss;
		var response = await ApiClient.CreateTriggerAsync(new()
		{
			WalletAddress = Signer.WalletAddress,
			PositionId = positionId,
			Requests =
			[
				new()
				{
					ReceiveToken = condition.ReceiveToken,
					TriggerPrice = message.Price.ToMicroUsd(),
					Type = requestType,
					IsEntirePosition = condition.IsEntirePosition,
					SizeUsd = condition.IsEntirePosition
						? null
						: (volume * message.Price).ToMicroUsd(),
				},
			],
		}, cancellationToken);
		var requestId = response.RequestIds?.FirstOrDefault() ??
			response.Requests?.FirstOrDefault()?.RequestId;
		if (response.Transaction.IsEmpty() || requestId.IsEmpty())
			throw new InvalidDataException(
				"Jupiter Perps returned an incomplete TP/SL request.");
		var signature = await SignAndExecutePerpetualAsync(
			JupiterPerpetualTransactionActions.CreateTakeProfitStopLoss,
			response.Transaction, cancellationToken);
		var tracked = new TrackedOrder
		{
			TransactionId = message.TransactionId,
			Market = market,
			Kind = requestType == JupiterPerpetualRequestTypes.TakeProfit
				? JupiterTrackedOrderKinds.TakeProfit
				: JupiterTrackedOrderKinds.StopLoss,
			OrderId = requestId.NormalizePublicKey(),
			Signature = signature,
			PositionId = positionId,
			Side = message.Side,
			Volume = volume,
			Price = message.Price,
			SubmittedTime = CurrentTime,
			State = OrderStates.Active,
		};
		RememberOrder(tracked);
		await SendTrackedOrderAsync(tracked, message.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(replaceMsg.PortfolioName);
		var orderId = NormalizeOrderIdentity(replaceMsg.OldOrderStringId);
		if (replaceMsg.Price <= 0)
			throw new InvalidOperationException(
				"Jupiter replacement trigger price must be positive.");
		var tracked = await ResolveTrackedOrderAsync(orderId,
			cancellationToken);
		if (tracked.State != OrderStates.Active)
			throw new InvalidOperationException(
				$"Jupiter order '{orderId}' is no longer active.");
		string transaction;
		JupiterPerpetualTransactionActions action;
		if (tracked.Kind == JupiterTrackedOrderKinds.PerpetualLimit)
		{
			var response = await ApiClient.UpdateLimitOrderAsync(new()
			{
				RequestId = orderId,
				TriggerPrice = replaceMsg.Price.ToMicroUsd(),
			}, cancellationToken);
			transaction = response.Transaction;
			action = JupiterPerpetualTransactionActions.UpdateLimitOrder;
		}
		else if (tracked.Kind is JupiterTrackedOrderKinds.TakeProfit or
			JupiterTrackedOrderKinds.StopLoss)
		{
			var response = await ApiClient.UpdateTriggerAsync(new()
			{
				RequestId = orderId,
				TriggerPrice = replaceMsg.Price.ToMicroUsd(),
			}, cancellationToken);
			transaction = response.Transaction;
			action =
				JupiterPerpetualTransactionActions.UpdateTakeProfitStopLoss;
		}
		else
			throw new NotSupportedException(
				"Only Jupiter Perps limit and TP/SL requests can be replaced.");
		if (transaction.IsEmpty())
			throw new InvalidDataException(
				"Jupiter returned no replacement transaction.");
		tracked.Signature = await SignAndExecutePerpetualAsync(action,
			transaction, cancellationToken);
		tracked.Price = replaceMsg.Price;
		await SendTrackedOrderAsync(tracked, replaceMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var orderId = NormalizeOrderIdentity(cancelMsg.OrderStringId);
		var tracked = await ResolveTrackedOrderAsync(orderId,
			cancellationToken);
		await CancelTrackedOrderAsync(tracked, cancelMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Jupiter bulk cancellation does not close Perps positions.");
		await RefreshRemoteOrdersAsync(cancellationToken);
		TrackedOrder[] orders;
		using (_sync.EnterScope())
			orders = [.. _trackedOrders.Values.Where(order =>
				order.State == OrderStates.Active &&
				(order.Kind == JupiterTrackedOrderKinds.PerpetualLimit ||
					order.Kind == JupiterTrackedOrderKinds.TakeProfit ||
					order.Kind == JupiterTrackedOrderKinds.StopLoss) &&
				(cancelMsg.SecurityId.SecurityCode.IsEmpty() ||
					cancelMsg.SecurityId.SecurityCode.EqualsIgnoreCase(
						order.Market.SecurityCode)) &&
				(cancelMsg.Side is null || cancelMsg.Side == order.Side))];
		foreach (var order in orders)
			await CancelTrackedOrderAsync(order, cancelMsg.TransactionId,
				cancellationToken);
	}

	private async ValueTask CancelTrackedOrderAsync(TrackedOrder order,
		long target, CancellationToken cancellationToken)
	{
		if (order.State != OrderStates.Active)
			throw new InvalidOperationException(
				$"Jupiter order '{order.OrderId}' is not active.");
		JupiterPerpetualCancelResponse response;
		JupiterPerpetualTransactionActions action;
		if (order.Kind == JupiterTrackedOrderKinds.PerpetualLimit)
		{
			response = await ApiClient.CancelLimitOrderAsync(new()
			{
				RequestId = order.OrderId,
			}, cancellationToken);
			action = JupiterPerpetualTransactionActions.CancelLimitOrder;
		}
		else if (order.Kind is JupiterTrackedOrderKinds.TakeProfit or
			JupiterTrackedOrderKinds.StopLoss)
		{
			response = await ApiClient.CancelTriggerAsync(new()
			{
				RequestId = order.OrderId,
			}, cancellationToken);
			action =
				JupiterPerpetualTransactionActions.CancelTakeProfitStopLoss;
		}
		else
			throw new NotSupportedException(
				"A completed Jupiter transaction cannot be cancelled.");
		if (response.Transaction.IsEmpty())
			throw new InvalidDataException(
				"Jupiter returned no cancellation transaction.");
		order.Signature = await SignAndExecutePerpetualAsync(action,
			response.Transaction, cancellationToken);
		order.State = OrderStates.Done;
		await SendTrackedOrderAsync(order, target, cancellationToken);
	}

	private async ValueTask<string> SignAndExecutePerpetualAsync(
		JupiterPerpetualTransactionActions action, string transaction,
		CancellationToken cancellationToken)
	{
		var signed = Signer.SignTransaction(transaction);
		var result = await ApiClient.ExecutePerpetualTransactionAsync(new()
		{
			Action = action,
			Transaction = signed,
		}, cancellationToken);
		if (!result.Error.IsEmpty() || result.TransactionId.IsEmpty())
			throw new JupiterApiException(result.Error ??
				"Jupiter Perps did not return a transaction signature.");
		return result.TransactionId.NormalizeSignature();
	}

	private async ValueTask<decimal> CalculateCollateralPriceAsync(
		JupiterCollateralTokens token, CancellationToken cancellationToken)
	{
		if (token == JupiterCollateralTokens.Usdc)
			return 1m;
		var stats = await ApiClient.GetPerpetualStatsAsync(token.GetMint(),
			cancellationToken);
		var price = JupiterExtensions.ParseDecimal(stats.Price,
			$"{token.GetSymbol()} collateral price");
		if (price <= 0)
			throw new InvalidDataException(
				"Jupiter returned a non-positive collateral price.");
		return price;
	}

	private async ValueTask<string> CalculateCollateralAmountAsync(
		JupiterCollateralTokens collateralToken, decimal sizeUsd,
		decimal leverage, CancellationToken cancellationToken)
	{
		var collateralPrice = await CalculateCollateralPriceAsync(
			collateralToken, cancellationToken);
		var amount = sizeUsd / leverage / collateralPrice;
		var token = GetToken(collateralToken.GetMint());
		return amount.ToRawAmount(token, DateTime.UtcNow).ToString(
			CultureInfo.InvariantCulture);
	}

	private async ValueTask<decimal> GetPerpetualMarkPriceAsync(
		JupiterMarket market, CancellationToken cancellationToken)
	{
		var stats = await ApiClient.GetPerpetualStatsAsync(
			market.BaseToken.Mint, cancellationToken);
		var price = JupiterExtensions.ParseDecimal(stats.Price,
			"perpetual mark price");
		if (price <= 0)
			throw new InvalidDataException(
				"Jupiter returned a non-positive perpetual mark price.");
		using (_sync.EnterScope())
			market.LastPrice = price;
		return price;
	}

	private int GetPerpetualSlippageBasisPoints()
		=> decimal.Round(PerpetualSlippageTolerance * 100m, 0,
			MidpointRounding.AwayFromZero).To<int>();

	private static void ValidateTriggerPrice(decimal? price, string name)
	{
		if (price is <= 0)
			throw new InvalidOperationException(
				$"Jupiter {name} price must be positive.");
	}

	private static decimal GetPerpetualPrice(JupiterPerpetualQuote quote,
		decimal fallback)
	{
		if (quote is not null && !quote.AveragePriceUsd.IsEmpty())
			return quote.AveragePriceUsd.FromMicroUsd("average price");
		return fallback;
	}

	private static decimal GetPerpetualVolume(JupiterPerpetualQuote quote,
		decimal fallback, decimal price)
	{
		if (quote?.SizeUsd.IsEmpty() != false || price <= 0)
			return fallback;
		return quote.SizeUsd.FromMicroUsd("position size") / price;
	}

	private static decimal? GetPerpetualCommission(
		JupiterPerpetualQuote quote)
	{
		var value = quote?.TotalFeeUsd.IsEmpty() == false
			? quote.TotalFeeUsd
			: quote?.OpenFeeUsd;
		return value.IsEmpty() ? null : value.FromMicroUsd("Perps fee");
	}

	private void RememberOrder(TrackedOrder order)
	{
		if (order.OrderId.IsEmpty())
			throw new InvalidDataException(
				"A tracked Jupiter order must have an identifier.");
		using (_sync.EnterScope())
			_trackedOrders[order.OrderId] = order;
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!lookupMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
			{
				_portfolioSubscriptions.Remove(
					lookupMsg.OriginalTransactionId);
				RemoveFingerprintPrefix(_balanceFingerprints,
					lookupMsg.OriginalTransactionId);
				RemoveFingerprintPrefix(_positionFingerprints,
					lookupMsg.OriginalTransactionId);
			}
			return;
		}
		ValidatePortfolio(lookupMsg.PortfolioName);
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = GetPortfolioName(),
			BoardCode = BoardCodes.Jupiter,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		var snapshot = await LoadPrivateSnapshotAsync(cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, true,
			snapshot, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_portfolioSubscriptions.Add(lookupMsg.TransactionId);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!statusMsg.IsSubscribe)
		{
			RemoveOrderSubscription(statusMsg.OriginalTransactionId);
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await CompleteOrderStatusAsync(statusMsg, cancellationToken);
			return;
		}
		ValidatePortfolio(statusMsg.PortfolioName);
		if (statusMsg.OrderId is not null)
			throw new NotSupportedException(
				"Jupiter orders use public keys and transaction signatures, " +
				"not numeric identifiers.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"Jupiter has no exchange-side user identifier.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for Jupiter order status.");
		var orderId = statusMsg.OrderStringId.IsEmpty()
			? null
			: NormalizeOrderIdentity(statusMsg.OrderStringId);
		var subscription = new OrderSubscription
		{
			OrderId = orderId,
			SecurityId = statusMsg.SecurityId,
			Side = statusMsg.Side,
			States = statusMsg.States,
			From = statusMsg.From?.ToUniversalTime(),
			To = statusMsg.To?.ToUniversalTime(),
			Skip = Math.Max(0, statusMsg.Skip ?? 0).Min(int.MaxValue).To<int>(),
			Maximum = (statusMsg.Count ?? HistoryLimit).Min(10000).Max(1)
				.To<int>(),
		};
		if (Signer.IsWalletAvailable)
		{
			await RefreshRemoteOrdersAsync(cancellationToken);
			await LoadPrivateTradeHistoryAsync(subscription,
				cancellationToken);
		}
		await SendOrderSnapshotAsync(subscription, statusMsg.TransactionId,
			true, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await CompleteOrderStatusAsync(statusMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_orderSubscriptions[statusMsg.TransactionId] = subscription;
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask PollPrivateAsync(
		CancellationToken cancellationToken)
	{
		long[] portfolioTargets;
		KeyValuePair<long, OrderSubscription>[] orderTargets;
		bool hasActiveOrders;
		using (_sync.EnterScope())
		{
			portfolioTargets = [.. _portfolioSubscriptions];
			orderTargets = [.. _orderSubscriptions];
			hasActiveOrders = _trackedOrders.Values.Any(static order =>
				order.State == OrderStates.Active);
		}
		if (portfolioTargets.Length > 0)
		{
			var snapshot = await LoadPrivateSnapshotAsync(cancellationToken);
			foreach (var target in portfolioTargets)
				await SendPortfolioSnapshotAsync(target, false, snapshot,
					cancellationToken);
		}
		if (orderTargets.Length > 0 || hasActiveOrders)
			await RefreshRemoteOrdersAsync(cancellationToken);
		foreach (var target in orderTargets)
		{
			await LoadPrivateTradeHistoryAsync(target.Value,
				cancellationToken);
			await SendOrderSnapshotAsync(target.Value, target.Key, false,
				cancellationToken);
		}
	}

	private async ValueTask<PrivateSnapshot> LoadPrivateSnapshotAsync(
		CancellationToken cancellationToken)
	{
		var holdings = await ApiClient.GetHoldingsAsync(Signer.WalletAddress,
			cancellationToken);
		var positions = IsPerpetualsEnabled
			? (await ApiClient.GetPositionsAsync(Signer.WalletAddress,
				cancellationToken)).Positions ?? []
			: [];
		return new()
		{
			Holdings = holdings,
			Positions = positions,
		};
	}

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, PrivateSnapshot snapshot,
		CancellationToken cancellationToken)
	{
		foreach (var balance in await BuildBalancesAsync(snapshot.Holdings,
			cancellationToken))
		{
			var fingerprint = new BalanceFingerprint(balance.Current,
				balance.Blocked);
			var key = $"{target}:balance:{balance.Identity}";
			using (_sync.EnterScope())
			{
				if (!isForced && _balanceFingerprints.TryGetValue(key,
					out var previous) && previous == fingerprint)
					continue;
				_balanceFingerprints[key] = fingerprint;
			}
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = new()
				{
					SecurityCode = balance.Code,
					BoardCode = BoardCodes.Jupiter,
				},
				ServerTime = CurrentTime,
				OriginalTransactionId = target,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, balance.Current, true)
			.TryAdd(PositionChangeTypes.BlockedValue, balance.Blocked, true),
				cancellationToken);
		}
		foreach (var position in snapshot.Positions ?? [])
			await SendPerpetualPositionAsync(position, target, isForced,
				cancellationToken);
	}

	private async ValueTask<TokenBalance[]> BuildBalancesAsync(
		JupiterHoldingsResponse holdings,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(holdings);
		var holdingTokens = holdings.Tokens ?? [];
		var unknownMints = holdingTokens.Select(static item => item.Mint)
			.Where(mint =>
			{
				using (_sync.EnterScope())
					return !_tokens.ContainsKey(mint);
			}).Distinct(StringComparer.Ordinal).ToArray();
		await TryLoadPortfolioTokensAsync(unknownMints, cancellationToken);

		var result = new List<TokenBalance>();
		if (!BigInteger.TryParse(holdings.NativeAmount, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var nativeAmount))
			throw new InvalidDataException(
				"Jupiter returned an invalid native SOL balance.");
		result.Add(new("SOL", JupiterExtensions.WrappedSolMint,
			(decimal)nativeAmount / 1_000_000_000m, 0m));

		var available = new List<(JupiterHoldingToken Holding,
			JupiterToken Token)>();
		foreach (var holding in holdingTokens)
		{
			JupiterToken token;
			using (_sync.EnterScope())
				_tokens.TryGetValue(holding.Mint, out token);
			if (token is not null)
				available.Add((holding, token));
		}
		var duplicateSymbols = available.GroupBy(static item =>
			GetBalanceSymbol(item.Token), StringComparer.OrdinalIgnoreCase).Where(
			static group => group.Count() > 1).Select(static group => group.Key)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var item in available)
		{
			BigInteger currentRaw = 0;
			BigInteger blockedRaw = 0;
			foreach (var account in item.Holding.Accounts ?? [])
			{
				if (account.Decimals != item.Token.Decimals ||
					!BigInteger.TryParse(account.Amount, NumberStyles.Integer,
						CultureInfo.InvariantCulture, out var amount))
					throw new InvalidDataException(
						$"Jupiter returned an invalid {item.Token.Symbol} " +
						"token account.");
				currentRaw += amount;
				if (account.IsFrozen)
					blockedRaw += amount;
			}
			var current = currentRaw.ToString(CultureInfo.InvariantCulture)
				.FromRawAmount(item.Token, DateTime.UtcNow);
			var blocked = blockedRaw.ToString(CultureInfo.InvariantCulture)
				.FromRawAmount(item.Token, DateTime.UtcNow);
			var symbol = GetBalanceSymbol(item.Token);
			var code = duplicateSymbols.Contains(symbol)
				? $"{symbol}-" +
					item.Token.Mint[..6].ToUpperInvariant()
				: symbol;
			result.Add(new(code, item.Token.Mint, current, blocked));
		}
		return result.ToArray();
	}

	private static string GetBalanceSymbol(JupiterToken token)
		=> token.Mint == JupiterExtensions.WrappedSolMint
			? "WSOL"
			: token.Symbol;

	private async ValueTask TryLoadPortfolioTokensAsync(string[] mints,
		CancellationToken cancellationToken)
	{
		for (var offset = 0; offset < mints.Length; offset += 100)
		{
			var requested = mints.Skip(offset).Take(100).Select(static mint =>
				mint.NormalizePublicKey()).ToArray();
			foreach (var token in await ApiClient.GetTokensAsync(requested,
				cancellationToken))
			{
				if (token?.Mint.IsEmpty() != false)
					continue;
				var mint = token.Mint.NormalizePublicKey();
				if (!requested.Contains(mint, StringComparer.Ordinal))
					continue;
				try
				{
					ValidateToken(token, mint);
					token.Mint = mint;
					token.TokenProgram = token.TokenProgram.NormalizePublicKey();
					token.Symbol = NormalizeTokenSymbol(token.Symbol, mint);
					token.Name = token.Name.IsEmpty()
						? token.Symbol
						: token.Name.Trim();
					using (_sync.EnterScope())
						_tokens[mint] = token;
				}
				catch (Exception error) when (error is FormatException or
					InvalidDataException)
				{
					this.AddWarningLog(
						"Jupiter portfolio token {0} was skipped: {1}",
						mint, error.Message);
				}
			}
		}
	}

	private async ValueTask SendPerpetualPositionAsync(
		JupiterPerpetualPosition position, long target, bool isForced,
		CancellationToken cancellationToken)
	{
		if (position?.PositionId.IsEmpty() != false ||
			position.AssetMint.IsEmpty())
			return;
		var market = FindPerpetualMarket(position.AssetMint);
		if (market is null)
			return;
		var sizeUsd = position.SizeUsd.FromMicroUsd("position size");
		var average = position.EntryPriceUsd.FromMicroUsd("entry price");
		var mark = position.MarkPriceUsd.FromMicroUsd("mark price");
		var pnl = position.PnlAfterFeesUsd.FromMicroUsd("position PnL");
		var liquidation = position.LiquidationPriceUsd.FromMicroUsd(
			"liquidation price");
		var leverage = JupiterExtensions.ParseDecimal(position.Leverage,
			"position leverage");
		var side = position.Side.ToStockSharp();
		var current = mark > 0 ? sizeUsd / mark : 0m;
		var fingerprint = new PositionFingerprint(current, average, mark, pnl,
			liquidation, leverage, side);
		var key = $"{target}:position:{position.PositionId}";
		using (_sync.EnterScope())
		{
			if (!isForced && _positionFingerprints.TryGetValue(key,
				out var previous) && previous == fingerprint)
				return;
			_positionFingerprints[key] = fingerprint;
		}
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = market.ToStockSharp(),
			DepoName = position.PositionId,
			ServerTime = position.UpdatedTime > 0
				? FromUnixSeconds(position.UpdatedTime)
				: CurrentTime,
			OriginalTransactionId = target,
			Side = current == 0 ? null : side,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, current.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, average, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, mark, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, pnl, true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, liquidation, true)
		.TryAdd(PositionChangeTypes.Leverage, leverage, true),
			cancellationToken);
	}

	private async ValueTask RefreshRemoteOrdersAsync(
		CancellationToken cancellationToken)
	{
		if (!Signer.IsWalletAvailable || !IsPerpetualsEnabled)
			return;
		var positions = (await ApiClient.GetPositionsAsync(
			Signer.WalletAddress, cancellationToken)).Positions ?? [];
		var limits = (await ApiClient.GetLimitOrdersAsync(
			Signer.WalletAddress, cancellationToken)).Orders ?? [];
		var activeIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var limit in limits)
		{
			if (limit?.RequestId.IsEmpty() != false)
				continue;
			var order = CreateRemoteLimitOrder(limit);
			activeIds.Add(order.OrderId);
			MergeRemoteOrder(order);
		}
		foreach (var position in positions)
		{
			if (position?.PositionId.IsEmpty() != false)
				continue;
			foreach (var request in position.TakeProfitStopLossRequests ?? [])
			{
				if (request?.RequestId.IsEmpty() != false)
					continue;
				var order = CreateRemoteTriggerOrder(position, request);
				activeIds.Add(order.OrderId);
				MergeRemoteOrder(order);
			}
		}
		using (_sync.EnterScope())
			foreach (var order in _trackedOrders.Values.Where(static order =>
				order.State == OrderStates.Active &&
				order.Kind is JupiterTrackedOrderKinds.PerpetualLimit or
					JupiterTrackedOrderKinds.TakeProfit or
					JupiterTrackedOrderKinds.StopLoss))
				if (!activeIds.Contains(order.OrderId))
					order.State = OrderStates.Done;
	}

	private TrackedOrder CreateRemoteLimitOrder(
		JupiterPerpetualLimitOrder order)
	{
		var market = FindPerpetualMarket(order.MarketMint) ?? throw new
			InvalidDataException(
				$"Unknown Jupiter Perps mint '{order.MarketMint}'.");
		var price = order.TriggerPrice.FromMicroUsd("limit trigger price");
		var sizeUsd = order.SizeUsd.FromMicroUsd("limit order size");
		return new()
		{
			Market = market,
			Kind = JupiterTrackedOrderKinds.PerpetualLimit,
			OrderId = order.RequestId.NormalizePublicKey(),
			PositionId = order.PositionId,
			Side = order.Side.ToStockSharp(),
			Volume = price > 0 ? sizeUsd / price : 0m,
			Price = price,
			SubmittedTime = ParseApiTime(order.OpenTime),
			State = OrderStates.Active,
		};
	}

	private TrackedOrder CreateRemoteTriggerOrder(
		JupiterPerpetualPosition position,
		JupiterPerpetualPositionRequest request)
	{
		var market = FindPerpetualMarket(position.AssetMint) ?? throw new
			InvalidDataException(
				$"Unknown Jupiter Perps mint '{position.AssetMint}'.");
		var price = request.TriggerPriceUsd.FromMicroUsd(
			"TP/SL trigger price");
		var sizeUsd = request.SizeUsd.IsEmpty()
			? position.SizeUsd.FromMicroUsd("position size")
			: request.SizeUsd.FromMicroUsd("TP/SL size");
		return new()
		{
			Market = market,
			Kind = request.Type == JupiterPerpetualRequestTypes.TakeProfit
				? JupiterTrackedOrderKinds.TakeProfit
				: JupiterTrackedOrderKinds.StopLoss,
			OrderId = request.RequestId.NormalizePublicKey(),
			PositionId = position.PositionId,
			Side = position.Side == JupiterPerpetualSides.Long
				? Sides.Sell
				: Sides.Buy,
			Volume = price > 0 ? sizeUsd / price : 0m,
			Price = price,
			SubmittedTime = ParseApiTime(request.OpenTime),
			State = OrderStates.Active,
		};
	}

	private void MergeRemoteOrder(TrackedOrder remote)
	{
		using (_sync.EnterScope())
		{
			if (_trackedOrders.TryGetValue(remote.OrderId, out var existing))
			{
				existing.Price = remote.Price;
				existing.Volume = remote.Volume;
				existing.PositionId ??= remote.PositionId;
				existing.State = OrderStates.Active;
			}
			else
				_trackedOrders[remote.OrderId] = remote;
		}
	}

	private async ValueTask<TrackedOrder> ResolveTrackedOrderAsync(
		string orderId, CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			if (_trackedOrders.TryGetValue(orderId, out var existing))
				return existing;
		await RefreshRemoteOrdersAsync(cancellationToken);
		using (_sync.EnterScope())
			return _trackedOrders.TryGetValue(orderId, out var order)
				? order
				: throw new InvalidOperationException(
					$"Unknown Jupiter order '{orderId}'.");
	}

	private async ValueTask LoadPrivateTradeHistoryAsync(
		OrderSubscription subscription, CancellationToken cancellationToken)
	{
		if (!Signer.IsWalletAvailable)
			return;
		if (IsPerpetualsEnabled)
		{
			var limit = subscription.Maximum.Min(HistoryLimit).Max(1);
			var page = await ApiClient.GetPerpetualTradesAsync(
				Signer.WalletAddress, 0, limit, subscription.From,
				subscription.To, cancellationToken);
			foreach (var trade in page.Trades ?? [])
				IngestPerpetualTrade(trade);
		}
		await LoadSpotTradeHistoryAsync(subscription, cancellationToken);
	}

	private void IngestPerpetualTrade(JupiterPerpetualTrade trade)
	{
		if (trade?.TransactionHash.IsEmpty() != false ||
			trade.MarketMint.IsEmpty())
			return;
		var market = FindPerpetualMarket(trade.MarketMint);
		if (market is null)
			return;
		var signature = trade.TransactionHash.NormalizeSignature();
		using (_sync.EnterScope())
			if (_trackedOrders.ContainsKey(signature))
				return;
		var price = JupiterExtensions.ParseDecimal(trade.Price,
			"Perps trade price");
		var sizeUsd = JupiterExtensions.ParseDecimal(trade.SizeUsd,
			"Perps trade size");
		var fee = JupiterExtensions.ParseDecimal(trade.Fee,
			"Perps trade fee");
		RememberOrder(new()
		{
			Market = market,
			Kind = trade.Action == JupiterPerpetualTradeActions.Increase
				? JupiterTrackedOrderKinds.PerpetualMarket
				: JupiterTrackedOrderKinds.PerpetualClose,
			OrderId = signature,
			Signature = signature,
			PositionId = trade.PositionId,
			Side = trade.Side.ToStockSharp(),
			Volume = price > 0 ? sizeUsd / price : 0m,
			Price = price,
			Commission = fee,
			CommissionCurrency = "USD",
			SubmittedTime = FromUnixSeconds(trade.CreatedTime),
			State = OrderStates.Done,
			IsTradeSent = true,
		});
	}

	private async ValueTask LoadSpotTradeHistoryAsync(
		OrderSubscription subscription, CancellationToken cancellationToken)
	{
		var left = subscription.Maximum.Min(HistoryLimit).Max(1);
		var offset = default(string);
		var trades = new List<JupiterSpotTrade>();
		for (var pageIndex = 0; pageIndex < 10 && left > 0; pageIndex++)
		{
			var page = await ApiClient.GetSpotTradesAsync(
				Signer.WalletAddress, null, subscription.From, subscription.To,
				(left * 2).Min(30).Max(2), offset, cancellationToken);
			trades.AddRange(page.Trades ?? []);
			if (page.Next.IsEmpty() || page.Next == offset)
				break;
			offset = page.Next;
			left -= (page.Trades?.Length ?? 0) / 2;
		}
		foreach (var group in trades.Where(static trade =>
			!trade.TransactionHash.IsEmpty()).GroupBy(static trade =>
				trade.TransactionHash, StringComparer.Ordinal))
			IngestSpotTrade(group.ToArray());
	}

	private void IngestSpotTrade(JupiterSpotTrade[] entries)
	{
		var sold = entries.FirstOrDefault(static trade =>
			trade.Type == JupiterSpotTradeTypes.Sell);
		var bought = entries.FirstOrDefault(static trade =>
			trade.Type == JupiterSpotTradeTypes.Buy);
		if (sold is null || bought is null)
			return;
		JupiterMarket market;
		Sides side;
		decimal baseAmount;
		decimal quoteAmount;
		using (_sync.EnterScope())
		{
			market = _markets.Values.FirstOrDefault(item =>
				item.Kind == JupiterMarketKinds.Spot &&
				item.BaseToken.Mint == sold.AssetMint &&
				item.QuoteToken.Mint == bought.AssetMint);
			if (market is not null)
			{
				side = Sides.Sell;
				baseAmount = sold.Amount;
				quoteAmount = bought.Amount;
			}
			else
			{
				market = _markets.Values.FirstOrDefault(item =>
					item.Kind == JupiterMarketKinds.Spot &&
					item.QuoteToken.Mint == sold.AssetMint &&
					item.BaseToken.Mint == bought.AssetMint);
				if (market is null)
					return;
				side = Sides.Buy;
				baseAmount = bought.Amount;
				quoteAmount = sold.Amount;
			}
		}
		if (baseAmount <= 0 || quoteAmount <= 0)
			return;
		var signature = sold.TransactionHash.NormalizeSignature();
		using (_sync.EnterScope())
			if (_trackedOrders.ContainsKey(signature))
				return;
		RememberOrder(new()
		{
			Market = market,
			Kind = JupiterTrackedOrderKinds.SpotSwap,
			OrderId = signature,
			Signature = signature,
			Side = side,
			Volume = baseAmount,
			Price = quoteAmount / baseAmount,
			SubmittedTime = ParseApiTime(sold.BlockTime),
			State = OrderStates.Done,
			IsTradeSent = true,
		});
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderSubscription subscription, long target, bool isForced,
		CancellationToken cancellationToken)
	{
		TrackedOrder[] orders;
		using (_sync.EnterScope())
			orders = [.. _trackedOrders.Values.Where(order =>
				Matches(subscription, order)).OrderBy(static order =>
					order.SubmittedTime)];
		var skipped = 0;
		var delivered = 0;
		foreach (var order in orders)
		{
			if (subscription.States is { Length: > 0 } states &&
				!states.Contains(order.State))
				continue;
			if (skipped++ < subscription.Skip)
				continue;
			if (delivered++ >= subscription.Maximum)
				break;
			var key = $"{target}:{order.OrderId}";
			var isOrderRequired = false;
			var isTradeRequired = false;
			using (_sync.EnterScope())
			{
				var isKnown = _orderFingerprints.TryGetValue(key,
					out var previous);
				isOrderRequired = isForced || !isKnown ||
					previous.State != order.State ||
					previous.Price != order.Price ||
					previous.Volume != order.Volume;
				isTradeRequired = order.State == OrderStates.Done &&
					order.IsTradeSent && (!isKnown || !previous.IsTradeSent);
				_orderFingerprints[key] = new(order.State, order.Price,
					order.Volume,
					(isKnown && previous.IsTradeSent) || isTradeRequired);
			}
			if (isOrderRequired)
				await SendTrackedOrderAsync(order, target, cancellationToken);
			if (isTradeRequired)
				await SendTrackedTradeAsync(order, target, cancellationToken);
		}
	}

	private ValueTask SendTrackedOrderAsync(TrackedOrder order, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Market.ToStockSharp(),
			ServerTime = order.SubmittedTime,
			PortfolioName = GetPortfolioName(),
			Side = order.Side,
			OrderVolume = order.Volume,
			Balance = order.State == OrderStates.Active ? order.Volume : 0m,
			OrderPrice = order.Price,
			OrderType = order.Kind switch
			{
				JupiterTrackedOrderKinds.PerpetualLimit => OrderTypes.Limit,
				JupiterTrackedOrderKinds.TakeProfit or
					JupiterTrackedOrderKinds.StopLoss => OrderTypes.Conditional,
				_ => OrderTypes.Market,
			},
			OrderState = order.State,
			OrderStringId = order.OrderId,
			TransactionId = order.TransactionId,
			OriginalTransactionId = target,
			Commission = order.Commission,
			CommissionCurrency = order.CommissionCurrency,
			Condition = CreateCondition(order),
		}, cancellationToken);

	private ValueTask SendTrackedTradeAsync(TrackedOrder order, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = order.Market.ToStockSharp(),
			ServerTime = order.SubmittedTime,
			PortfolioName = GetPortfolioName(),
			Side = order.Side,
			OrderStringId = order.OrderId,
			TradeStringId = order.Signature ?? order.OrderId,
			TradePrice = order.Price,
			TradeVolume = order.Volume,
			TransactionId = order.TransactionId,
			OriginalTransactionId = target,
			Commission = order.Commission,
			CommissionCurrency = order.CommissionCurrency,
		}, cancellationToken);

	private static JupiterOrderCondition CreateCondition(TrackedOrder order)
	{
		if (order.Kind == JupiterTrackedOrderKinds.SpotSwap)
			return null;
		return new()
		{
			Action = order.Kind switch
			{
				JupiterTrackedOrderKinds.PerpetualClose =>
					JupiterOrderActions.Close,
				JupiterTrackedOrderKinds.TakeProfit =>
					JupiterOrderActions.TakeProfit,
				JupiterTrackedOrderKinds.StopLoss =>
					JupiterOrderActions.StopLoss,
				_ => JupiterOrderActions.Open,
			},
			PositionId = order.PositionId,
		};
	}

	private static bool Matches(OrderSubscription subscription,
		TrackedOrder order)
	{
		if (!subscription.OrderId.IsEmpty() &&
			!subscription.OrderId.Equals(order.OrderId,
				StringComparison.Ordinal))
			return false;
		if (!subscription.SecurityId.SecurityCode.IsEmpty() &&
			!subscription.SecurityId.SecurityCode.EqualsIgnoreCase(
				order.Market.SecurityCode))
			return false;
		if (subscription.Side is Sides side && order.Side != side)
			return false;
		return (subscription.From is null ||
				order.SubmittedTime >= subscription.From) &&
			(subscription.To is null || order.SubmittedTime <= subscription.To);
	}

	private JupiterMarket FindPerpetualMarket(string mint)
	{
		if (mint.IsEmpty())
			return null;
		mint = mint.NormalizePublicKey();
		using (_sync.EnterScope())
			return _markets.Values.FirstOrDefault(market =>
				market.Kind == JupiterMarketKinds.Perpetual &&
				market.BaseToken.Mint == mint);
	}

	private void RemoveOrderSubscription(long target)
	{
		using (_sync.EnterScope())
		{
			_orderSubscriptions.Remove(target);
			RemoveFingerprintPrefix(_orderFingerprints, target);
		}
	}

	private async ValueTask CompleteOrderStatusAsync(
		OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}

	private static string NormalizeOrderIdentity(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		byte[] bytes;
		try
		{
			bytes = Encoders.Base58.DecodeData(value);
		}
		catch (Exception error) when (error is FormatException or
			ArgumentException)
		{
			throw new FormatException(
				$"Invalid Jupiter order identity '{value}'.", error);
		}
		if (bytes.Length is not (32 or 64))
			throw new FormatException(
				$"Invalid Jupiter order identity '{value}'.");
		return value;
	}

	private static DateTime FromUnixSeconds(long value)
	{
		try
		{
			return DateTime.UnixEpoch.AddSeconds(value);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				$"Jupiter returned invalid Unix time '{value}'.", error);
		}
	}

	private static DateTime ParseApiTime(string value)
	{
		if (long.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var seconds))
			return FromUnixSeconds(seconds);
		if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var time))
			return time;
		return DateTime.UtcNow;
	}
}
