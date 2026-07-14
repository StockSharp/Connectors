namespace StockSharp.ETrade;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Collections;
using Ecng.Common;

using Newtonsoft.Json.Linq;

using StockSharp.Localization;
using StockSharp.Messages;

partial class ETradeMessageAdapter
{
	private readonly SynchronizedDictionary<string, string> _accountKeys = new(StringComparer.OrdinalIgnoreCase);

	private static IEnumerable<JToken> ToItems(JToken token)
		=> token is null ? [] : token is JArray array ? array : [token];

	private async Task<IEnumerable<JToken>> GetAccounts(CancellationToken cancellationToken)
	{
		var response = await Get("v1/accounts/list.json", cancellationToken);
		var accounts = ToItems(response?["AccountListResponse"]?["Accounts"]?["Account"]).ToArray();
		foreach (var account in accounts)
		{
			var id = account.Value<string>("accountId");
			var key = account.Value<string>("accountIdKey");
			if (!id.IsEmpty() && !key.IsEmpty())
			{
				_accountKeys[id] = key;
				_accountKeys[key] = key;
			}
		}
		return accounts;
	}

	private async Task<string> ResolveAccount(string account, CancellationToken cancellationToken)
	{
		if (!account.IsEmpty() && _accountKeys.TryGetValue(account, out var key))
			return key;

		var accounts = await GetAccounts(cancellationToken);
		if (!account.IsEmpty() && _accountKeys.TryGetValue(account, out key))
			return key;

		return accounts.Select(a => a.Value<string>("accountIdKey")).FirstOrDefault(k => !k.IsEmpty())
			?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
	}

	internal static JObject CreatePreviewPayload(long transactionId, string securityCode, Sides side, OrderTypes orderType, decimal volume, decimal price, TimeInForce? timeInForce = null)
	{
		var order = new JObject
		{
			["allOrNone"] = false,
			["priceType"] = orderType == OrderTypes.Market ? "MARKET" : "LIMIT",
			["orderTerm"] = timeInForce == TimeInForce.PutInQueue ? "GOOD_UNTIL_CANCEL" : "GOOD_FOR_DAY",
			["marketSession"] = "REGULAR",
			["stopPrice"] = string.Empty,
			["limitPrice"] = orderType == OrderTypes.Market ? "0" : price.ToString(CultureInfo.InvariantCulture),
			["Instrument"] = new JArray(new JObject
			{
				["Product"] = new JObject { ["securityType"] = "EQ", ["symbol"] = securityCode },
				["orderAction"] = side == Sides.Buy ? "BUY" : "SELL",
				["quantityType"] = "QUANTITY",
				["quantity"] = volume.ToString(CultureInfo.InvariantCulture),
			}),
		};

		return new JObject
		{
			["PreviewOrderRequest"] = new JObject
			{
				["orderType"] = "EQ",
				["clientOrderId"] = transactionId.ToString(),
				["Order"] = new JArray(order),
			},
		};
	}

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage message, CancellationToken cancellationToken)
	{
		var accountKey = await ResolveAccount(message.PortfolioName, cancellationToken);
		var previewRequest = CreatePreviewPayload(message.TransactionId, message.SecurityId.SecurityCode, message.Side, message.OrderType ?? OrderTypes.Limit, message.Volume, message.Price, message.TimeInForce);
		var preview = await Send(HttpMethod.Post, $"v1/accounts/{accountKey.DataEscape()}/orders/preview.json", previewRequest, cancellationToken);
		var previewIds = preview?["PreviewOrderResponse"]?["PreviewIds"]?.DeepClone();
		if (previewIds is null)
			throw new InvalidOperationException("Preview identifier is missing in the E*TRADE response.");

		var placeRequest = (JObject)previewRequest["PreviewOrderRequest"].DeepClone();
		placeRequest["PreviewIds"] = previewIds;
		var result = await Send(HttpMethod.Post, $"v1/accounts/{accountKey.DataEscape()}/orders/place.json", new JObject { ["PlaceOrderRequest"] = placeRequest }, cancellationToken);
		var orderId = ToItems(result?["PlaceOrderResponse"]?["OrderIds"]).Select(i => i.Value<string>("orderId")).FirstOrDefault();

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = message.TransactionId,
			OrderStringId = orderId,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage message, CancellationToken cancellationToken)
	{
		var orderId = message.OrderId?.ToString() ?? message.OrderStringId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));

		var accountKey = await ResolveAccount(message.PortfolioName, cancellationToken);
		await Send(HttpMethod.Put, $"v1/accounts/{accountKey.DataEscape()}/orders/cancel.json", new JObject
		{
			["CancelOrderRequest"] = new JObject { ["orderId"] = orderId },
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		foreach (var account in await GetAccounts(cancellationToken))
		{
			var id = account.Value<string>("accountId");
			var key = account.Value<string>("accountIdKey");
			await SendOutMessageAsync(new PortfolioMessage { PortfolioName = id, BoardCode = BoardCodes.Nasdaq, OriginalTransactionId = message.TransactionId }, cancellationToken);

			var balance = (await Get($"v1/accounts/{key.DataEscape()}/balance.json?instType=BROKERAGE&realTimeNAV=true", cancellationToken))?["BalanceResponse"];
			var computed = balance?["Computed"] ?? balance?["computedBalance"];
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = id,
				SecurityId = SecurityId.Money,
				OriginalTransactionId = message.TransactionId,
				ServerTime = DateTime.UtcNow,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, computed?["cashBalance"]?.Value<decimal?>())
			.TryAdd(PositionChangeTypes.BuyOrdersMargin, computed?["marginBuyingPower"]?.Value<decimal?>() ?? computed?["cashBuyingPower"]?.Value<decimal?>())
			.TryAdd(PositionChangeTypes.Currency, CurrencyTypes.USD), cancellationToken);

			var portfolio = await Get($"v1/accounts/{key.DataEscape()}/portfolio.json?view=QUICK", cancellationToken);
			foreach (var position in portfolio.SelectTokens("$..Position").SelectMany(ToItems))
			{
				var product = position["Product"] ?? position["product"];
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = id,
					SecurityId = new() { SecurityCode = product?.Value<string>("symbol"), BoardCode = BoardCodes.Nasdaq },
					OriginalTransactionId = message.TransactionId,
					ServerTime = DateTime.UtcNow,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, position["quantity"]?.Value<decimal?>())
				.TryAdd(PositionChangeTypes.AveragePrice, position["pricePaid"]?.Value<decimal?>())
				.TryAdd(PositionChangeTypes.CurrentPrice, position["price"]?.Value<decimal?>()), cancellationToken);
			}
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		var accountKey = await ResolveAccount(message.PortfolioName, cancellationToken);
		var response = await Get($"v1/accounts/{accountKey.DataEscape()}/orders.json", cancellationToken);
		foreach (var order in response.SelectTokens("$..Order").SelectMany(ToItems))
		{
			var detail = ToItems(order["OrderDetail"]).FirstOrDefault();
			var instrument = ToItems(detail?["Instrument"]).FirstOrDefault();
			var status = detail?.Value<string>("status");
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				OriginalTransactionId = message.TransactionId,
				OrderStringId = order.Value<string>("orderId"),
				PortfolioName = message.PortfolioName,
				SecurityId = new() { SecurityCode = instrument?["Product"]?.Value<string>("symbol"), BoardCode = BoardCodes.Nasdaq },
				Side = instrument?.Value<string>("orderAction")?.StartsWith("BUY", StringComparison.OrdinalIgnoreCase) == true ? Sides.Buy : Sides.Sell,
				OrderType = detail?.Value<string>("priceType") == "MARKET" ? OrderTypes.Market : OrderTypes.Limit,
				OrderPrice = detail?.Value<decimal?>("limitPrice") ?? 0,
				OrderVolume = instrument?.Value<decimal?>("orderedQuantity") ?? instrument?.Value<decimal?>("quantity"),
				Balance = instrument?.Value<decimal?>("cancelQuantity"),
				OrderState = status switch
				{
					"OPEN" => OrderStates.Active,
					"EXECUTED" => OrderStates.Done,
					"CANCELLED" or "EXPIRED" => OrderStates.Done,
					"REJECTED" => OrderStates.Failed,
					_ => OrderStates.Pending,
				},
				ServerTime = DateTime.UtcNow,
			}, cancellationToken);
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}
}
