namespace StockSharp.LMAX.Native;

using Ecng.ComponentModel;

class SocketClient : BaseLogReceiver
{
	private readonly string _marketDataWsUrl;
	private readonly string _accountWsUrl;
	private readonly Func<SecureString> _getMarketDataToken;
	private readonly Func<SecureString> _getAccountToken;
	private readonly SemaphoreSlim _subscriptionGate = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.DateTime,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		FloatParseHandling = FloatParseHandling.Decimal,
	};

	private WebSocketClient _marketDataClient;
	private WebSocketClient _accountClient;

	private readonly SynchronizedSet<string> _subscribedOrderBooks = [];
	private readonly SynchronizedSet<string> _subscribedTickers = [];
	private readonly SynchronizedSet<string> _subscribedTrades = [];

	public event Func<WsOrderBookMessage, CancellationToken, ValueTask>
		OrderBookReceived;
	public event Func<WsOrderBookMessage, CancellationToken, ValueTask>
		TickerReceived;
	public event Func<WsTradeEventMessage, CancellationToken, ValueTask>
		TradeReceived;

	public event Func<WsOrderMessage, CancellationToken, ValueTask>
		OrderReceived;
	public event Func<WsExecutionMessage, CancellationToken, ValueTask>
		ExecutionReceived;
	public event Func<WsPositionMessage, CancellationToken, ValueTask>
		PositionReceived;
	public event Func<WsWalletMessage, CancellationToken, ValueTask>
		WalletReceived;

	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public SocketClient(
		string marketDataWsUrl,
		string accountWsUrl,
		Func<SecureString> getMarketDataToken,
		Func<SecureString> getAccountToken,
		WorkingTime workingTime)
	{
		_marketDataWsUrl = marketDataWsUrl
			?? throw new ArgumentNullException(nameof(marketDataWsUrl));
		_accountWsUrl = accountWsUrl
			?? throw new ArgumentNullException(nameof(accountWsUrl));
		_getMarketDataToken = getMarketDataToken
			?? throw new ArgumentNullException(nameof(getMarketDataToken));
		_getAccountToken = getAccountToken
			?? throw new ArgumentNullException(nameof(getAccountToken));
		workingTime = workingTime
			?? throw new ArgumentNullException(nameof(workingTime));

		_marketDataClient = new WebSocketClient(
			_marketDataWsUrl,
			OnMarketDataStateChanged,
			OnError,
			ProcessMarketDataMessage,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = -1,
			WorkingTime = workingTime,
		};

		_accountClient = new WebSocketClient(
			_accountWsUrl,
			OnAccountStateChanged,
			OnError,
			ProcessAccountMessage,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = -1,
			WorkingTime = workingTime,
		};

		_marketDataClient.InitAsync += (ws, _) =>
		{
			SetAuthorizationHeader(ws, _getMarketDataToken);
			return default;
		};
		_accountClient.InitAsync += (ws, _) =>
		{
			SetAuthorizationHeader(ws, _getAccountToken);
			return default;
		};
		_marketDataClient.PostConnect += OnMarketDataPostConnect;
		_accountClient.PostConnect += OnAccountPostConnect;
	}

	public override string Name => nameof(LMAX) + "_" + nameof(SocketClient);

	public bool IsConnected =>
		_marketDataClient?.IsConnected == true &&
		_accountClient?.IsConnected == true;

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		await _marketDataClient.ConnectAsync(cancellationToken);
		this.AddInfoLog("Connected to market data WebSocket");

		await _accountClient.ConnectAsync(cancellationToken);
		this.AddInfoLog("Connected to account WebSocket");
	}

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		if (_marketDataClient != null)
			await _marketDataClient.DisconnectAsync(cancellationToken);

		if (_accountClient != null)
			await _accountClient.DisconnectAsync(cancellationToken);

		_subscribedOrderBooks.Clear();
		_subscribedTickers.Clear();
		_subscribedTrades.Clear();

		this.AddInfoLog("Disconnected");
	}

	public ValueTask SubscribeOrderBookAsync(
		string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeBidOfferSubscriptionAsync(
			instrumentId,
			_subscribedOrderBooks,
			_subscribedTickers,
			true,
			cancellationToken);

	public ValueTask UnsubscribeOrderBookAsync(
		string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeBidOfferSubscriptionAsync(
			instrumentId,
			_subscribedOrderBooks,
			_subscribedTickers,
			false,
			cancellationToken);

	public ValueTask SubscribeTickerAsync(
		string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeBidOfferSubscriptionAsync(
			instrumentId,
			_subscribedTickers,
			_subscribedOrderBooks,
			true,
			cancellationToken);

	public ValueTask UnsubscribeTickerAsync(
		string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeBidOfferSubscriptionAsync(
			instrumentId,
			_subscribedTickers,
			_subscribedOrderBooks,
			false,
			cancellationToken);

	public ValueTask SubscribeTradesAsync(
		string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeMarketSubscriptionAsync(
			instrumentId,
			_subscribedTrades,
			WsChannels.Trade,
			true,
			cancellationToken);

	public ValueTask UnsubscribeTradesAsync(
		string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeMarketSubscriptionAsync(
			instrumentId,
			_subscribedTrades,
			WsChannels.Trade,
			false,
			cancellationToken);

	private ValueTask OnError(
		Exception error,
		CancellationToken cancellationToken)
	{
		this.AddErrorLog(error);

		if (Error is { } handler)
			return handler.InvokeAsync(error, cancellationToken);

		return default;
	}

	private ValueTask OnMarketDataStateChanged(
		ConnectionStates state,
		CancellationToken cancellationToken)
	{
		this.AddInfoLog("MarketData WebSocket state: {0}", state);

		if (StateChanged is { } handler)
			return handler.InvokeAsync(state, cancellationToken);

		return default;
	}

	private ValueTask OnAccountStateChanged(
		ConnectionStates state,
		CancellationToken cancellationToken)
	{
		this.AddInfoLog("Account WebSocket state: {0}", state);

		if (StateChanged is { } handler)
			return handler.InvokeAsync(state, cancellationToken);

		return default;
	}

	private async ValueTask OnMarketDataPostConnect(
		bool reconnect,
		CancellationToken cancellationToken)
	{
		if (!reconnect)
			return;

		await _subscriptionGate.WaitAsync(cancellationToken);
		try
		{
			var bidOffer = _subscribedOrderBooks
				.Concat(_subscribedTickers)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
			var channels = new List<WsMarketChannel>();

			if (bidOffer.Length > 0)
			{
				channels.Add(new()
				{
					Name = WsChannels.BidOffer,
					Instruments = bidOffer,
				});
			}

			if (_subscribedTrades.Count > 0)
			{
				channels.Add(new()
				{
					Name = WsChannels.Trade,
					Instruments = [.. _subscribedTrades],
				});
			}

			if (channels.Count > 0)
			{
				await _marketDataClient.SendAsync(
					new WsMarketSubscribeRequest
					{
						Channels = [.. channels],
					},
					cancellationToken);
			}
		}
		finally
		{
			_subscriptionGate.Release();
		}
	}

	private ValueTask OnAccountPostConnect(
		bool reconnect,
		CancellationToken cancellationToken)
	{
		_ = reconnect;

		return _accountClient.SendAsync(
			new WsAccountSubscribeRequest
			{
				Channels =
				[
					WsChannels.InstrumentPositions,
					WsChannels.WalletBalances,
					WsChannels.WorkingOrders,
					WsChannels.Trades,
				],
			},
			cancellationToken);
	}

	private async ValueTask ChangeBidOfferSubscriptionAsync(
		string instrumentId,
		SynchronizedSet<string> target,
		SynchronizedSet<string> other,
		bool subscribe,
		CancellationToken cancellationToken)
	{
		instrumentId = instrumentId.ThrowIfEmpty(nameof(instrumentId));
		await _subscriptionGate.WaitAsync(cancellationToken);
		try
		{
			if (subscribe)
			{
				if (!target.TryAdd(instrumentId))
					return;
				if (other.Contains(instrumentId))
					return;

				try
				{
					await SendMarketSubscriptionAsync(
						WsChannels.BidOffer,
						instrumentId,
						true,
						cancellationToken);
				}
				catch
				{
					target.Remove(instrumentId);
					throw;
				}
			}
			else
			{
				if (!target.Remove(instrumentId))
					return;
				if (other.Contains(instrumentId))
					return;

				try
				{
					await SendMarketSubscriptionAsync(
						WsChannels.BidOffer,
						instrumentId,
						false,
						cancellationToken);
				}
				catch
				{
					target.Add(instrumentId);
					throw;
				}
			}
		}
		finally
		{
			_subscriptionGate.Release();
		}
	}

	private async ValueTask ChangeMarketSubscriptionAsync(
		string instrumentId,
		SynchronizedSet<string> target,
		string channel,
		bool subscribe,
		CancellationToken cancellationToken)
	{
		instrumentId = instrumentId.ThrowIfEmpty(nameof(instrumentId));
		await _subscriptionGate.WaitAsync(cancellationToken);
		try
		{
			if (subscribe)
			{
				if (!target.TryAdd(instrumentId))
					return;

				try
				{
					await SendMarketSubscriptionAsync(
						channel,
						instrumentId,
						true,
						cancellationToken);
				}
				catch
				{
					target.Remove(instrumentId);
					throw;
				}
			}
			else
			{
				if (!target.Remove(instrumentId))
					return;

				try
				{
					await SendMarketSubscriptionAsync(
						channel,
						instrumentId,
						false,
						cancellationToken);
				}
				catch
				{
					target.Add(instrumentId);
					throw;
				}
			}
		}
		finally
		{
			_subscriptionGate.Release();
		}
	}

	private ValueTask SendMarketSubscriptionAsync(
		string channel,
		string instrumentId,
		bool subscribe,
		CancellationToken cancellationToken)
	{
		var channels = new[]
		{
			new WsMarketChannel
			{
				Name = channel,
				Instruments = [instrumentId],
			},
		};

		return subscribe
			? _marketDataClient.SendAsync(
				new WsMarketSubscribeRequest { Channels = channels },
				cancellationToken)
			: _marketDataClient.SendAsync(
				new WsMarketUnsubscribeRequest { Channels = channels },
				cancellationToken);
	}

	private async ValueTask ProcessMarketDataMessage(
		WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		try
		{
			var payload = GetPayload(message);
			var header = Deserialize<WsMessage>(payload);

			switch (header.Type)
			{
				case WsMessageTypes.BidOfferSnapshot:
					var orderBook = Deserialize<WsOrderBookMessage>(payload);
					if (_subscribedOrderBooks.Contains(
						orderBook.InstrumentId) &&
						OrderBookReceived is { } orderBookHandler)
					{
						await orderBookHandler.InvokeAsync(orderBook, cancellationToken);
					}
					if (_subscribedTickers.Contains(
						orderBook.InstrumentId) &&
						TickerReceived is { } tickerHandler)
					{
						await tickerHandler.InvokeAsync(orderBook, cancellationToken);
					}
					break;

				case WsMessageTypes.TradeEvent:
					var trade = Deserialize<WsTradeEventMessage>(payload);
					if (_subscribedTrades.Contains(trade.InstrumentId) &&
						TradeReceived is { } tradeHandler)
					{
						await tradeHandler.InvokeAsync(
							trade,
							cancellationToken);
					}
					break;

				case WsMessageTypes.Subscriptions:
					this.AddInfoLog(
						"MarketData subscriptions were updated.");
					break;

				case WsMessageTypes.SubscriptionRejection:
					var rejection =
						Deserialize<WsSubscriptionRejectionMessage>(payload);
					this.AddErrorLog(
						"MarketData subscription rejected: {0} - {1}",
						rejection.Reason,
						rejection.Message);
					break;

				case WsMessageTypes.Error:
					LogWebSocketError(
						"MarketData",
						Deserialize<WsErrorMessage>(payload));
					break;

				default:
					this.AddWarningLog(
						"Unsupported MarketData WebSocket message type '{0}'.",
						header.Type);
					break;
			}
		}
		catch (Exception error)
		{
			this.AddErrorLog(
				"Error processing market data message: {0}",
				error);
		}
	}

	private async ValueTask ProcessAccountMessage(
		WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		try
		{
			var payload = GetPayload(message);
			var header = Deserialize<WsMessage>(payload);

			switch (header.Type)
			{
				case WsMessageTypes.WorkingOrder:
					if (OrderReceived is { } orderHandler)
					{
						await orderHandler.InvokeAsync(
							Deserialize<WsOrderMessage>(payload),
							cancellationToken);
					}
					break;

				case WsMessageTypes.Trade:
					if (ExecutionReceived is { } executionHandler)
					{
						await executionHandler.InvokeAsync(
							Deserialize<WsExecutionMessage>(payload),
							cancellationToken);
					}
					break;

				case WsMessageTypes.InstrumentPosition:
					if (PositionReceived is { } positionHandler)
					{
						await positionHandler.InvokeAsync(
							Deserialize<WsPositionMessage>(payload),
							cancellationToken);
					}
					break;

				case WsMessageTypes.WalletBalances:
					if (WalletReceived is { } walletHandler)
					{
						await walletHandler.InvokeAsync(
							Deserialize<WsWalletMessage>(payload),
							cancellationToken);
					}
					break;

				case WsMessageTypes.Subscriptions:
					this.AddInfoLog(
						"Account subscriptions were updated.");
					break;

				case WsMessageTypes.Error:
					LogWebSocketError(
						"Account",
						Deserialize<WsErrorMessage>(payload));
					break;

				default:
					this.AddWarningLog(
						"Unsupported Account WebSocket message type '{0}'.",
						header.Type);
					break;
			}
		}
		catch (Exception error)
		{
			this.AddErrorLog(
				"Error processing account message: {0}",
				error);
		}
	}

	private static void SetAuthorizationHeader(
		ClientWebSocket socket,
		Func<SecureString> getToken)
	{
		var token = getToken();

		if (token.IsEmpty())
		{
			throw new InvalidOperationException(
				"LMAX WebSocket authentication token is unavailable.");
		}

		socket.Options.SetRequestHeader(
			"Authorization",
			$"Bearer {token.UnSecure()}");
	}

	private static string GetPayload(WebSocketMessage message)
		=> message.AsString()?.Trim().ThrowIfEmpty(nameof(message));

	private TMessage Deserialize<TMessage>(string payload)
		where TMessage : class
		=> JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings)
			?? throw new InvalidDataException(
				$"LMAX returned an empty {typeof(TMessage).Name} payload.");

	private void LogWebSocketError(string source, WsErrorMessage error)
	{
		this.AddErrorLog(
			"{0} error: {1} - {2}",
			source,
			error.ErrorCode,
			error.ErrorMessage);
	}

	protected override void DisposeManaged()
	{
		_marketDataClient?.Dispose();
		_accountClient?.Dispose();
		_subscriptionGate.Dispose();
		base.DisposeManaged();
	}
}
