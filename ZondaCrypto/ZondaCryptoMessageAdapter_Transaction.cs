namespace StockSharp.ZondaCrypto;

public partial class ZondaCryptoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var market = GetMarket(regMsg.SecurityId);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"zondacrypto order volume must be positive.");
		if (market.MinimumBaseAmount > 0 &&
			volume < market.MinimumBaseAmount)
			throw new InvalidOperationException(
				$"zondacrypto order amount {volume} is below the " +
					$"{market.MinimumBaseAmount} minimum.");
		if (regMsg.VisibleVolume is > 0 &&
			regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"zondacrypto does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"zondacrypto does not expose GTD expiration.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (
			OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(
					orderType, regMsg.TransactionId));
		if (orderType == OrderTypes.Limit &&
			regMsg.Price <= 0)
			throw new InvalidOperationException(
				"zondacrypto limit orders require a positive price.");
		if (regMsg.PostOnly == true &&
			orderType != OrderTypes.Limit)
			throw new NotSupportedException(
				"zondacrypto post-only execution is available only " +
					"for limit orders.");
		if (regMsg.TimeInForce is not (
			null or
			TimeInForce.PutInQueue or
			TimeInForce.MatchOrCancel or
			TimeInForce.CancelBalance))
			throw new NotSupportedException(
				"zondacrypto supports GTC, IOC and FOK only.");

		var result = await RestClient.PlaceOrderAsync(
			new()
			{
				MarketCode = market.Code,
				Side = regMsg.Side,
				OrderType = orderType,
				TimeInForce = regMsg.TimeInForce,
				PostOnly = regMsg.PostOnly == true,
				Price = regMsg.Price,
				Amount = volume,
			},
			cancellationToken);
		if (result?.Id.IsEmpty() != false)
			throw new InvalidDataException(
				"zondacrypto accepted an order without returning " +
					"its identifier.");
		TrackOrder(result.Id, new()
		{
			TransactionId = regMsg.TransactionId,
			MarketCode = market.Code,
			SecurityCode = market.SecurityCode,
			Side = regMsg.Side,
			OrderType = orderType,
			Volume = volume,
			Price = regMsg.Price,
		});
		await SendOrderAsync(
			result,
			regMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var orderId = ResolveOrderId(
			cancelMsg.OrderId,
			cancelMsg.OrderStringId);
		var tracked = GetTrackedOrder(orderId);
		if (tracked is null)
		{
			var offer = (await RestClient.GetOffersAsync(
				null, cancellationToken) ?? [])
				.FirstOrDefault(item =>
					item.Id.EqualsIgnoreCase(orderId));
			if (offer is not null)
			{
				var market = GetMarket(offer.MarketCode);
				tracked = new()
				{
					MarketCode = offer.MarketCode,
					SecurityCode = market?.SecurityCode,
					Side = offer.Side,
					OrderType = offer.OrderType,
					Volume = offer.OriginalAmount,
					Price = offer.Price,
				};
			}
		}
		if (tracked is null ||
			tracked.MarketCode.IsEmpty() ||
			tracked.Price <= 0)
			throw new InvalidOperationException(
				"zondacrypto cancellation requires the active " +
					"order market, side and price.");
		var result = await RestClient.CancelOrderAsync(
			tracked.MarketCode,
			orderId,
			tracked.Side,
			tracked.Price,
			cancellationToken);
		await SendOrderAsync(
			result,
			cancelMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"zondacrypto does not provide an atomic order-replace " +
				"operation.");
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.Mode.HasFlag(
			OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"zondacrypto spot cancellation cannot close " +
					"positions.");
		var market = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId);
		foreach (var offer in await RestClient.GetOffersAsync(
			market?.Code, cancellationToken) ?? [])
		{
			if (cancelMsg.Side is Sides side &&
				offer.Side != side ||
				offer.Price <= 0)
				continue;
			var result = await RestClient.CancelOrderAsync(
				offer.MarketCode,
				offer.Id,
				offer.Side,
				offer.Price,
				cancellationToken);
			await SendOrderAsync(
				result,
				cancelMsg.TransactionId,
				cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId, cancellationToken);
		EnsurePrivateReady();
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId ==
				lookupMsg.OriginalTransactionId)
			{
				_portfolioSubscriptionId = 0;
				await WsClient.UnsubscribePrivateAsync(
					"balances",
					"balance/bitbay/updatefunds",
					cancellationToken);
			}
			return;
		}

		var portfolioName = GetPortfolioName();
		if (lookupMsg.PortfolioName.IsEmpty() ||
			lookupMsg.PortfolioName.EqualsIgnoreCase(
				portfolioName))
		{
			await SendOutMessageAsync(
				new PortfolioMessage
				{
					PortfolioName = portfolioName,
					BoardCode = BoardCodes.ZondaCrypto,
					OriginalTransactionId =
						lookupMsg.TransactionId,
				},
				cancellationToken);
			await SendPortfolioSnapshotAsync(
				lookupMsg.TransactionId, cancellationToken);
		}
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(
				lookupMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(
				lookupMsg.TransactionId, cancellationToken);
			return;
		}
		if (_portfolioSubscriptionId != 0)
			throw new InvalidOperationException(
				"zondacrypto portfolio subscription already exists.");
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		try
		{
			await WsClient.SubscribePrivateAsync(
				"balances",
				"balance/bitbay/updatefunds",
				cancellationToken);
			await SendSubscriptionResultAsync(
				lookupMsg, cancellationToken);
		}
		catch
		{
			_portfolioSubscriptionId = 0;
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			statusMsg.TransactionId, cancellationToken);
		EnsurePrivateReady();
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId ==
				statusMsg.OriginalTransactionId)
			{
				_orderStatusSubscriptionId = 0;
				await WsClient.UnsubscribePrivateAsync(
					"trading",
					"offers",
					cancellationToken);
			}
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await CompleteOrderStatusAsync(
				statusMsg, cancellationToken);
			return;
		}

		await SendOrderSnapshotAsync(
			statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(
				statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(
				statusMsg.TransactionId, cancellationToken);
			return;
		}
		if (_orderStatusSubscriptionId != 0)
			throw new InvalidOperationException(
				"zondacrypto order-status subscription already " +
					"exists.");
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		try
		{
			await WsClient.SubscribePrivateAsync(
				"trading",
				"offers",
				cancellationToken);
			await SendSubscriptionResultAsync(
				statusMsg, cancellationToken);
		}
		catch
		{
			_orderStatusSubscriptionId = 0;
			throw;
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var wallet in await RestClient.GetWalletsAsync(
			cancellationToken) ?? [])
			await SendBalanceAsync(
				wallet,
				originalTransactionId,
				cancellationToken);
	}

	private ValueTask SendBalanceAsync(
		ZondaCryptoWallet wallet,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (wallet?.Currency.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(
			new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = new()
				{
					SecurityCode = wallet.Currency,
					BoardCode = BoardCodes.ZondaCrypto,
				},
				ServerTime = CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(
				PositionChangeTypes.CurrentValue,
				wallet.Available,
				true)
			.TryAdd(
				PositionChangeTypes.BlockedValue,
				wallet.Locked,
				true),
			cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		var market = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(statusMsg.SecurityId);
		var maximum = (statusMsg.Count ?? 100)
			.Max(1).Min(200).To<int>();
		foreach (var offer in (await RestClient.GetOffersAsync(
			market?.Code, cancellationToken) ?? [])
			.Where(offer => MatchesOrder(offer, statusMsg))
			.OrderBy(GetOrderTimestamp)
			.TakeLast(maximum))
			await SendOrderAsync(
				offer,
				statusMsg.TransactionId,
				cancellationToken);
	}

	private async ValueTask PollOrdersAsync(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var offer in await RestClient.GetOffersAsync(
			null, cancellationToken) ?? [])
			await SendOrderAsync(
				offer,
				originalTransactionId,
				cancellationToken);
	}

	private ValueTask SendOrderAsync(
		ZondaCryptoOffer offer,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (offer?.Id.IsEmpty() != false ||
			offer.MarketCode.IsEmpty())
			return default;
		var market = GetMarket(offer.MarketCode);
		if (market is null)
			return default;
		var tracked = GetTrackedOrder(offer.Id);
		tracked ??= new()
		{
			MarketCode = market.Code,
			SecurityCode = market.SecurityCode,
			Side = offer.Side,
			OrderType = offer.OrderType,
			Volume = offer.OriginalAmount,
			Price = offer.Price,
		};
		TrackOrder(offer.Id, tracked);
		var execution = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = offer.CreatedAt?.ToUniversalTime() ??
				CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = offer.Side,
			OrderVolume = offer.OriginalAmount > 0
				? offer.OriginalAmount
				: tracked.Volume,
			Balance = offer.RemainingAmount,
			OrderPrice = offer.Price > 0
				? offer.Price
				: tracked.Price,
			OrderType = offer.OrderType,
			OrderState = offer.State == OrderStates.None
				? OrderStates.Active
				: offer.State,
			OrderStringId = offer.Id,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = offer.TimeInForce,
			PostOnly = offer.PostOnly,
		};
		if (long.TryParse(
			offer.Id,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var numericOrderId))
			execution.OrderId = numericOrderId;
		return SendOutMessageAsync(execution, cancellationToken);
	}

	private bool MatchesOrder(
		ZondaCryptoOffer offer,
		OrderStatusMessage filter)
	{
		if (offer?.Id.IsEmpty() != false)
			return false;
		if (filter.HasOrderId())
		{
			var requestedId = !filter.OrderStringId.IsEmpty()
				? filter.OrderStringId
				: filter.OrderId?.ToString(
					CultureInfo.InvariantCulture);
			if (!offer.Id.EqualsIgnoreCase(requestedId))
				return false;
		}
		var time = offer.CreatedAt?.ToUniversalTime() ??
			DateTime.MinValue;
		if (filter.From is DateTime from &&
			time < from.ToUniversalTime() ||
			filter.To is DateTime to &&
			time > to.ToUniversalTime())
			return false;
		if (filter.Side is Sides side &&
			offer.Side != side)
			return false;
		if (filter.States.Length > 0 &&
			!filter.States.Contains(offer.State))
			return false;
		if (filter.Volume is decimal volume &&
			offer.OriginalAmount != volume)
			return false;
		if (!filter.PortfolioName.IsEmpty() &&
			!filter.PortfolioName.EqualsIgnoreCase(
				GetPortfolioName()))
			return false;
		if (!filter.SecurityId.SecurityCode.IsEmpty())
			return GetMarket(filter.SecurityId).Code
				.EqualsIgnoreCase(offer.MarketCode);
		return true;
	}

	private static DateTime GetOrderTimestamp(
		ZondaCryptoOffer offer)
		=> offer?.CreatedAt?.ToUniversalTime() ??
			DateTime.MinValue;

	private async ValueTask CompleteOrderStatusAsync(
		OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}
}
