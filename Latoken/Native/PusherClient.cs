namespace StockSharp.LATOKEN.Native;

using System.Net.WebSockets;
using System.Text;

/// <summary>
/// Market and account data feed of LATOKEN.
/// </summary>
/// <remarks>
/// The venue speaks STOMP over the WebSocket connection: every payload is a text frame made of a
/// command, a block of headers and a body separated from them by an empty line and terminated by
/// the NULL character. A destination such as <c>/v1/book/{base}/{quote}</c> names both the channel
/// and the instrument, so the instrument of a message is taken from the destination header.
/// </remarks>
class PusherClient : BaseLogReceiver
{
	// to get readable name after obfuscation
	public override string Name => nameof(LATOKEN) + "_" + nameof(PusherClient);

	public event Func<Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<string, string, OrderBook, bool, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<Trade, CancellationToken, ValueTask> NewTrade;
	public event Func<Order, CancellationToken, ValueTask> OrderChanged;
	public event Func<Balance, CancellationToken, ValueTask> BalanceChanged;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private const char _frameEnd = '\0';

	private readonly WebSocketClient _client;
	private readonly Authenticator _authenticator;
	private readonly SynchronizedDictionary<string, long> _subscriptions = new(StringComparer.InvariantCultureIgnoreCase);
	private long _nextSubscriptionId;

	public PusherClient(string endpoint, Authenticator authenticator, WorkingTime workingTime)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

		_client = new(
			endpoint.ThrowIfEmpty(nameof(endpoint)),
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

		_client.PostConnect += OnPostConnect;

		if (_authenticator.CanSign)
			_client.InitAsync += OnInit;
	}

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;

		if (_authenticator.CanSign)
			_client.InitAsync -= OnInit;

		_client.Dispose();
		base.DisposeManaged();
	}

	private ValueTask OnInit(ClientWebSocket s, CancellationToken _)
	{
		var signData = ((long)TimeHelper.UnixNowS).ToString();

		s.Options.SetRequestHeader("X-LA-APIKEY", _authenticator.Key.UnSecure());
		s.Options.SetRequestHeader("X-LA-DIGEST", Authenticator.HashAlgo);
		s.Options.SetRequestHeader("X-LA-SIGNATURE", _authenticator.MakeSign(signData));
		s.Options.SetRequestHeader("X-LA-SIGDATA", signData);
		return default;
	}

	// the session has to be opened with a STOMP handshake before any destination can be subscribed,
	// and it has to be repeated after every reconnection
	private ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		_ = reconnect;

		return _client.SendAsync(CreateFrame("CONNECT", new()
		{
			{ "accept-version", "1.1" },
			{ "heart-beat", "0,0" },
		}), cancellationToken);
	}

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);
		return _client.ConnectAsync(cancellationToken);
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		_subscriptions.Clear();
		return _client.DisconnectAsync(cancellationToken);
	}

	private static class Channels
	{
		public const string Ticker = "ticker";
		public const string Book = "book";
		public const string Trade = "trade";
		public const string Order = "order";
		public const string Account = "account";
	}

	public ValueTask SubscribeTicker(string code, string board, CancellationToken cancellationToken)
		=> SubscribeAsync(CreateDestination(Channels.Ticker, code, board), cancellationToken);

	public ValueTask UnSubscribeTicker(string code, string board, CancellationToken cancellationToken)
		=> UnsubscribeAsync(CreateDestination(Channels.Ticker, code, board), cancellationToken);

	public ValueTask SubscribeTrades(string code, string board, CancellationToken cancellationToken)
		=> SubscribeAsync(CreateDestination(Channels.Trade, code, board), cancellationToken);

	public ValueTask UnSubscribeTrades(string code, string board, CancellationToken cancellationToken)
		=> UnsubscribeAsync(CreateDestination(Channels.Trade, code, board), cancellationToken);

	public ValueTask SubscribeOrderBook(string code, string board, CancellationToken cancellationToken)
		=> SubscribeAsync(CreateDestination(Channels.Book, code, board), cancellationToken);

	public ValueTask UnSubscribeOrderBook(string code, string board, CancellationToken cancellationToken)
		=> UnsubscribeAsync(CreateDestination(Channels.Book, code, board), cancellationToken);

	// the private destinations are addressed by the LATOKEN user id, which the adapter does not
	// know yet, so the account streams stay unimplemented
	public ValueTask SubscribeOrders(CancellationToken cancellationToken)
	{
		_ = cancellationToken;
		return default;
	}

	public ValueTask UnSubscribeOrders(CancellationToken cancellationToken)
	{
		_ = cancellationToken;
		return default;
	}

	public ValueTask SubscribeAccounts(CancellationToken cancellationToken)
	{
		_ = cancellationToken;
		return default;
	}

	public ValueTask UnSubscribeAccounts(CancellationToken cancellationToken)
	{
		_ = cancellationToken;
		return default;
	}

	private static string CreateDestination(string channel, string baseCurrencyId, string quoteCurrencyId)
		=> $"/v1/{channel}/{baseCurrencyId.ThrowIfEmpty(nameof(baseCurrencyId))}/{quoteCurrencyId.ThrowIfEmpty(nameof(quoteCurrencyId))}";

	private ValueTask SubscribeAsync(string destination, CancellationToken cancellationToken)
	{
		long subId;

		using (_subscriptions.EnterScope())
		{
			if (_subscriptions.ContainsKey(destination))
				return default;

			subId = ++_nextSubscriptionId;
			_subscriptions.Add(destination, subId);
		}

		// a positive id keeps the frame in the resend list, so the subscription is restored
		// automatically after a reconnection
		return _client.SendAsync(CreateFrame("SUBSCRIBE", new()
		{
			{ "id", subId.To<string>() },
			{ "destination", destination },
		}), cancellationToken, subId);
	}

	private ValueTask UnsubscribeAsync(string destination, CancellationToken cancellationToken)
	{
		long subId;

		using (_subscriptions.EnterScope())
		{
			if (!_subscriptions.TryGetValue(destination, out subId))
				return default;

			_subscriptions.Remove(destination);
		}

		return _client.SendAsync(CreateFrame("UNSUBSCRIBE", new()
		{
			{ "id", subId.To<string>() },
		}), cancellationToken, -subId);
	}

	private static string CreateFrame(string command, Dictionary<string, string> headers)
	{
		var builder = new StringBuilder(command).Append('\n');

		foreach (var (name, value) in headers)
			builder.Append(name).Append(':').Append(value).Append('\n');

		return builder.Append('\n').Append(_frameEnd).ToString();
	}

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var frame = msg.AsString();

		// a lone end of line is the STOMP heart beat
		if (frame.IsEmpty() || frame.Trim('\r', '\n', _frameEnd).IsEmpty())
			return;

		var (command, headers, body) = ParseFrame(frame);

		switch (command)
		{
			case "CONNECTED":
			case "RECEIPT":
				return;

			case "ERROR":
				if (Error is { } errorHandler)
					await errorHandler(new InvalidOperationException(headers.TryGetValue("message", out var reason) ? reason : body), cancellationToken);
				return;

			case "MESSAGE":
				break;

			default:
				this.AddErrorLog(LocalizedStrings.UnknownEvent, command);
				return;
		}

		if (!headers.TryGetValue("destination", out var destination))
			return;

		var (channel, baseCurrencyId, quoteCurrencyId) = ParseDestination(destination);

		if (channel.IsEmpty() || body.IsEmpty())
			return;

		var envelope = JObject.Parse(body);
		var payload = envelope["payload"];

		if (payload is null || payload.Type == JTokenType.Null)
			return;

		switch (channel)
		{
			case Channels.Ticker:
				await (TickerChanged?.Invoke(payload.DeserializeObject<Ticker>(), cancellationToken) ?? default);
				break;

			case Channels.Book:
			{
				// the very first message of a destination carries the whole book, the ones after
				// it carry the changed price levels only
				var isSnapshot = (envelope["nonce"]?.Value<long?>() ?? 0) == 0;
				await (OrderBookChanged?.Invoke(baseCurrencyId, quoteCurrencyId, payload.DeserializeObject<OrderBook>(), isSnapshot, cancellationToken) ?? default);
				break;
			}

			case Channels.Trade:
			{
				if (NewTrade is not { } tradeHandler)
					break;

				foreach (var trade in payload.DeserializeObject<Trade[]>() ?? [])
					await tradeHandler(trade, cancellationToken);

				break;
			}

			case Channels.Order:
				await (OrderChanged?.Invoke(payload.DeserializeObject<Order>(), cancellationToken) ?? default);
				break;

			case Channels.Account:
			{
				if (BalanceChanged is not { } balanceHandler)
					break;

				foreach (var balance in payload.DeserializeObject<Balance[]>() ?? [])
					await balanceHandler(balance, cancellationToken);

				break;
			}

			default:
				this.AddErrorLog(LocalizedStrings.UnknownEvent, channel);
				break;
		}
	}

	private static (string Command, Dictionary<string, string> Headers, string Body) ParseFrame(string frame)
	{
		var lines = frame.Split('\n');
		var headers = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
		var command = lines[0].Trim('\r');
		var index = 1;

		for (; index < lines.Length; index++)
		{
			var line = lines[index].Trim('\r');

			if (line.IsEmpty())
			{
				index++;
				break;
			}

			var separator = line.IndexOf(':');

			if (separator > 0)
				headers[line[..separator]] = line[(separator + 1)..];
		}

		var body = index < lines.Length
			? string.Join("\n", lines, index, lines.Length - index).TrimEnd('\r', '\n', _frameEnd)
			: string.Empty;

		return (command, headers, body);
	}

	// /v1/<channel>/<base currency id>/<quote currency id>
	private static (string Channel, string BaseCurrencyId, string QuoteCurrencyId) ParseDestination(string destination)
	{
		var parts = destination.Split('/', StringSplitOptions.RemoveEmptyEntries);

		if (parts.Length < 2)
			return default;

		// a private destination is prefixed with /user/<user id>
		var offset = parts[0].EqualsIgnoreCase("user") ? 2 : 0;

		if (parts.Length < offset + 2)
			return default;

		return parts.Length < offset + 4
			? (parts[offset + 1], null, null)
			: (parts[offset + 1], parts[offset + 2], parts[offset + 3]);
	}
}
