namespace StockSharp.Paxos;

using StockSharp.Paxos.Native;
using StockSharp.Paxos.Native.Model;

public partial class PaxosMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage message, CancellationToken cancellationToken)
	{
		EnsureAuthenticated();
		var condition = message.Condition as PaxosOrderCondition;
		var operation = condition?.Operation ?? PaxosOperations.Trade;
		if (operation == PaxosOperations.Trade)
			await RegisterTradingOrderAsync(message, condition, cancellationToken);
		else
			await RegisterCustodyOperationAsync(message, condition ?? throw new
				InvalidOperationException(
					"Paxos custody operations require PaxosOrderCondition."),
				cancellationToken);
	}

	private async ValueTask RegisterTradingOrderAsync(
		OrderRegisterMessage message, PaxosOrderCondition condition,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(message.SecurityId);
		var portfolio = await GetPortfolioAsync(message.PortfolioName,
			cancellationToken);
		var volume = message.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"Paxos order volume must be positive.");
		ValidateIdentity(condition);
		var nativeType = ResolveOrderType(message, condition);
		var isMarket = nativeType == PaxosOrderTypes.Market;
		var isMarketBuy = isMarket &&
			message.Side == Sides.Buy;
		var quoteAmount = isMarketBuy ? condition?.QuoteAmount : null;
		if (isMarketBuy && quoteAmount is not > 0)
			throw new InvalidOperationException(
				"Paxos market buys require a positive QuoteAmount in PaxosOrderCondition.");
		if (!isMarket && message.Price <= 0)
			throw new InvalidOperationException(
				$"Paxos {nativeType} orders require a positive limit price.");
		if (nativeType is PaxosOrderTypes.StopMarket or
			PaxosOrderTypes.StopLimit && condition?.StopPrice is not > 0)
			throw new InvalidOperationException(
				$"Paxos {nativeType} orders require a positive stop price.");
		if (nativeType == PaxosOrderTypes.StopMarket &&
			message.Side != Sides.Sell)
			throw new NotSupportedException(
				"Paxos stop-market orders are available only for sells.");

		var hasExpiration = message.TillDate != default &&
			message.TillDate != DateTime.MaxValue;
		if (hasExpiration && message.TillDate.Value.EnsureUtc() <=
			DateTime.UtcNow + TimeSpan.FromSeconds(10))
			throw new InvalidOperationException(
				"Paxos GTT expiration must be more than ten seconds in the future.");
		if (isMarket && hasExpiration)
			throw new NotSupportedException(
				"Paxos market orders use IOC and cannot have an expiration.");
		var timeInForce = hasExpiration
			? PaxosTimeInForces.GoodTillTime
			: message.TimeInForce.ToPaxos(isMarket);
		if (isMarket && timeInForce != PaxosTimeInForces.ImmediateOrCancel)
			throw new NotSupportedException(
				"Paxos market orders use immediate-or-cancel time in force.");
		if (nativeType is PaxosOrderTypes.StopMarket or
			PaxosOrderTypes.StopLimit && timeInForce is not
				(PaxosTimeInForces.GoodTillCancel or
					PaxosTimeInForces.GoodTillTime))
			throw new NotSupportedException(
				"Paxos stop orders support only GTC or GTT time in force.");
		var refId = CreateRefId(message.TransactionId);
		var request = new PaxosCreateOrderRequest
		{
			RefId = refId,
			Side = message.Side.ToPaxos(),
			Market = market.Market,
			Type = nativeType,
			BaseAmount = isMarketBuy ? null : volume.ToPaxosAmount(),
			QuoteAmount = isMarketBuy
				? quoteAmount.Value.ToPaxosAmount()
				: null,
			Price = nativeType is PaxosOrderTypes.Market or
				PaxosOrderTypes.StopMarket
					? null
					: message.Price.ToPaxosAmount(),
			StopPrice = condition?.StopPrice is > 0
				? condition.StopPrice.Value.ToPaxosAmount()
				: null,
			TimeInForce = timeInForce,
			ExpirationDate = hasExpiration
				? ToUnixMilliseconds(message.TillDate.Value.EnsureUtc()).ToString(
					CultureInfo.InvariantCulture)
				: null,
			IdentityId = condition?.IdentityId,
			IdentityAccountId = condition?.IdentityAccountId,
			RecipientProfileId = condition?.DestinationProfileId,
		};
		var order = await SubmitOrderAsync(portfolio.Profile.Id, request,
			cancellationToken);
		if (order?.Id.IsEmpty() != false)
			throw new InvalidDataException(
				"Paxos returned an incomplete order response.");
		PopulateOrder(order, portfolio.Profile.Id, request);
		TrackOrder(order, message.TransactionId, portfolio.Name);
		await SendOrderAsync(order, message.TransactionId, true,
			portfolio.Name, cancellationToken);
	}

	private static PaxosOrderTypes ResolveOrderType(
		OrderRegisterMessage message, PaxosOrderCondition condition)
	{
		if (message.PostOnly == true || condition?.IsPostOnly == true)
		{
			if (message.OrderType is not (null or OrderTypes.Limit))
				throw new NotSupportedException(
					"Paxos post-only is available only for limit orders.");
			return PaxosOrderTypes.PostOnlyLimit;
		}
		return message.OrderType switch
		{
			null when message.Price > 0 => PaxosOrderTypes.Limit,
			null => PaxosOrderTypes.Market,
			OrderTypes.Limit => PaxosOrderTypes.Limit,
			OrderTypes.Market => PaxosOrderTypes.Market,
			OrderTypes.Conditional when condition?.StopPrice is > 0 &&
				message.Price > 0 => PaxosOrderTypes.StopLimit,
			OrderTypes.Conditional when condition?.StopPrice is > 0 =>
				PaxosOrderTypes.StopMarket,
			_ => throw new NotSupportedException(
				$"Paxos does not support {message.OrderType} orders."),
		};
	}

	private async ValueTask<PaxosOrder> SubmitOrderAsync(string profileId,
		PaxosCreateOrderRequest request, CancellationToken cancellationToken)
	{
		try
		{
			return await RestClient.CreateOrderAsync(profileId, request,
				cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
			(error is HttpRequestException or TaskCanceledException ||
				error is PaxosApiException apiError && apiError.StatusCode ==
					HttpStatusCode.Conflict))
		{
			var recovered = await RestClient.GetOrdersAsync(profileId,
				request.RefId, null, null, 10, 10, cancellationToken);
			var order = recovered.SingleOrDefault(item =>
				item?.RefId.EqualsIgnoreCase(request.RefId) == true);
			if (order is not null)
				return order;
			ExceptionDispatchInfo.Capture(error).Throw();
			throw;
		}
	}

	private static void PopulateOrder(PaxosOrder order, string profileId,
		PaxosCreateOrderRequest request)
	{
		order.ProfileId = order.ProfileId.IsEmpty()
			? profileId
			: order.ProfileId;
		order.RefId = order.RefId.IsEmpty() ? request.RefId : order.RefId;
		order.Market = order.Market.IsEmpty() ? request.Market : order.Market;
		if (order.Side == PaxosSides.Unknown)
			order.Side = request.Side;
		if (order.Type == PaxosOrderTypes.Unknown)
			order.Type = request.Type;
		order.BaseAmount = order.BaseAmount.IsEmpty()
			? request.BaseAmount
			: order.BaseAmount;
		order.QuoteAmount = order.QuoteAmount.IsEmpty()
			? request.QuoteAmount
			: order.QuoteAmount;
		order.Price = order.Price.IsEmpty() ? request.Price : order.Price;
		order.StopPrice = order.StopPrice.IsEmpty()
			? request.StopPrice
			: order.StopPrice;
		if (order.TimeInForce == PaxosTimeInForces.Unknown)
			order.TimeInForce = request.TimeInForce;
		order.CreatedAt = order.CreatedAt.IsEmpty()
			? DateTime.UtcNow.ToPaxosTime()
			: order.CreatedAt;
	}

	private async ValueTask RegisterCustodyOperationAsync(
		OrderRegisterMessage message, PaxosOrderCondition condition,
		CancellationToken cancellationToken)
	{
		if (message.OrderType != OrderTypes.Conditional)
			throw new NotSupportedException(
				"Paxos custody operations use conditional orders.");
		if (message.Side != Sides.Sell)
			throw new NotSupportedException(
				"Paxos outgoing custody operations use the sell side.");
		if (message.TimeInForce is not null)
			throw new NotSupportedException(
				"Time-in-force is not applicable to Paxos custody operations.");
		ValidateIdentity(condition);
		var portfolio = await GetPortfolioAsync(message.PortfolioName,
			cancellationToken);
		var asset = message.SecurityId.SecurityCode.ThrowIfEmpty(
			nameof(message.SecurityId)).Trim();
		var amount = message.Volume.Abs();
		if (amount <= 0)
			throw new InvalidOperationException(
				"Paxos custody amount must be positive.");
		var refId = CreateRefId(message.TransactionId);

		switch (condition.Operation)
		{
			case PaxosOperations.CryptoWithdrawal:
			{
				if (condition.CryptoNetwork == PaxosCryptoNetworks.Unknown)
					throw new InvalidOperationException(
						"Paxos crypto withdrawal network must be specified.");
				var destinationAddress = condition.DestinationAddress;
				if (destinationAddress.IsEmpty())
					destinationAddress = condition.WithdrawInfo?.CryptoAddress;
				var request = new PaxosCryptoWithdrawalRequest
				{
					RefId = refId,
					ProfileId = portfolio.Profile.Id,
					IdentityId = condition.IdentityId,
					AccountId = condition.IdentityAccountId,
					DestinationAddress = destinationAddress.ThrowIfEmpty(
						nameof(condition.DestinationAddress)).Trim(),
					Asset = asset,
					CryptoNetwork = condition.CryptoNetwork,
					Amount = amount.ToPaxosAmount(),
					Memo = condition.Memo.IsEmpty()
						? condition.WithdrawInfo?.PaymentId
						: condition.Memo,
				};
				var transfer = await SubmitTransferAsync(refId, portfolio.Profile.Id,
					ct => RestClient.CreateCryptoWithdrawalAsync(request, ct),
					cancellationToken);
				await TrackAndSendTransferAsync(transfer, message, portfolio,
					condition.Operation, refId, cancellationToken);
				break;
			}
			case PaxosOperations.InternalTransfer:
			case PaxosOperations.PaxosTransfer:
			{
				var request = new PaxosProfileTransferRequest
				{
					RefId = refId,
					FromProfileId = portfolio.Profile.Id,
					ToProfileId = condition.DestinationProfileId.ThrowIfEmpty(
						nameof(condition.DestinationProfileId)),
					Amount = amount.ToPaxosAmount(),
					Asset = asset,
					FromIdentityId = condition.IdentityId,
					FromAccountId = condition.IdentityAccountId,
				};
				var transfer = await SubmitTransferAsync(refId, portfolio.Profile.Id,
					condition.Operation == PaxosOperations.InternalTransfer
						? ct => RestClient.CreateInternalTransferAsync(request, ct)
						: ct => RestClient.CreatePaxosTransferAsync(request, ct),
					cancellationToken);
				await TrackAndSendTransferAsync(transfer, message, portfolio,
					condition.Operation, refId, cancellationToken);
				break;
			}
			case PaxosOperations.StablecoinConversion:
			{
				var request = new PaxosStablecoinConversionRequest
				{
					ProfileId = portfolio.Profile.Id,
					Amount = amount.ToPaxosAmount(),
					SourceAsset = asset,
					TargetAsset = condition.DestinationAsset.ThrowIfEmpty(
						nameof(condition.DestinationAsset)),
					RefId = refId,
					IdentityId = condition.IdentityId,
					AccountId = condition.IdentityAccountId,
					RecipientProfileId = condition.DestinationProfileId,
				};
				var conversion = await SubmitConversionAsync(request,
					cancellationToken);
				if (conversion?.Id.IsEmpty() != false)
					throw new InvalidDataException(
						"Paxos returned an incomplete conversion response.");
				TrackConversion(conversion, message.TransactionId, portfolio.Name,
					refId);
				await SendConversionAsync(conversion, message.TransactionId, true,
					portfolio.Name, cancellationToken);
				break;
			}
			default:
				throw new ArgumentOutOfRangeException(nameof(condition),
					condition.Operation, null);
		}
	}

	private async ValueTask<PaxosTransfer> SubmitTransferAsync(string refId,
		string profileId, Func<CancellationToken, ValueTask<PaxosTransfer>> submit,
		CancellationToken cancellationToken)
	{
		try
		{
			return await submit(cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
			(error is HttpRequestException or TaskCanceledException ||
				error is PaxosApiException apiError && apiError.StatusCode ==
					HttpStatusCode.Conflict))
		{
			var recovered = await RestClient.GetTransfersAsync(profileId, refId,
				null, null, 10, 10, cancellationToken);
			var transfer = recovered.SingleOrDefault(item =>
				item?.RefId.EqualsIgnoreCase(refId) == true);
			if (transfer is not null)
				return transfer;
			ExceptionDispatchInfo.Capture(error).Throw();
			throw;
		}
	}

	private async ValueTask<PaxosStablecoinConversion> SubmitConversionAsync(
		PaxosStablecoinConversionRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			return await RestClient.CreateStablecoinConversionAsync(request,
				cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
			(error is HttpRequestException or TaskCanceledException ||
				error is PaxosApiException apiError && apiError.StatusCode ==
					HttpStatusCode.Conflict))
		{
			var recovered = await RestClient.GetStablecoinConversionsAsync(
				request.ProfileId, request.RefId, null, null, 10, 10,
				cancellationToken);
			var conversion = recovered.SingleOrDefault(item =>
				item?.RefId.EqualsIgnoreCase(request.RefId) == true);
			if (conversion is not null)
				return conversion;
			ExceptionDispatchInfo.Capture(error).Throw();
			throw;
		}
	}

	private async ValueTask TrackAndSendTransferAsync(PaxosTransfer transfer,
		OrderRegisterMessage message, PortfolioReference portfolio,
		PaxosOperations operation, string refId,
		CancellationToken cancellationToken)
	{
		if (transfer?.Id.IsEmpty() != false)
			throw new InvalidDataException(
				"Paxos returned an incomplete transfer response.");
		transfer.ProfileId = transfer.ProfileId.IsEmpty()
			? portfolio.Profile.Id
			: transfer.ProfileId;
		transfer.RefId = transfer.RefId.IsEmpty() ? refId : transfer.RefId;
		TrackTransfer(transfer, message.TransactionId, portfolio.Name, operation,
			refId);
		await SendTransferAsync(transfer, message.TransactionId, true,
			portfolio.Name, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage message, CancellationToken cancellationToken)
	{
		EnsureAuthenticated();
		var nativeId = message.OrderStringId;
		if (nativeId.IsEmpty())
			using (_sync.EnterScope())
				_nativeIds.TryGetValue(message.OriginalTransactionId, out nativeId);
		if (nativeId.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(
					message.OriginalTransactionId));
		var tracked = GetTrackedOperation(nativeId, null);
		if (tracked is null)
			throw new InvalidOperationException(
				$"Paxos operation '{nativeId}' is not tracked by this session.");
		switch (tracked.Kind)
		{
			case NativeOperationKinds.Order:
			{
				await RestClient.CancelOrderAsync(tracked.ProfileId, nativeId,
					cancellationToken);
				var order = await RestClient.GetOrderAsync(tracked.ProfileId,
					nativeId, cancellationToken);
				TrackOrder(order, tracked.TransactionId, tracked.PortfolioName);
				await SendOrderAsync(order, message.TransactionId, true,
					tracked.PortfolioName, cancellationToken);
				break;
			}
			case NativeOperationKinds.Conversion:
			{
				var conversion = await RestClient.CancelStablecoinConversionAsync(
					nativeId, cancellationToken);
				TrackConversion(conversion, tracked.TransactionId,
					tracked.PortfolioName, tracked.RefId);
				await SendConversionAsync(conversion, message.TransactionId, true,
					tracked.PortfolioName, cancellationToken);
				break;
			}
			case NativeOperationKinds.Transfer:
				throw new NotSupportedException(
					"Paxos transfers have no API cancellation endpoint.");
			default:
				throw new ArgumentOutOfRangeException(nameof(tracked), tracked.Kind,
					null);
		}
	}

	private static void ValidateIdentity(PaxosOrderCondition condition)
	{
		if (condition is null)
			return;
		if (condition.IdentityId.IsEmpty() !=
			condition.IdentityAccountId.IsEmpty())
			throw new InvalidOperationException(
				"Paxos IdentityId and IdentityAccountId must be supplied together.");
	}

	private static long ToUnixMilliseconds(DateTime value)
		=> checked((long)(value.EnsureUtc() - DateTime.UnixEpoch).TotalMilliseconds);
}
