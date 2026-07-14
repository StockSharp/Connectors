namespace StockSharp.DXtrade.Native;

using Ecng.ComponentModel;

abstract class BaseSocketClient : BaseLogReceiver, IConnection
{
	private class SubscriptionRequest
	{
		[JsonProperty("type")]
		public string Type { get; set; }

		[JsonProperty("requestId")]
		public string RequestId { get; set; }

		[JsonProperty("refRequestId")]
		public string RefRequestId { get; set; }

		[JsonProperty("timestamp")]
		public object Timestamp { get; set; }

		[JsonProperty("session")]
		public string Session { get; set; }

		[JsonProperty("payload")]
		public object Payload { get; set; }

		public SubscriptionRequest(string type, string requestId, string sessionToken, object payload = default)
		{
			Type = type;
			RequestId = requestId;
			Timestamp = DateTime.UtcNow.ToTimeStamp();
			Session = sessionToken;
			Payload = payload;
		}
	}

	protected class WebSocketMessage
	{
		[JsonProperty("type")]
		public string Type { get; set; }

		[JsonProperty("requestId")]
		public string RequestId { get; set; }

		[JsonProperty("inReplyTo")]
		public string InReplyTo { get; set; }

		[JsonProperty("refRequestId")]
		public string RefRequestId { get; set; }

		[JsonProperty("timestamp")]
		public DateTime Timestamp { get; set; }

		[JsonProperty("session")]
		public string Session { get; set; }

		[JsonProperty("payload")]
		public JToken Payload { get; set; }
	}

	protected static class MsgTypes
	{
		public const string Ping = nameof(Ping);
		public const string PingRequest = nameof(PingRequest);
		public const string Reject = nameof(Reject);
		public const string SessionClosed = nameof(SessionClosed);
		public const string MarketData = nameof(MarketData);
		public const string MarketDataSubscriptionClosed = nameof(MarketDataSubscriptionClosed);
		public const string AccountPortfolios = nameof(AccountPortfolios);
		public const string AccountPortfoliosSubscriptionClosed = nameof(AccountPortfoliosSubscriptionClosed);
		public const string AccountMetrics = nameof(AccountMetrics);
		public const string AccountEvents = nameof(AccountEvents);
		public const string CashTransfers = nameof(CashTransfers);
	}

	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly WebSocketClient _client;
	private readonly string _url;
	private readonly SecureString _sessionToken;

	protected BaseSocketClient(string url, int reconnectAttempts, SecureString sessionToken, WorkingTime workingTime)
	{
		_url = url.ThrowIfEmpty(nameof(url));
		_sessionToken = sessionToken.ThrowIfEmpty(nameof(url));

		_client = new(
			_url,
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
			async (c, msg1, t) =>
			{
				var msg = msg1.AsObject<WebSocketMessage>();

				if (msg.Type == MsgTypes.Reject)
					this.AddErrorLog(msg.Payload.ToString());
				else if (msg.Type == MsgTypes.SessionClosed)
					this.AddWarningLog(MsgTypes.SessionClosed);
				else if (msg.Type == MsgTypes.PingRequest)
					await SendPing(t);
				else
					await OnProcess(msg, t);
			},
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
		};
	}

	protected override void DisposeManaged()
	{
		_client.Dispose();
		base.DisposeManaged();
	}

	protected abstract ValueTask OnProcess(WebSocketMessage response, CancellationToken cancellationToken);

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);
		return _client.ConnectAsync(cancellationToken);
	}

	public void Disconnect()
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		_client.Disconnect();
	}

	protected ValueTask Subscribe(string type, long transactionId, object payload, CancellationToken cancellationToken)
		=> Send(transactionId, new SubscriptionRequest($"{type}SubscriptionRequest", transactionId.ToString(), GetToken(), payload), cancellationToken);

	protected ValueTask Unsubscribe(string type, long transactionId, long originTransId, CancellationToken cancellationToken)
		=> Send(-originTransId, new SubscriptionRequest($"{type}CloseSubscriptionRequest", transactionId.ToString(), GetToken()) { RefRequestId = originTransId.ToString() }, cancellationToken);

	public ValueTask SendPing(CancellationToken cancellationToken)
	{
		var request = new
		{
			type = MsgTypes.Ping,
			timestamp = DateTime.UtcNow.ToTimeStamp(),
			session = GetToken(),
		};

		return Send(0, request, cancellationToken);
	}

	private string GetToken() => _sessionToken.UnSecure();

	//private ValueTask Auth(string uri, object body, CancellationToken cancellationToken)
	//{
	//	var timestamp = (long)DateTime.UtcNow.ToUnix(false);
	//	var content = body.ToJson();
	//	var signature = _authenticator.Sign("WS", content, uri, timestamp);

	//	var request = new
	//	{
	//		type = "AuthRequest",
	//		requestId = Guid.NewGuid().ToString(),
	//		timestamp = DateTime.UtcNow.ToString("o"),
	//		principal = _authenticator.Key.UnSecure(),
	//		hash = signature,
	//		session = _sessionToken
	//	};

	//	return Send(request, cancellationToken);
	//}

	private ValueTask Send(long subId, object request, CancellationToken cancellationToken)
		=> _client.SendAsync(request, cancellationToken, subId);
}

class PublicSocketClient : BaseSocketClient
{
	public override string Name => nameof(PublicSocketClient);

	public event Func<Quote, CancellationToken, ValueTask> QuoteReceived;
	public event Func<Candle, CancellationToken, ValueTask> CandleReceived;

	public PublicSocketClient(string url, int reconnectAttempts, SecureString sessionToken, WorkingTime workingTime)
		: base(url, reconnectAttempts, sessionToken, workingTime)
	{
	}

	protected override async ValueTask OnProcess(WebSocketMessage response, CancellationToken cancellationToken)
	{
		switch (response.Type)
		{
			case MsgTypes.MarketData:
			{
				foreach (var evt in response.Payload["events"])
				{
					var type = (string)evt["type"];

					switch (type)
					{
						case MarketDataType.Quote:
							if (QuoteReceived is { } quoteHandler)
								await quoteHandler(evt.DeserializeObject<Quote>(), cancellationToken);
							break;
						case MarketDataType.Candle:
							if (CandleReceived is { } candleHandler)
								await candleHandler(evt.DeserializeObject<Candle>(), cancellationToken);
							break;
						default:
							this.AddWarningLog(LocalizedStrings.UnknownEvent, type);
							break;
					}
				}

				break;
			}
			case MsgTypes.MarketDataSubscriptionClosed:
				break;
			default:
				this.AddWarningLog(LocalizedStrings.UnknownEvent, response.Type);
				break;
		}
	}

	public ValueTask SubscribeQuotes(long transactionId, string account, string symbol, object fromTime, object toTime, CancellationToken cancellationToken)
	{
		var request = new MarketDataRequest
		{
			Account = account,
			Symbols = [symbol],
			EventTypes =
			[
				new MarketDataEventType
				{
					Type = MarketDataType.Quote,
					FromTime = fromTime,
					ToTime = toTime,
					Format = "COMPACT",
				}
			]
		};

		return Subscribe(MsgTypes.MarketData, transactionId, request, cancellationToken);
	}

	public ValueTask SubscribeCandles(long transactionId, string account, string symbol, string timeFrame, object fromTime, object toTime, CancellationToken cancellationToken)
	{
		var request = new MarketDataRequest
		{
			Account = account,
			Symbols = [symbol],
			EventTypes =
			[
				new MarketDataEventType
				{
					Type = MarketDataType.Candle,
					CandleType = timeFrame,
					FromTime = fromTime,
					ToTime = toTime,
					Format = "COMPACT",
				}
			]
		};

		return Subscribe(MsgTypes.MarketData, transactionId, request, cancellationToken);
	}

	public ValueTask Unsubscribe(long transactionId, long originTransId, CancellationToken cancellationToken)
		=> Unsubscribe(MsgTypes.MarketData, transactionId, originTransId, cancellationToken);
}

class PrivateSocketClient : BaseSocketClient
{
	public class AccountRequest
	{
		[JsonProperty("accounts")]
		public string[] Accounts { get; set; }

		[JsonProperty("requestType")]
		public string RequestType { get; set; }

		[JsonProperty("includeOffset")]
		public bool? IncludeOffset { get; set; }
	}

	public class AccountMetricsRequest : AccountRequest
	{
		[JsonProperty("includePositions")]
		public bool IncludePositions { get; set; }
	}

	public override string Name => nameof(PrivateSocketClient);

	public event Func<AccountPortfolio, CancellationToken, ValueTask> PortfolioReceived;
	public event Func<AccountMetrics, CancellationToken, ValueTask> MetricsReceived;
	public event Func<AccountEvent, CancellationToken, ValueTask> EventReceived;
	public event Func<CashTransfer, CancellationToken, ValueTask> CashTransferReceived;

	public PrivateSocketClient(string url, int reconnectAttempts, SecureString sessionToken, WorkingTime workingTime)
		: base(url, reconnectAttempts, sessionToken, workingTime)
	{
	}

	protected override async ValueTask OnProcess(WebSocketMessage message, CancellationToken cancellationToken)
	{
		T getValue<T>()
			=> message.Payload.DeserializeObject<T>();

		switch (message.Type)
		{
			case MsgTypes.AccountPortfolios:
				if (PortfolioReceived is { } portfolioHandler)
					await portfolioHandler(getValue<AccountPortfolio>(), cancellationToken);
				break;
			case MsgTypes.AccountMetrics:
				if (MetricsReceived is { } metricsHandler)
					await metricsHandler(getValue<AccountMetrics>(), cancellationToken);
				break;
			case MsgTypes.AccountEvents:
				if (EventReceived is { } eventHandler)
					await eventHandler(getValue<AccountEvent>(), cancellationToken);
				break;
			case MsgTypes.CashTransfers:
				if (CashTransferReceived is { } cashHandler)
					await cashHandler(getValue<CashTransfer>(), cancellationToken);
				break;
			case MsgTypes.AccountPortfoliosSubscriptionClosed:
				break;
			default:
				this.AddWarningLog(LocalizedStrings.UnknownEvent, message.Type);
				break;
		}
	}

	public ValueTask SubscribePortfolio(long transactionId, AccountRequest request, CancellationToken cancellationToken)
	   => Subscribe(MsgTypes.AccountPortfolios, transactionId, request, cancellationToken);

	public ValueTask UnsubscribePortfolio(long transactionId, long originTransId, CancellationToken cancellationToken)
		=> Unsubscribe(MsgTypes.AccountPortfolios, transactionId, originTransId, cancellationToken);

	//public ValueTask SubscribeMetrics(AccountMetricsRequest request, CancellationToken cancellationToken)
	//	=> Subscribe(MsgTypes.AccountMetrics, request, cancellationToken);

	//public ValueTask UnsubscribeMetrics(string refRequestId, CancellationToken cancellationToken)
	//	=> Unsubscribe(MsgTypes.AccountMetrics, refRequestId, cancellationToken);

	//public ValueTask SubscribeEvents(AccountRequest request, CancellationToken cancellationToken)
	//	=> Subscribe(MsgTypes.AccountEvents, request, cancellationToken);

	//public ValueTask UnsubscribeEvents(string refRequestId, CancellationToken cancellationToken)
	//	=> Unsubscribe(MsgTypes.AccountEvents, refRequestId, cancellationToken);

	//public ValueTask SubscribeCashTransfers(AccountRequest request, CancellationToken cancellationToken)
	//	=> Subscribe(MsgTypes.CashTransfers, request, cancellationToken);

	//public ValueTask UnsubscribeCashTransfers(string refRequestId, CancellationToken cancellationToken)
	//	=> Unsubscribe(MsgTypes.CashTransfers, refRequestId, cancellationToken);
}