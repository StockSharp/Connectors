namespace StockSharp.Tradier.Native;

using System.Text.RegularExpressions;

using Ecng.ComponentModel;

using Newtonsoft.Json.Linq;

abstract class BaseSocketClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private readonly string _url;

	protected string SessionId { get; }

	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected BaseSocketClient(string url, string sessionId, int reconnectAttempts, WorkingTime workingTime)
	{
		_url = url.ThrowIfEmpty(nameof(url));
		SessionId = sessionId.ThrowIfEmpty(nameof(sessionId));

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
			OnProcess,
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

	public ValueTask Connect(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);
		return _client.ConnectAsync(cancellationToken);
	}

	public void Disconnect()
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		_client.Disconnect();
	}

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var matches = Regex.Matches(msg.AsString(), @"\{.*?\}", RegexOptions.Singleline);

		foreach (Match match in matches)
		{
			try
			{
				dynamic dyn = match.Value.FromJson(typeof(object));

				if (dyn.@event == "heartbeat")
				{
				}
				else
					await OnProcess((JObject)dyn, cancellationToken);
			}
			catch (Exception ex)
			{
				this.AddErrorLog(ex);
			}
		}
	}

	protected abstract ValueTask OnProcess(JObject obj, CancellationToken cancellationToken);

	protected ValueTask SendAsync(long subId, object request, CancellationToken cancellationToken)
	{
		if (request is null)
			throw new ArgumentNullException(nameof(request));

		return _client.SendAsync(request, cancellationToken, subId);
	}
}

class MarketDataClient : BaseSocketClient
{
	private static class Topics
	{
		public const string TradeEx = "tradeex";
		public const string Trade = "trade";
		public const string TimeSale = "timesale";
		public const string Quote = "quote";
		public const string Summary = "summary";
	}

	private readonly CachedSynchronizedSet<string> _symbols = [];

	public override string Name => nameof(Tradier) + "_" + nameof(MarketDataClient);

	public event Func<Quote, CancellationToken, ValueTask> QuoteReceived;
	public event Func<Trade, CancellationToken, ValueTask> TradeReceived;
	public event Func<Summary, CancellationToken, ValueTask> SummaryReceived;

	public MarketDataClient(string url, string sessionId, int reconnectAttempts, WorkingTime workingTime)
		: base(url, sessionId, reconnectAttempts, workingTime)
	{
	}

	protected override async ValueTask OnProcess(JObject obj, CancellationToken cancellationToken)
	{
		if (obj.Property("error") is JProperty error)
		{
			this.AddErrorLog((string)error.Value);
			return;
		}

		var type = (string)obj.Property("type").Value;

		switch (type)
		{
			case Topics.TradeEx:
			case Topics.Trade:
				if (TradeReceived is { } tradeHandler)
					await tradeHandler(obj.DeserializeObject<Trade>(), cancellationToken);
				break;

			case Topics.Quote:
			case Topics.TimeSale:
				if (QuoteReceived is { } quoteHandler)
					await quoteHandler(obj.DeserializeObject<Quote>(), cancellationToken);
				break;

			case Topics.Summary:
				if (SummaryReceived is { } summaryHandler)
					await summaryHandler(obj.DeserializeObject<Summary>(), cancellationToken);
				break;

			default:
				this.AddErrorLog(LocalizedStrings.UnknownEvent, type);
				break;
		}
	}

	private async ValueTask SendRequest(long subId, bool isSubscribe, string symbol, CancellationToken cancellationToken)
	{
		if (isSubscribe)
			_symbols.Add(symbol);
		else
			_symbols.Remove(symbol);

		Disconnect();
		await TimeSpan.FromSeconds(5).Delay(cancellationToken);
		await Connect(cancellationToken);

		if (_symbols.Count == 0)
			return;

		await SendAsync(subId, new
		{
			symbols = _symbols.Cache,
			sessionid = SessionId,
			linebreak = true,
		}, cancellationToken);
	}

	public ValueTask SubscribeTrades(long transId, string symbol, CancellationToken cancellationToken)
		=> SendRequest(transId, true, symbol, cancellationToken);

	public ValueTask SubscribeQuote(long transId, string symbol, CancellationToken cancellationToken)
		=> SendRequest(transId, true, symbol, cancellationToken);

	public ValueTask SubscribeSummary(long transId, string symbol, CancellationToken cancellationToken)
		=> SendRequest(transId, true, symbol, cancellationToken);

	public ValueTask UnSubscribeTrades(long originTransId, string symbol, CancellationToken cancellationToken)
		=> SendRequest(-originTransId, false, symbol, cancellationToken);

	public ValueTask UnSubscribeQuote(long originTransId, string symbol, CancellationToken cancellationToken)
		=> SendRequest(-originTransId, false, symbol, cancellationToken);

	public ValueTask UnSubscribeSummary(long originTransId, string symbol, CancellationToken cancellationToken)
		=> SendRequest(-originTransId, false, symbol, cancellationToken);
}

class AccountClient : BaseSocketClient
{
	public override string Name => nameof(Tradier) + "_" + nameof(AccountClient);

	public event Func<Order, CancellationToken, ValueTask> OrderReceived;

	public AccountClient(string url, string sessionId, int reconnectAttempts, WorkingTime workingTime)
		: base(url, sessionId, reconnectAttempts, workingTime)
	{
	}

	protected override async ValueTask OnProcess(JObject obj, CancellationToken cancellationToken)
	{
		if (OrderReceived is { } handler)
			await handler(obj.DeserializeObject<Order>(), cancellationToken);
	}

	public ValueTask Subscribe(long transId, CancellationToken cancellationToken)
		=> SendAsync(transId, new
		{
			events = new[] { "order" },
			sessionid = SessionId,
		}, cancellationToken);
}