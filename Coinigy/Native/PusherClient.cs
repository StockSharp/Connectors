namespace StockSharp.Coinigy.Native;

class PusherClient : BaseLogReceiver
{
	private enum MessageTypes
	{
		IsAuthenticated,
		Publish,
		RemoveToken,
		SetToken,
		Event,
		AckReceive
	}

	private static class Parser
	{
		public static MessageTypes Parse(long? rid, string evt)
		{
			if (evt == null)
				return rid == 1 ? MessageTypes.IsAuthenticated : MessageTypes.AckReceive;

			switch (evt)
			{
				case "#publish":
					return MessageTypes.Publish;
				case "#removeAuthToken":
					return MessageTypes.RemoveToken;
				case "#setAuthToken":
					return MessageTypes.SetToken;
				default:
					return MessageTypes.Event;
			}
		}
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Coinigy) + "_" + nameof(PusherClient);

	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly SecureString _key;
	private readonly SecureString _secret;
	private readonly SecureString _webSocketId;
	private readonly WebSocketClient _client;

	private string _token;

	//private readonly Dictionary<long?, object[]> _acks;

	public PusherClient(SecureString key, SecureString secret, SecureString webSocketId, WorkingTime workingTime)
	{
		_key = key;
		_secret = secret;
		_webSocketId = webSocketId;

		//_acks = new Dictionary<long?, object[]>();

		_client = new(
			"wss://sc-02.coinigy.com/socketcluster/",
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

		_client.PostConnect += OnConnected;
	}

	private ValueTask OnConnected(bool reconnect, CancellationToken cancellationToken)
	{
		if (_webSocketId.IsEmpty())
		{
			return default;
		}

		//var authobject = new Dictionary<string, object>
		//{
		//	{ "event", "#handshake" },
		//	{ "data", new Dictionary<string, object> { { "authToken", _webSocketId.To<string>() } } },
		//	{ "cid", Interlocked.Increment(ref _counter) }
		//};
		//var json = JsonConvert.SerializeObject(authobject, Formatting.Indented);

		//_client.Send(json);

		return EmitAsync("auth", new
		{
			apiKey = _key.UnSecure(),
			apiSecret = _secret.UnSecure(),
		}, cancellationToken);
	}

	protected override void DisposeManaged()
	{
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		_token = null;

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
		if (msg.AsString() == "#1")
		{
			await _client.SendAsync("#2", cancellationToken);
		}
		else
		{
			var dict = msg.AsObject<Dictionary<string, object>>();

			var dataObj = dict.TryGetValue("data");
			var rid = (long?)dict.TryGetValue("rid");
			var cid = (long?)dict.TryGetValue("cid");
			var evt = (string)dict.TryGetValue("event");

			var type = Parser.Parse(rid, evt);

			switch (type)
			{
				case MessageTypes.IsAuthenticated:
					var isAuthenticated = (bool?)dict.TryGetValue("isAuthenticated");
					if (isAuthenticated == true)
					{
					}
					else if (Error is { } errorHandler)
						await errorHandler(new InvalidOperationException("isAuthenticated == false"), cancellationToken);
					break;
				case MessageTypes.Publish:
					break;
				case MessageTypes.RemoveToken:
					_token = null;
					break;
				case MessageTypes.SetToken:
					_token = (string)dict.TryGetValue("token");
					break;
				case MessageTypes.Event:
					break;
				case MessageTypes.AckReceive:
					break;
				default:
					throw new ArgumentOutOfRangeException(type.ToString());
			}
		}
	}

	private static class Channels
	{
		public const string TicksChannel = "TRADE-{0}--{1}--{2}";
		public const string OrderBookChannel = "ORDER-{0}--{1}--{2}";
	}

	public ValueTask SubscribeTradesAsync(string baseCurr, string quoteCurr, string exchange, CancellationToken cancellationToken)
	{
		return SubscribeAsync(Channels.TicksChannel.Put(exchange, baseCurr, quoteCurr), cancellationToken);
	}

	public ValueTask UnSubscribeTradesAsync(string baseCurr, string quoteCurr, string exchange, CancellationToken cancellationToken)
	{
		return UnsubscribeAsync(Channels.TicksChannel.Put(exchange, baseCurr, quoteCurr), cancellationToken);
	}

	public ValueTask SubscribeOrderBookAsync(string baseCurr, string quoteCurr, string exchange, CancellationToken cancellationToken)
	{
		return SubscribeAsync(Channels.OrderBookChannel.Put(exchange, baseCurr, quoteCurr), cancellationToken);
	}

	public ValueTask UnSubscribeOrderBookAsync(string baseCurr, string quoteCurr, string exchange, CancellationToken cancellationToken)
	{
		return UnsubscribeAsync(Channels.OrderBookChannel.Put(exchange, baseCurr, quoteCurr), cancellationToken);
	}

	//private void Ack(long? cid)
	//{
	//	return (name, error, data) =>
	//	{
	//		var dataObject = new Dictionary<string, object>
	//		{
	//			{ "error", error },
	//			{ "data", data },
	//			{ "rid", cid }
	//		};
	//		var json = JsonConvert.SerializeObject(dataObject, Formatting.Indented);
	//		_client.Send(json);
	//	};
	//}

	private ValueTask EmitAsync(string evt, object data, CancellationToken cancellationToken)
	{
		var eventObject = new Dictionary<string, object>
		{
			{ "event", evt },
			{ "data", data }
		};
		var json = JsonConvert.SerializeObject(eventObject, Formatting.Indented);
		return _client.SendAsync(json, cancellationToken);
	}

	private ValueTask SubscribeAsync(string channel, CancellationToken cancellationToken)
			=> EmitAsync("#subscribe", new { channel }, cancellationToken);

	private ValueTask UnsubscribeAsync(string channel, CancellationToken cancellationToken)
			=> EmitAsync("#unsubscribe", new { channel }, cancellationToken);

	//private void Emit(string evt, object data, Ackcall ack)
	//{
	//	var count = Interlocked.Increment(ref _counter);
	//	var eventObject = new Dictionary<string, object>
	//	{
	//		{ "event", evt },
	//		{ "data", data },
	//		{ "cid", count }
	//	};
	//	_acks.Add(count, GetAckObject(evt, ack));
	//	var json = JsonConvert.SerializeObject(eventObject, Formatting.Indented);
	//	_client.Send(json);
	//}


	//private void Subscribe(string channel, Ackcall ack)
	//{
	//	var count = Interlocked.Increment(ref _counter);
	//	var subscribeObject = new Dictionary<string, object>
	//	{
	//		{ "event", "#subscribe" },
	//		{ "data", new Dictionary<string, string>() { { "channel", channel } } },
	//		{ "cid", count }
	//	};
	//	_acks.Add(count, GetAckObject(channel, ack));
	//	var json = JsonConvert.SerializeObject(subscribeObject, Formatting.Indented);
	//	_client.Send(json);
	//}


	//private void Unsubscribe(string channel, Ackcall ack)
	//{
	//	var count = Interlocked.Increment(ref _counter);
	//	var subscribeObject = new Dictionary<string, object>
	//	{
	//		{ "event", "#unsubscribe" },
	//		{ "data", channel },
	//		{ "cid", count }
	//	};
	//	_acks.Add(count, GetAckObject(channel, ack));
	//	var json = JsonConvert.SerializeObject(subscribeObject, Formatting.Indented);
	//	_client.Send(json);
	//}


	//private void Publish(string channel, object data, Ackcall ack)
	//{
	//	var count = Interlocked.Increment(ref _counter);
	//	var publishObject = new Dictionary<string, object>
	//	{
	//		{ "event", "#publish" },
	//		{ "data", new Dictionary<string, object> { { "channel", channel }, { "data", data } } },
	//		{ "cid", count }
	//	};
	//	_acks.Add(count, GetAckObject(channel, ack));
	//	var json = JsonConvert.SerializeObject(publishObject, Formatting.Indented);
	//	_client.Send(json);
	//}

	//private static object[] GetAckObject(string Event, Ackcall ack)
	//{
	//	return new object[] { Event, ack };
	//}
}