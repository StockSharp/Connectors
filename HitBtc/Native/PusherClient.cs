namespace StockSharp.HitBtc.Native;

using System.Security.Cryptography;

class PusherClient : BaseLogReceiver
{
	// to get readable name after obfuscation
	public override string Name => nameof(HitBtc) + "_" + nameof(PusherClient);

	public event Func<Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<long, string, IEnumerable<Trade>, CancellationToken, ValueTask> NewTrades;
	public event Func<string, string, Ohlc, CancellationToken, ValueTask> NewCandle;
	public event Func<OrderBook, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<long, IEnumerable<Symbol>, CancellationToken, ValueTask> NewSymbols;
	public event Func<long, Order, CancellationToken, ValueTask> OrderChanged;
	public event Func<long, Order[], CancellationToken, ValueTask> NewOrders;
	public event Func<long, Balance[], CancellationToken, ValueTask> BalanceChanged;
	public event Func<long, string, CancellationToken, ValueTask> OrderError;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;
	//public event Action<string> TradesSubscribed;
	//public event Action<string> OrderBooksSubscribed;

	private readonly WebSocketClient _client;

	private enum Requests
	{
		Symbols,
		Trades,
		PlaceOrder,
		CancelOrder,
		ReplaceOrder,
		ActiveOrders,
		Balance,
	}

	private readonly SynchronizedDictionary<long, Requests> _requests = new();

	private readonly SecureString _key;
	//private readonly SecureString _secret;
	private readonly HashAlgorithm _hasher;

	public PusherClient(SecureString key, SecureString secret, WorkingTime workingTime)
	{
		_key = key;
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().ASCII());

		_client = new(
			"wss://api.hitbtc.com/api/2/ws",
			(state, token) =>
			{
				if (StateChanged is { } handler)
					return handler(state, token);
				return default;
			},
			(error, token) =>
			{
				this.AddErrorLog(error);
				if (Error is { } handler)
					return handler(error, token);
				return default;
			},
			OnProcess,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
		};
	}

	protected override void DisposeManaged()
	{
		_client.Dispose();
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		_requests.Clear();

		this.AddInfoLog(LocalizedStrings.Connecting);
		await _client.ConnectAsync(cancellationToken);

		if (_hasher != null)
			await SendLogin(cancellationToken);
	}

	public void Disconnect()
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		_client.Disconnect();
	}

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var obj = msg.AsObject();
		var error = obj.error;

		if (error != null)
		{
			var errorMsg = (string)error.message;

			if (obj.id != null)
			{
				var id = (long)obj.id;

				if (_requests.TryGetValue(id, out var request))
				{
					switch (request)
					{
						case Requests.PlaceOrder:
						case Requests.CancelOrder:
						case Requests.ReplaceOrder:
							if (OrderError is { } orderErrorHandler)
								await orderErrorHandler(id, errorMsg, cancellationToken);
							return;
					}
				}
			}

			if (Error is { } errorHandler)
				await errorHandler(new InvalidOperationException(errorMsg), cancellationToken);
			return;
		}

		if (obj.method == null && obj.channel == null)
		{
			if (obj.id == null)
			{
				return;
			}

			var id = (long)obj.id;

			var request = _requests.TryGetValue2(id);

			switch (request)
			{
				case Requests.Symbols:
					if (NewSymbols is { } symbolsHandler)
						await symbolsHandler(id, ((JArray)obj.result).DeserializeObject<Symbol[]>(), cancellationToken);
					break;
				case Requests.Trades:
					if (NewTrades is { } tradesHandler)
						await tradesHandler(id, (string)obj.result.symbol, ((JArray)obj.result.data).Select(i => i.DeserializeObject<Trade>()).ToArray(), cancellationToken);
					break;
				case Requests.PlaceOrder:
				case Requests.CancelOrder:
				case Requests.ReplaceOrder:
				{
					if (obj.error == null)
					{
						if (OrderChanged is { } orderChangedHandler)
							await orderChangedHandler(id, ((JToken)obj.result).DeserializeObject<Order>(), cancellationToken);
					}
					else
					{
						if (OrderError is { } orderErrorHandler2)
							await orderErrorHandler2(id, (string)obj.error.message, cancellationToken);
					}

					break;
				}
				case Requests.ActiveOrders:
				{
					if (obj.error == null)
					{
						if (NewOrders is { } newOrdersHandler)
							await newOrdersHandler(id, ((JArray)obj.result).DeserializeObject<Order[]>(), cancellationToken);
					}
					else
					{
						if (Error is { } errorHandler2)
							await errorHandler2(new InvalidOperationException((string)obj.error.message), cancellationToken);
					}

					break;
				}
				case Requests.Balance:
				{
					if (obj.error == null)
					{
						if (BalanceChanged is { } balanceHandler)
							await balanceHandler(id, ((JArray)obj.result).DeserializeObject<Balance[]>(), cancellationToken);
					}
					else
					{
						if (Error is { } errorHandler3)
							await errorHandler3(new InvalidOperationException((string)obj.error.message), cancellationToken);
					}

					break;
				}
				case null:
					//this.AddErrorLog(LocalizedStrings.UnknownEvent, id);
					break;
				default:
					throw new ArgumentOutOfRangeException(request.Value.ToString());
			}
		}
		else
		{
			var method = (string)(obj.method ?? obj.channel);

			switch (method)
			{
				case "ticker":
					if (TickerChanged is { } tickerHandler)
						await tickerHandler(((JToken)(obj.@params ?? obj.data)).DeserializeObject<Ticker>(), cancellationToken);
					break;

				case "snapshotOrderbook":
				case "updateOrderbook":
					if (OrderBookChanged is { } bookHandler)
						await bookHandler(((JToken)obj.@params).DeserializeObject<OrderBook>(), cancellationToken);
					break;

				case "snapshotTrades":
				case "updateTrades":
					if (NewTrades is { } tradesHandler2)
						await tradesHandler2(0, (string)obj.@params.symbol, ((JArray)obj.@params.data).Select(i => i.DeserializeObject<Trade>()).ToArray(), cancellationToken);
					break;

				case "snapshotCandles":
				case "updateCandles":
				{
					var symbol = (string)obj.@params.symbol;
					var period = (string)obj.@params.period;

					foreach (var item in obj.@params.data)
					{
						if (NewCandle is { } candleHandler)
							await candleHandler(symbol, period, ((JToken)item).DeserializeObject<Ohlc>(), cancellationToken);
					}

					break;
				}

				case "activeOrders":
					//NewOrders?.Invoke(id, ((JArray)obj.result).DeserializeObject<Order[]>());
					break;

				case "report":
					var order = ((JToken)obj.@params).DeserializeObject<Order>();
					if (OrderChanged is { } reportHandler)
						await reportHandler(0, order, cancellationToken);
					break;

				default:
					this.AddErrorLog(LocalizedStrings.UnknownEvent, method);
					break;
			}
		}
	}

	public ValueTask RequestSymbolsAsync(long transactionId, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, Requests.Symbols);

		return ProcessAsync("getSymbols", new { }, transactionId, cancellationToken);
	}

	public ValueTask RequestTradesAsync(string symbol, string sort, string by, long? from, long? till, long? limit, long? offset, long transactionId, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, Requests.Trades);

		return ProcessAsync("getTrades", new
		{
			symbol,
			sort,
			by,
			from,
			till,
			limit,
			offset,
		}, transactionId, cancellationToken);
	}

	public ValueTask SubscribeTickerAsync(string symbol, long transactionId, CancellationToken cancellationToken)
	{
		return ProcessAsync("subscribeTicker", new { symbol }, transactionId, cancellationToken);
	}

	public ValueTask UnSubscribeTickerAsync(string symbol, long transactionId, CancellationToken cancellationToken)
	{
		return ProcessAsync("unsubscribeTicker", new { symbol }, transactionId, cancellationToken);
	}

	public ValueTask SubscribeTradesAsync(string symbol, long transactionId, CancellationToken cancellationToken)
	{
		return ProcessAsync("subscribeTrades", new { symbol }, transactionId, cancellationToken);
	}

	public ValueTask UnSubscribeTradesAsync(string symbol, long transactionId, CancellationToken cancellationToken)
	{
		return ProcessAsync("unsubscribeTrades", new { symbol }, transactionId, cancellationToken);
	}

	public ValueTask SubscribeOrderBookAsync(string symbol, long transactionId, CancellationToken cancellationToken)
	{
		return ProcessAsync("subscribeOrderbook", new { symbol }, transactionId, cancellationToken);
	}

	public ValueTask UnSubscribeOrderBookAsync(string symbol, long transactionId, CancellationToken cancellationToken)
	{
		return ProcessAsync("unsubscribeOrderbook", new { symbol }, transactionId, cancellationToken);
	}

	public ValueTask SubscribeCandlesAsync(string symbol, string period, long transactionId, CancellationToken cancellationToken)
	{
		return ProcessAsync("subscribeCandles", new { symbol, period }, transactionId, cancellationToken);
	}

	public ValueTask UnSubscribeCandlesAsync(string symbol, string period, long transactionId, CancellationToken cancellationToken)
	{
		return ProcessAsync("unsubscribeCandles", new { symbol, period }, transactionId, cancellationToken);
	}

	private ValueTask ProcessAsync(string method, dynamic @params, long id, CancellationToken cancellationToken)
	{
		if (method.IsEmpty())
			throw new ArgumentNullException(nameof(method));

		if (@params == null)
			throw new ArgumentNullException(nameof(@params));

		return _client.SendAsync(new
		{
			method,
			@params,
			id,
		}, cancellationToken);
	}

	private ValueTask SendLogin(CancellationToken cancellationToken)
	{
		var nonce = TypeHelper.GenerateSalt(16).Base64();

		return _client.SendAsync(new
		{
			method = "login",
			@params = new
			{
				algo = "HS256",
				pKey = _key.UnSecure(),
				nonce,
				signature = _hasher.ComputeHash(nonce.UTF8()).Digest().ToLowerInvariant(),
			},
		}, cancellationToken);
	}

	public ValueTask PlaceOrderAsync(string clientOrderId, string symbol, string side, string type, decimal? price, decimal quantity, string timeInForce, decimal? stopPrice, DateTime? expireTime, long transactionId, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, Requests.PlaceOrder);

		return ProcessAsync("newOrder", new
		{
			clientOrderId,
			symbol,
			side,
			type,
			price,
			quantity,
			timeInForce,
			stopPrice,
			expireTime,
		}, transactionId, cancellationToken);
	}

	public ValueTask CancelOrderAsync(string clientOrderId, long transactionId, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, Requests.CancelOrder);

		return ProcessAsync("cancelOrder", new { clientOrderId }, transactionId, cancellationToken);
	}

	public ValueTask ReplaceOrderAsync(string clientOrderId, string requestClientId, decimal price, decimal? quantity, long transactionId, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, Requests.ReplaceOrder);

		return ProcessAsync("cancelReplaceOrder", new
		{
			clientOrderId,
			requestClientId,
			price,
			quantity,
		}, transactionId, cancellationToken);
	}

	public ValueTask RequestActiveOrdersAsync(long transactionId, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, Requests.ActiveOrders);

		return ProcessAsync("getOrders", new { }, transactionId, cancellationToken);
	}

	public ValueTask RequestBalanceAsync(long transactionId, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, Requests.Balance);

		return ProcessAsync("getTradingBalance", new { }, transactionId, cancellationToken);
	}

	public void SubscribeReports()
	{
		_client.Send(new
		{
			method = "subscribeReports",
			@params = new { },
		});
	}

	public async Task<string> WithdrawAsync(string currency, decimal volume, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var url = "https://api.hitbtc.com/api/2/account/crypto/withdraw".To<Uri>();

		var request = new RestRequest((string)null, Method.Post);

		request
			.AddParameter("amount", volume.To<string>())
			.AddParameter("currency", currency)
			.AddParameter("address", info.CryptoAddress);

		if (!info.PaymentId.IsEmpty())
			request.AddParameter("paymentId", info.PaymentId);

		if (info.ChargeFee != null)
			request.AddParameter("networkFee", info.ChargeFee.Value);

		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		return (string)obj.id;
	}
}