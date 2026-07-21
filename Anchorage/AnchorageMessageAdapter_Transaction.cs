namespace StockSharp.Anchorage;

public partial class AnchorageMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"Anchorage does not expose a post-only instruction.");
		var condition = regMsg.Condition as AnchorageOrderCondition;
		var operation = condition?.Operation ?? AnchorageOperations.Trade;
		if (operation == AnchorageOperations.Trade)
			await RegisterTradingOrderAsync(regMsg, condition, cancellationToken);
		else
			await RegisterCustodyOperationAsync(regMsg, condition ?? throw new
				InvalidOperationException(
					"Anchorage custody operations require AnchorageOrderCondition."),
				cancellationToken);
	}

	private async ValueTask RegisterTradingOrderAsync(
		OrderRegisterMessage message, AnchorageOrderCondition condition,
		CancellationToken cancellationToken)
	{
		var product = GetProduct(message.SecurityId);
		var portfolio = GetPortfolio(message.PortfolioName);
		if (portfolio.Kind != PortfolioKinds.Trading)
			throw new InvalidOperationException(
				"Anchorage trading orders require a trading portfolio.");
		var quantity = message.Volume.Abs();
		if (quantity <= 0)
			throw new InvalidOperationException(
				"Anchorage order quantity must be positive.");
		var nativeType = ResolveNativeOrderType(message, condition);
		var clientOrderId = CreateClientOrderId(message);
		var currency = condition?.QuantityCurrency;
		if (currency.IsEmpty())
			currency = product.Reference?.BaseAssetType;
		currency = currency.ThrowIfEmpty(nameof(condition.QuantityCurrency));

		AnchorageTradingOrder order;
		if (nativeType is AnchorageNativeOrderTypes.Market or
			AnchorageNativeOrderTypes.Rfq or
			AnchorageNativeOrderTypes.LimitAllIn)
		{
			if (message.TimeInForce is not null and not TimeInForce.MatchOrCancel)
				throw new NotSupportedException(
					"Anchorage immediate orders use fill-or-kill semantics.");
			var request = new AnchorageImmediateOrderRequest
			{
				ClientOrderId = clientOrderId,
				Symbol = product.Pair,
				Side = message.Side.ToAnchorage(),
				Currency = currency,
				Quantity = FormatAmount(quantity),
				OrderType = nativeType,
				LimitPrice = GetLimitPrice(message, nativeType),
				Timestamp = DateTime.UtcNow.ToAnchorageTime(),
				AccountId = portfolio.Id,
			};
			order = await PlaceTradingOrderAsync(clientOrderId,
				ct => RestClient.PlaceImmediateOrderAsync(request, ct),
				cancellationToken);
			PopulateTradingOrder(order, request.ClientOrderId, request.Symbol,
				request.AccountId, request.Side, request.Currency, request.Quantity,
				request.OrderType, request.LimitPrice,
				AnchorageTimeInForces.FillOrKill, request.Timestamp);
		}
		else
		{
			if (nativeType is not (AnchorageNativeOrderTypes.Limit or
				AnchorageNativeOrderTypes.StopLoss or
				AnchorageNativeOrderTypes.StopLimit or
				AnchorageNativeOrderTypes.TakeProfitLimit))
				throw new NotSupportedException(
					$"Anchorage {nativeType} orders are historical/reporting types and " +
					"cannot be submitted through the asynchronous order endpoint.");
			var triggerPrice = condition?.TriggerPrice;
			if (nativeType is AnchorageNativeOrderTypes.StopLoss or
				AnchorageNativeOrderTypes.StopLimit or
				AnchorageNativeOrderTypes.TakeProfitLimit && triggerPrice is not > 0)
				throw new InvalidOperationException(
					$"Anchorage {nativeType} requires a positive trigger price.");
			var request = new AnchorageAsyncOrderRequest
			{
				ClientOrderId = clientOrderId,
				Symbol = product.Pair,
				Side = message.Side.ToAnchorage(),
				Currency = currency,
				Quantity = FormatAmount(quantity),
				OrderType = nativeType,
				LimitPrice = GetLimitPrice(message, nativeType),
				TimeInForce = message.TimeInForce.ToAnchorage(),
				Timestamp = DateTime.UtcNow.ToAnchorageTime(),
				AccountId = portfolio.Id,
				Parameters = triggerPrice is > 0 || condition?.EndTime is not null
					? new()
					{
						TriggerPrice = triggerPrice is > 0
							? FormatAmount(triggerPrice.Value)
							: null,
						EndTime = condition?.EndTime?.EnsureUtc()
							.ToAnchorageTime(),
					}
					: null,
			};
			order = await PlaceTradingOrderAsync(clientOrderId,
				ct => RestClient.PlaceAsyncOrderAsync(request, ct),
				cancellationToken);
			PopulateTradingOrder(order, request.ClientOrderId, request.Symbol,
				request.AccountId, request.Side, request.Currency, request.Quantity,
				request.OrderType, request.LimitPrice, request.TimeInForce,
				request.Timestamp);
		}

		if (order?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				"Anchorage returned an incomplete order response.");
		TrackTradingOrder(order, message.TransactionId, portfolio.Name);
		await SendTradingOrderAsync(order, message.TransactionId, true,
			cancellationToken, portfolio.Name);
		if (!order.Status.IsFinal() && _socketClient is not null)
			await _socketClient.WatchOrderAsync(order.OrderId,
				order.ClientOrderId, order.AccountId, cancellationToken);
	}

	private static AnchorageNativeOrderTypes ResolveNativeOrderType(
		OrderRegisterMessage message, AnchorageOrderCondition condition)
	{
		if (condition?.NativeOrderType is AnchorageNativeOrderTypes native &&
			native != AnchorageNativeOrderTypes.Unknown)
			return native;
		return message.OrderType switch
		{
			OrderTypes.Market => AnchorageNativeOrderTypes.Market,
			OrderTypes.Limit => AnchorageNativeOrderTypes.Limit,
			OrderTypes.Conditional => throw new InvalidOperationException(
				"Set NativeOrderType for an Anchorage conditional trading order."),
			_ => throw new NotSupportedException(
				$"Anchorage does not support {message.OrderType} orders."),
		};
	}

	private static string GetLimitPrice(OrderRegisterMessage message,
		AnchorageNativeOrderTypes nativeType)
	{
		if (nativeType is AnchorageNativeOrderTypes.Market or
			AnchorageNativeOrderTypes.Rfq)
			return null;
		if (nativeType == AnchorageNativeOrderTypes.StopLoss &&
			message.Price <= 0)
			return null;
		if (message.Price <= 0)
			throw new InvalidOperationException(
				$"Anchorage {nativeType} requires a positive limit price.");
		return FormatAmount(message.Price);
	}

	private async ValueTask<AnchorageTradingOrder> PlaceTradingOrderAsync(
		string clientOrderId,
		Func<CancellationToken, ValueTask<AnchorageTradingOrder>> submit,
		CancellationToken cancellationToken)
	{
		try
		{
			return await submit(cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
			(error is HttpRequestException or TaskCanceledException ||
				error is AnchorageApiException apiError && apiError.StatusCode is
					HttpStatusCode.BadRequest or HttpStatusCode.Conflict))
		{
			try
			{
				var recovered = await RestClient.GetTradingOrderAsync(clientOrderId,
					cancellationToken);
				if (recovered is not null)
					return recovered;
			}
			catch (AnchorageApiException recoveryError) when (
				recoveryError.StatusCode == HttpStatusCode.NotFound)
			{
			}
			ExceptionDispatchInfo.Capture(error).Throw();
			throw;
		}
	}

	private static void PopulateTradingOrder(AnchorageTradingOrder order,
		string clientOrderId, string symbol, string accountId,
		AnchorageSides side, string currency, string quantity,
		AnchorageNativeOrderTypes orderType, string limitPrice,
		AnchorageTimeInForces timeInForce, string timestamp)
	{
		ArgumentNullException.ThrowIfNull(order);
		order.ClientOrderId = order.ClientOrderId.IsEmpty()
			? clientOrderId
			: order.ClientOrderId;
		order.Symbol = order.Symbol.IsEmpty() ? symbol : order.Symbol;
		order.AccountId = order.AccountId.IsEmpty() ? accountId : order.AccountId;
		if (order.Side == AnchorageSides.Unknown)
			order.Side = side;
		order.Currency = order.Currency.IsEmpty() ? currency : order.Currency;
		order.OrderQuantity = order.OrderQuantity.IsEmpty()
			? quantity
			: order.OrderQuantity;
		if (order.OrderType == AnchorageNativeOrderTypes.Unknown)
			order.OrderType = orderType;
		order.LimitPrice = order.LimitPrice.IsEmpty()
			? limitPrice
			: order.LimitPrice;
		if (order.TimeInForce == AnchorageTimeInForces.Unknown)
			order.TimeInForce = timeInForce;
		order.SubmitTime = order.SubmitTime.IsEmpty()
			? timestamp
			: order.SubmitTime;
	}

	private async ValueTask RegisterCustodyOperationAsync(
		OrderRegisterMessage message, AnchorageOrderCondition condition,
		CancellationToken cancellationToken)
	{
		if (message.OrderType != OrderTypes.Conditional)
			throw new NotSupportedException(
				"Anchorage custody operations use conditional orders.");
		if (message.Side != Sides.Sell)
			throw new NotSupportedException(
				"Anchorage outgoing custody operations use the sell side.");
		if (message.TimeInForce is not null)
			throw new NotSupportedException(
				"Time-in-force is not applicable to custody operations.");
		var portfolio = GetPortfolio(message.PortfolioName);
		var assetType = message.SecurityId.SecurityCode.ThrowIfEmpty(
			nameof(message.SecurityId)).Trim();
		var amount = message.Volume.Abs();
		if (amount <= 0 && !(condition.Operation == AnchorageOperations.Unstake &&
			condition.IsFullAmount))
			throw new InvalidOperationException(
				"Anchorage custody amount must be positive.");
		var wallet = GetSourceWallet(portfolio, assetType,
			condition.SourceWalletId);
		var source = new AnchorageResource
		{
			Id = wallet.Id,
			Type = AnchorageResourceTypes.Wallet,
		};
		var idempotentId = CreateClientOrderId(message);
		string nativeId;
		switch (condition.Operation)
		{
			case AnchorageOperations.Transfer:
			{
				var request = new AnchorageTransferRequest
				{
					IdempotentId = idempotentId,
					Source = source,
					Destination = CreateDestination(condition),
					AssetType = assetType,
					Amount = FormatAmount(amount),
					Memo = condition.Memo,
					IsFeeDeducted = condition.IsFeeDeducted,
					IsGasStationUsed = condition.IsGasStationUsed,
					ExtraParameters = CreateAssetParameters(condition),
				};
				var transfer = await ExecuteIdempotentAsync(
					ct => RestClient.CreateTransferAsync(request, ct),
					cancellationToken);
				if (transfer?.Id.IsEmpty() != false)
					throw new InvalidDataException(
						"Anchorage returned an incomplete transfer response.");
				nativeId = transfer.Id;
				TrackTransfer(transfer, message.TransactionId, portfolio.Name,
					idempotentId);
				await SendTransferAsync(transfer, message.TransactionId, true,
					cancellationToken, portfolio.Name);
				break;
			}
			case AnchorageOperations.Withdrawal:
			{
				var request = new AnchorageWithdrawalRequest
				{
					IdempotentId = idempotentId,
					Source = source,
					Destination = CreateDestination(condition),
					AssetType = assetType,
					Amount = FormatAmount(amount),
					Description = message.Comment,
					IsGasStationUsed = condition.IsGasStationUsed,
					ExtraParameters = CreateAssetParameters(condition),
				};
				nativeId = await ExecuteIdempotentAsync(
					ct => RestClient.CreateWithdrawalAsync(request, ct),
					cancellationToken);
				await TrackAndSendTransactionAsync(nativeId, message, portfolio,
					assetType, amount, condition.Operation, idempotentId,
					cancellationToken);
				break;
			}
			case AnchorageOperations.Stake:
			{
				var request = new AnchorageStakingRequest
				{
					IdempotentId = idempotentId,
					Source = source,
					AssetType = assetType,
					Amount = FormatAmount(amount),
					Description = message.Comment,
					Parameters = new()
					{
						Provider = condition.StakingProvider,
						ProviderAddress = condition.StakingProviderAddress,
						ValidatorType = condition.ValidatorType ??
							AnchorageValidatorTypes.Unknown,
						PositionId = condition.StakingPositionId,
					},
				};
				nativeId = await ExecuteIdempotentAsync(
					ct => RestClient.CreateStakeAsync(request, ct), cancellationToken);
				await TrackAndSendTransactionAsync(nativeId, message, portfolio,
					assetType, amount, condition.Operation, idempotentId,
					cancellationToken);
				break;
			}
			case AnchorageOperations.Unstake:
			{
				var request = new AnchorageUnstakingRequest
				{
					IdempotentId = idempotentId,
					Source = source,
					AssetType = assetType,
					Amount = condition.IsFullAmount ? null : FormatAmount(amount),
					IsFullAmount = condition.IsFullAmount,
					StakingPositionId = condition.StakingPositionId.ThrowIfEmpty(
						nameof(condition.StakingPositionId)),
					Description = message.Comment,
				};
				nativeId = await ExecuteIdempotentAsync(
					ct => RestClient.CreateUnstakeAsync(request, ct), cancellationToken);
				await TrackAndSendTransactionAsync(nativeId, message, portfolio,
					assetType, amount, condition.Operation, idempotentId,
					cancellationToken);
				break;
			}
			default:
				throw new ArgumentOutOfRangeException(nameof(condition.Operation),
					condition.Operation, null);
		}
		if (nativeId.IsEmpty())
			throw new InvalidDataException(
				"Anchorage returned no custody operation identifier.");
	}

	private static AnchorageResource CreateDestination(
		AnchorageOrderCondition condition)
	{
		if (condition.DestinationType == AnchorageResourceTypes.Unknown)
			throw new InvalidOperationException(
				"Anchorage destination type must be specified.");
		return new()
		{
			Id = condition.DestinationId.ThrowIfEmpty(
				nameof(condition.DestinationId)).Trim(),
			Type = condition.DestinationType,
		};
	}

	private static AnchorageAssetParameters CreateAssetParameters(
		AnchorageOrderCondition condition)
	{
		var value = condition.WithdrawInfo?.PaymentId;
		return value.IsEmpty() ? null : new() { Value = value.Trim() };
	}

	private async ValueTask<T> ExecuteIdempotentAsync<T>(
		Func<CancellationToken, ValueTask<T>> action,
		CancellationToken cancellationToken)
	{
		try
		{
			return await action(cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
			error is HttpRequestException or TaskCanceledException)
		{
			this.AddWarningLog(
				"Anchorage write result was uncertain; retrying the same idempotent request: {0}",
				error.Message);
			return await action(cancellationToken);
		}
	}

	private async ValueTask TrackAndSendTransactionAsync(string nativeId,
		OrderRegisterMessage message, PortfolioReference portfolio,
		string assetType, decimal amount, AnchorageOperations operation,
		string clientOrderId, CancellationToken cancellationToken)
	{
		if (nativeId.IsEmpty())
			throw new InvalidDataException(
				"Anchorage returned no transaction identifier.");
		AnchorageTransaction transaction;
		try
		{
			transaction = await RestClient.GetTransactionAsync(nativeId,
				cancellationToken);
		}
		catch (AnchorageApiException error) when (
			error.StatusCode == HttpStatusCode.NotFound)
		{
			transaction = null;
		}
		transaction ??= new()
		{
			Id = nativeId,
			AssetType = assetType,
			Amount = new()
			{
				AssetType = assetType,
				Quantity = FormatAmount(amount),
			},
			Status = AnchorageTransactionStatuses.Initiating,
			Type = operation == AnchorageOperations.Withdrawal
				? AnchorageTransactionTypes.Withdraw
				: AnchorageTransactionTypes.Other,
			VaultId = portfolio.Id,
			Timestamp = DateTime.UtcNow.ToAnchorageTime(),
		};
		TrackTransaction(transaction, message.TransactionId, portfolio.Name,
			operation, clientOrderId);
		await SendTransactionAsync(transaction, message.TransactionId, true,
			cancellationToken, portfolio.Name, operation);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		var nativeId = cancelMsg.OrderStringId;
		if (nativeId.IsEmpty())
		{
			using (_sync.EnterScope())
				_nativeIds.TryGetValue(cancelMsg.OriginalTransactionId, out nativeId);
		}
		if (nativeId.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(
					cancelMsg.OriginalTransactionId));
		var tracked = GetTrackedOperation(nativeId, null) ?? throw new
			InvalidOperationException(
				$"Anchorage operation '{nativeId}' is not tracked by this session.");
		switch (tracked.Kind)
		{
			case NativeOperationKinds.TradingOrder:
			{
				var order = await RestClient.CancelTradingOrderAsync(nativeId,
					cancellationToken) ?? await RestClient.GetTradingOrderAsync(nativeId,
						cancellationToken);
				TrackTradingOrder(order, tracked.TransactionId,
					tracked.PortfolioName);
				await SendTradingOrderAsync(order, cancelMsg.TransactionId, true,
					cancellationToken, tracked.PortfolioName);
				break;
			}
			case NativeOperationKinds.Transfer:
			{
				await RestClient.CancelTransferAsync(nativeId, cancellationToken);
				var transfer = await RestClient.GetTransferAsync(nativeId,
					cancellationToken);
				TrackTransfer(transfer, tracked.TransactionId,
					tracked.PortfolioName, tracked.ClientOrderId);
				await SendTransferAsync(transfer, cancelMsg.TransactionId, true,
					cancellationToken, tracked.PortfolioName);
				break;
			}
			case NativeOperationKinds.Transaction:
				throw new NotSupportedException(
					"Anchorage transaction and staking workflows have no API cancellation endpoint.");
			default:
				throw new ArgumentOutOfRangeException(nameof(tracked.Kind),
					tracked.Kind, null);
		}
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Anchorage orders cannot be atomically replaced.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Anchorage has no atomic group-cancellation endpoint.");
	}

	private static string CreateClientOrderId(OrderRegisterMessage message)
	{
		var value = message.UserOrderId.IsEmpty()
			? "stocksharp-" + message.TransactionId.ToString(
				CultureInfo.InvariantCulture)
			: message.UserOrderId.Trim();
		if (value.Length > 255)
			throw new ArgumentOutOfRangeException(nameof(message.UserOrderId),
				value, "Anchorage client order ID cannot exceed 255 characters.");
		return value;
	}

	private static string FormatAmount(decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);
}
