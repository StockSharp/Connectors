namespace StockSharp.DxFeed.Native;

internal sealed class DxLinkClient : BaseLogReceiver
{
	private const int _feedChannel = 1;
	private const string _feedService = "FEED";
	private const string _domService = "DOM";

	private readonly WebSocketClient _client;
	private readonly string _token;
	private readonly double _aggregationPeriod;
	private readonly int _depthLevels;
	private readonly object _sync = new();
	private readonly Dictionary<DxFeedSubscriptionKey, FeedSubscriptionState> _feedSubscriptions = [];
	private readonly Dictionary<string, DomSubscriptionState> _domSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<int, DomSubscriptionState> _domChannels = [];

	private CancellationTokenSource _keepAliveCts;
	private Task _keepAliveTask;
	private volatile bool _isSocketConnected;
	private volatile bool _isFeedReady;
	private bool _isSessionReady;
	private int _authorizationAttempts;
	private int _nextChannel = _feedChannel;
	private double _serverKeepAliveSeconds = 60;

	public DxLinkClient(string address, string token, TimeSpan aggregationPeriod, int depthLevels,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_token = token;
		_aggregationPeriod = aggregationPeriod.TotalSeconds;
		_depthLevels = depthLevels;

		_client = new WebSocketClient(
			address.ThrowIfEmpty(nameof(address)),
			OnStateChanged,
			(error, ct) => Error is { } handler ? handler(error, ct) : default,
			Process,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime,
			DisableAutoResend = true,
			SendSettings = new() { NullValueHandling = NullValueHandling.Ignore },
		};
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(DxFeed) + "_dxLink";

	public event Func<DxFeedEvent, CancellationToken, ValueTask> FeedDataReceived;
	public event Func<string, DxDomSnapshot, CancellationToken, ValueTask> DomSnapshotReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_keepAliveCts != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		try
		{
			await _client.ConnectAsync(cancellationToken);
			_keepAliveCts = new();
			_keepAliveTask = KeepAlive(_keepAliveCts.Token);
		}
		catch
		{
			_keepAliveCts?.Dispose();
			_keepAliveCts = null;
			throw;
		}
	}

	public void Disconnect()
	{
		_keepAliveCts?.Cancel();
		_keepAliveCts?.Dispose();
		_keepAliveCts = null;
		_keepAliveTask = null;
		_client.Disconnect();
	}

	public async ValueTask SubscribeFeed(string eventType, string symbol, DateTime? from,
		string source, CancellationToken cancellationToken)
	{
		eventType.ThrowIfEmpty(nameof(eventType));
		symbol.ThrowIfEmpty(nameof(symbol));

		var key = new DxFeedSubscriptionKey(eventType, symbol, source ?? string.Empty);
		long? fromTime = from is DateTime fromValue
			? (long)fromValue.ToUniversalTime().ToUnix(false)
			: null;
		DxFeedSubscription subscription = null;
		bool isReady;

		lock (_sync)
		{
			if (!_feedSubscriptions.TryGetValue(key, out var state))
			{
				state = new(key);
				_feedSubscriptions.Add(key, state);
			}

			var previousFrom = state.EffectiveFromTime;
			var wasEmpty = state.IsEmpty;
			state.Add(fromTime);

			if (wasEmpty || previousFrom != state.EffectiveFromTime)
				subscription = state.ToProtocol();

			isReady = _isFeedReady;
		}

		if (isReady && subscription != null)
			await SendFeedSubscriptions([subscription], null, cancellationToken);
	}

	public async ValueTask UnsubscribeFeed(string eventType, string symbol, DateTime? from,
		string source, CancellationToken cancellationToken)
	{
		var key = new DxFeedSubscriptionKey(eventType, symbol, source ?? string.Empty);
		long? fromTime = from is DateTime fromValue
			? (long)fromValue.ToUniversalTime().ToUnix(false)
			: null;
		DxFeedSubscription add = null;
		DxFeedSubscription remove = null;
		bool isReady;

		lock (_sync)
		{
			if (!_feedSubscriptions.TryGetValue(key, out var state))
				return;

			var previousFrom = state.EffectiveFromTime;
			if (!state.Remove(fromTime))
				return;

			if (state.IsEmpty)
			{
				remove = state.ToProtocol(previousFrom ?? fromTime);
				_feedSubscriptions.Remove(key);
			}
			else if (previousFrom != state.EffectiveFromTime)
				add = state.ToProtocol();

			isReady = _isFeedReady;
		}

		if (!isReady)
			return;

		if (remove != null)
			await SendFeedSubscriptions(null, [remove], cancellationToken);
		else if (add != null)
			await SendFeedSubscriptions([add], null, cancellationToken);
	}

	public async ValueTask SubscribeDom(string symbol, string[] sources,
		CancellationToken cancellationToken)
	{
		symbol.ThrowIfEmpty(nameof(symbol));
		if (sources is not { Length: > 0 } || sources.Any(s => s.IsEmpty()))
			throw new ArgumentException("At least one dxFeed DOM source must be specified.", nameof(sources));

		var key = GetDomKey(symbol, sources);
		DomSubscriptionState state;
		bool shouldRequest;

		lock (_sync)
		{
			if (_domSubscriptions.TryGetValue(key, out state))
			{
				state.Count++;
				return;
			}

			state = new(key, symbol, [.. sources], _nextChannel += 2);
			_domSubscriptions.Add(key, state);
			_domChannels.Add(state.Channel, state);
			shouldRequest = _isSessionReady;
		}

		if (shouldRequest)
			await RequestDomChannel(state, cancellationToken);
	}

	public async ValueTask UnsubscribeDom(string symbol, string[] sources,
		CancellationToken cancellationToken)
	{
		var key = GetDomKey(symbol, sources);
		int? channel = null;

		lock (_sync)
		{
			if (!_domSubscriptions.TryGetValue(key, out var state) || --state.Count > 0)
				return;

			_domSubscriptions.Remove(key);
			_domChannels.Remove(state.Channel);
			if (_isSessionReady)
				channel = state.Channel;
		}

		if (channel != null)
			await _client.SendAsync(new DxChannelCancel { Channel = channel.Value }, cancellationToken);
	}

	private ValueTask OnStateChanged(ConnectionStates state, CancellationToken cancellationToken)
	{
		_isSocketConnected = state == ConnectionStates.Connected;
		if (!_isSocketConnected)
		{
			_isFeedReady = false;
			lock (_sync)
			{
				_isSessionReady = false;
				foreach (var dom in _domSubscriptions.Values)
					dom.IsOpened = false;
			}
		}

		return StateChanged is { } handler ? handler(state, cancellationToken) : default;
	}

	private ValueTask OnPostConnect(bool isReconnect, CancellationToken cancellationToken)
	{
		_isFeedReady = false;
		lock (_sync)
		{
			_isSessionReady = false;
			_authorizationAttempts = 0;
			foreach (var dom in _domSubscriptions.Values)
				dom.IsOpened = false;
		}

		return _client.SendAsync(new DxSetupMessage
		{
			Channel = 0,
			Version = "0.1-stocksharp/1.0",
			KeepAliveTimeout = 60,
			AcceptKeepAliveTimeout = 60,
		}, cancellationToken);
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var raw = message.AsString();
		if (raw.IsEmpty())
			return;

		var header = JsonConvert.DeserializeObject<DxLinkHeader>(raw)
			?? throw new InvalidDataException("dxLink returned an invalid message.");

		switch (header.Type)
		{
			case DxLinkMessageTypes.Setup:
				if (header.KeepAliveTimeout is > 0)
					_serverKeepAliveSeconds = header.KeepAliveTimeout.Value;

				if (_token.IsEmpty())
					await OpenChannels(cancellationToken);
				else
					await SendAuthorization(cancellationToken);
				break;

			case DxLinkMessageTypes.AuthState:
				if (header.State.EqualsIgnoreCase("AUTHORIZED"))
					await OpenChannels(cancellationToken);
				else if (_token.IsEmpty())
					await RaiseError("dxLink requires an authorization token.", cancellationToken);
				else
				{
					int attempts;
					lock (_sync)
						attempts = _authorizationAttempts;

					if (attempts < 2)
						await SendAuthorization(cancellationToken);
					else
						await RaiseError("dxLink authorization failed.", cancellationToken);
				}
				break;

			case DxLinkMessageTypes.ChannelOpened:
				if (header.Channel == _feedChannel)
					await OpenFeed(cancellationToken);
				else
					await OpenDom(header.Channel, cancellationToken);
				break;

			case DxLinkMessageTypes.ChannelClosed:
				await ProcessChannelClosed(header.Channel, cancellationToken);
				break;

			case DxLinkMessageTypes.FeedConfig:
				var feedConfig = JsonConvert.DeserializeObject<DxFeedConfigMessage>(raw);
				if (feedConfig?.DataFormat.EqualsIgnoreCase("FULL") != true)
					await RaiseError("dxLink did not accept the FULL feed data format.", cancellationToken);
				break;

			case DxLinkMessageTypes.FeedData:
				var feedData = JsonConvert.DeserializeObject<DxFeedDataMessage>(raw);
				if (FeedDataReceived is { } feedHandler)
				{
					foreach (var data in feedData?.Data ?? [])
						await feedHandler(data, cancellationToken);
				}
				break;

			case DxLinkMessageTypes.DomConfig:
				var domConfig = JsonConvert.DeserializeObject<DxDomConfigMessage>(raw);
				if (domConfig?.DataFormat.EqualsIgnoreCase("FULL") != true)
					await RaiseError("dxLink did not accept the FULL DOM data format.", cancellationToken);
				break;

			case DxLinkMessageTypes.DomSnapshot:
				await ProcessDomSnapshot(raw, header.Channel, cancellationToken);
				break;

			case DxLinkMessageTypes.Error:
				await RaiseError($"dxLink {header.Error.IsEmpty("UNKNOWN")}: {header.Message.IsEmpty("Protocol error.")}", cancellationToken);
				break;
		}
	}

	private async ValueTask SendAuthorization(CancellationToken cancellationToken)
	{
		lock (_sync)
			_authorizationAttempts++;

		await _client.SendAsync(new DxAuthMessage
		{
			Channel = 0,
			Token = _token,
		}, cancellationToken);
	}

	private async ValueTask OpenChannels(CancellationToken cancellationToken)
	{
		DomSubscriptionState[] domSubscriptions;
		lock (_sync)
		{
			if (_isSessionReady)
				return;

			_isSessionReady = true;
			domSubscriptions = [.. _domSubscriptions.Values];
		}

		await _client.SendAsync(new DxChannelRequest
		{
			Channel = _feedChannel,
			Service = _feedService,
			Parameters = new() { Contract = "AUTO" },
		}, cancellationToken);

		foreach (var dom in domSubscriptions)
			await RequestDomChannel(dom, cancellationToken);
	}

	private async ValueTask OpenFeed(CancellationToken cancellationToken)
	{
		await _client.SendAsync(new DxFeedSetup
		{
			Channel = _feedChannel,
			AcceptAggregationPeriod = _aggregationPeriod,
		}, cancellationToken);

		DxFeedSubscription[] subscriptions;
		lock (_sync)
		{
			_isFeedReady = true;
			subscriptions = _feedSubscriptions.Values.Select(s => s.ToProtocol()).ToArray();
		}

		if (subscriptions.Length > 0)
			await SendFeedSubscriptions(subscriptions, null, cancellationToken);
	}

	private async ValueTask OpenDom(int channel, CancellationToken cancellationToken)
	{
		DomSubscriptionState state;
		lock (_sync)
		{
			if (!_domChannels.TryGetValue(channel, out state))
				return;

			state.IsOpened = true;
		}

		await _client.SendAsync(new DxDomSetup
		{
			Channel = channel,
			AcceptAggregationPeriod = _aggregationPeriod,
			AcceptDepthLimit = _depthLevels,
			AcceptOrderFields = ["price", "size"],
		}, cancellationToken);
	}

	private async ValueTask ProcessChannelClosed(int channel, CancellationToken cancellationToken)
	{
		if (channel == _feedChannel)
		{
			_isFeedReady = false;
			await RaiseError("dxLink closed the FEED channel.", cancellationToken);
			return;
		}

		DomSubscriptionState state;
		lock (_sync)
		{
			if (!_domChannels.TryGetValue(channel, out state))
				return;

			state.IsOpened = false;
		}

		await RaiseError($"dxLink closed the DOM channel for '{state.Symbol}'.", cancellationToken);
	}

	private async ValueTask ProcessDomSnapshot(string raw, int channel,
		CancellationToken cancellationToken)
	{
		DomSubscriptionState state;
		lock (_sync)
		{
			if (!_domChannels.TryGetValue(channel, out state))
				return;
		}

		var snapshot = JsonConvert.DeserializeObject<DxDomSnapshot>(raw);
		if (snapshot != null && DomSnapshotReceived is { } handler)
			await handler(state.Symbol, snapshot, cancellationToken);
	}

	private ValueTask RequestDomChannel(DomSubscriptionState state,
		CancellationToken cancellationToken)
		=> _client.SendAsync(new DxChannelRequest
		{
			Channel = state.Channel,
			Service = _domService,
			Parameters = new()
			{
				Symbol = state.Symbol,
				Sources = state.Sources,
			},
		}, cancellationToken);

	private ValueTask SendFeedSubscriptions(DxFeedSubscription[] add,
		DxFeedSubscription[] remove, CancellationToken cancellationToken)
		=> _client.SendAsync(new DxFeedSubscriptionMessage
		{
			Channel = _feedChannel,
			Add = add,
			Remove = remove,
		}, cancellationToken);

	private async Task KeepAlive(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				var interval = TimeSpan.FromSeconds(Math.Max(1, Math.Min(30, _serverKeepAliveSeconds / 2)));
				await Task.Delay(interval, cancellationToken);
				if (_isSocketConnected)
					await _client.SendAsync(new DxKeepAliveMessage(), cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				if (Error is { } handler)
					await handler(ex, CancellationToken.None);
			}
		}
	}

	private ValueTask RaiseError(string message, CancellationToken cancellationToken)
		=> Error is { } handler
			? handler(new InvalidOperationException(message), cancellationToken)
			: default;

	private static string GetDomKey(string symbol, string[] sources)
		=> $"{symbol}\u001f{sources.Select(s => s.ToUpperInvariant()).OrderBy(s => s).Join(",")}";

	protected override void DisposeManaged()
	{
		Disconnect();
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	private sealed class FeedSubscriptionState
	{
		private readonly Dictionary<long, int> _fromTimes = [];

		public FeedSubscriptionState(DxFeedSubscriptionKey key)
		{
			Key = key;
		}

		public DxFeedSubscriptionKey Key { get; }
		public int RegularCount { get; private set; }
		public bool IsEmpty => RegularCount == 0 && _fromTimes.Count == 0;
		public long? EffectiveFromTime => _fromTimes.Count == 0 ? null : _fromTimes.Keys.Min();

		public void Add(long? fromTime)
		{
			if (fromTime == null)
			{
				RegularCount++;
				return;
			}

			_fromTimes.TryGetValue(fromTime.Value, out var count);
			_fromTimes[fromTime.Value] = count + 1;
		}

		public bool Remove(long? fromTime)
		{
			if (fromTime == null)
			{
				if (RegularCount == 0)
					return false;
				RegularCount--;
				return true;
			}

			if (!_fromTimes.TryGetValue(fromTime.Value, out var count))
				return false;

			if (count == 1)
				_fromTimes.Remove(fromTime.Value);
			else
				_fromTimes[fromTime.Value] = count - 1;
			return true;
		}

		public DxFeedSubscription ToProtocol(long? fromTime = null)
			=> new()
			{
				EventType = Key.EventType,
				Symbol = Key.Symbol,
				Source = Key.Source.IsEmpty() ? null : Key.Source,
				FromTime = fromTime ?? EffectiveFromTime,
			};
	}

	private sealed class DomSubscriptionState
	{
		public DomSubscriptionState(string key, string symbol, string[] sources, int channel)
		{
			Key = key;
			Symbol = symbol;
			Sources = sources;
			Channel = channel;
		}

		public string Key { get; }
		public string Symbol { get; }
		public string[] Sources { get; }
		public int Channel { get; }
		public int Count { get; set; } = 1;
		public bool IsOpened { get; set; }
	}
}
