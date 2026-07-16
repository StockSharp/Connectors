namespace StockSharp.IG.Native;

internal sealed class IgStreamingClient : BaseLogReceiver, IDisposable
{
	private enum StreamKinds
	{
		Price,
		Tick,
		Candle,
		Account,
		Trade,
	}

	private sealed class SubscriptionEntry
	{
		public Subscription Subscription { get; init; }
		public int References { get; set; }
	}

	private sealed class ClientEvents(IgStreamingClient owner) : ClientListener
	{
		public void onListenEnd() { }
		public void onListenStart() { }
		public void onPropertyChange(string property) { }

		public void onServerError(int errorCode, string errorMessage)
			=> owner.OnError(new InvalidOperationException($"IG Lightstreamer error {errorCode}: {errorMessage}"));

		public void onStatusChange(string status)
			=> owner.OnStatus(status);
	}

	private sealed class SubscriptionEvents(IgStreamingClient owner, StreamKinds kind, TimeSpan timeFrame)
		: SubscriptionListener
	{
		public void onClearSnapshot(string itemName, int itemPos) { }
		public void onCommandSecondLevelItemLostUpdates(int lostUpdates, string key) { }
		public void onCommandSecondLevelSubscriptionError(int code, string message, string key)
			=> owner.OnError(new InvalidOperationException($"IG Lightstreamer second-level subscription error {code}: {message}"));
		public void onEndOfSnapshot(string itemName, int itemPos) { }
		public void onItemLostUpdates(string itemName, int itemPos, int lostUpdates)
			=> owner.OnError(new InvalidOperationException($"IG Lightstreamer lost {lostUpdates} update(s) for {itemName}."));
		public void onListenEnd() { }
		public void onListenStart() { }
		public void onSubscription() { }
		public void onUnsubscription() { }
		public void onRealMaxFrequency(string frequency) { }

		public void onSubscriptionError(int code, string message)
			=> owner.OnError(new InvalidOperationException($"IG Lightstreamer subscription error {code}: {message}"));

		public void onItemUpdate(ItemUpdate itemUpdate)
			=> owner.OnUpdate(kind, timeFrame, itemUpdate);
	}

	private readonly IgSession _session;
	private readonly SynchronizedDictionary<string, SubscriptionEntry> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private LightstreamerClient _client;
	private TaskCompletionSource _connected;
	private bool _disposed;

	public IgStreamingClient(IgSession session)
	{
		_session = session ?? throw new ArgumentNullException(nameof(session));
	}

	public event Action<IgMarketUpdate> MarketReceived;
	public event Action<IgTickUpdate> TickReceived;
	public event Action<IgCandleUpdate> CandleReceived;
	public event Action<IgAccountUpdate> AccountReceived;
	public event Action<IgStreamingTradeUpdate> TradeReceived;
	public event Action<Exception> Error;
	public event Action<ConnectionStates> StateChanged;

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		_connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_client = new(_session.LightstreamerEndpoint, null);
		_client.connectionDetails.User = _session.AccountId;
		_client.connectionDetails.Password = $"CST-{_session.Cst}|XST-{_session.SecurityToken}";
		_client.connectionOptions.ForcedTransport = "WS-STREAMING";
		_client.addListener(new ClientEvents(this));
		_client.connect();
		try
		{
			await _connected.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
			AddFixedSubscription("account", StreamKinds.Account, "MERGE", $"ACCOUNT:{_session.AccountId}",
				["PNL", "DEPOSIT", "AVAILABLE_CASH", "FUNDS", "MARGIN", "AVAILABLE_TO_DEAL", "EQUITY"]);
			AddFixedSubscription("trade", StreamKinds.Trade, "DISTINCT", $"TRADE:{_session.AccountId}",
				["CONFIRMS", "OPU", "WOU"]);
		}
		catch
		{
			Disconnect();
			throw;
		}
	}

	public void SubscribePrice(string epic)
	{
		var key = $"price:{epic}";
		AddSubscription(key, StreamKinds.Price, default, "MERGE", $"PRICE:{_session.AccountId}:{epic}",
			["MID_OPEN", "HIGH", "LOW", "BIDQUOTEID", "ASKQUOTEID",
			 "BIDPRICE1", "BIDPRICE2", "BIDPRICE3", "BIDPRICE4", "BIDPRICE5",
			 "ASKPRICE1", "ASKPRICE2", "ASKPRICE3", "ASKPRICE4", "ASKPRICE5",
			 "BIDSIZE1", "BIDSIZE2", "BIDSIZE3", "BIDSIZE4", "BIDSIZE5",
			 "ASKSIZE1", "ASKSIZE2", "ASKSIZE3", "ASKSIZE4", "ASKSIZE5", "TIMESTAMP", "DLG_FLAG"], "Pricing");
	}

	public void UnsubscribePrice(string epic)
		=> RemoveSubscription($"price:{epic}");

	public void SubscribeTicks(string epic)
		=> AddSubscription($"tick:{epic}", StreamKinds.Tick, default, "DISTINCT", $"CHART:{epic}:TICK",
			["BID", "OFR", "LTP", "LTV", "TTV", "UTM"]);

	public void UnsubscribeTicks(string epic)
		=> RemoveSubscription($"tick:{epic}");

	public void SubscribeCandles(string epic, TimeSpan timeFrame)
	{
		var scale = timeFrame.ToLightstreamerScale();
		AddSubscription($"candle:{epic}:{scale}", StreamKinds.Candle, timeFrame, "MERGE", $"CHART:{epic}:{scale}",
			["LTV", "TTV", "UTM", "OFR_OPEN", "OFR_HIGH", "OFR_LOW", "OFR_CLOSE",
			 "BID_OPEN", "BID_HIGH", "BID_LOW", "BID_CLOSE", "LTP_OPEN", "LTP_HIGH", "LTP_LOW", "LTP_CLOSE",
			 "CONS_END", "CONS_TICK_COUNT"]);
	}

	public void UnsubscribeCandles(string epic, TimeSpan timeFrame)
		=> RemoveSubscription($"candle:{epic}:{timeFrame.ToLightstreamerScale()}");

	private void AddFixedSubscription(string key, StreamKinds kind, string mode, string item, string[] fields)
	{
		var subscription = CreateSubscription(kind, default, mode, item, fields, null);
		_subscriptions.Add(key, new() { Subscription = subscription, References = 1 });
		_client.subscribe(subscription);
	}

	private void AddSubscription(string key, StreamKinds kind, TimeSpan timeFrame, string mode, string item,
		string[] fields, string dataAdapter = null)
	{
		if (_disposed || _client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		lock (_subscriptions.SyncRoot)
		{
			if (_subscriptions.TryGetValue(key, out var existing))
			{
				existing.References++;
				return;
			}
			if (_subscriptions.Count >= 40)
				throw new InvalidOperationException("IG permits at most 40 concurrent Lightstreamer subscriptions by default.");
			var subscription = CreateSubscription(kind, timeFrame, mode, item, fields, dataAdapter);
			_subscriptions.Add(key, new() { Subscription = subscription, References = 1 });
			_client.subscribe(subscription);
		}
	}

	private Subscription CreateSubscription(StreamKinds kind, TimeSpan timeFrame, string mode, string item,
		string[] fields, string dataAdapter)
	{
		var subscription = new Subscription(mode, [item], fields)
		{
			RequestedSnapshot = "yes",
		};
		if (!dataAdapter.IsEmpty())
			subscription.DataAdapter = dataAdapter;
		subscription.addListener(new SubscriptionEvents(this, kind, timeFrame));
		return subscription;
	}

	private void RemoveSubscription(string key)
	{
		if (_client == null)
			return;
		lock (_subscriptions.SyncRoot)
		{
			if (!_subscriptions.TryGetValue(key, out var entry))
				return;
			if (--entry.References > 0)
				return;
			_subscriptions.Remove(key);
			_client.unsubscribe(entry.Subscription);
		}
	}

	private void OnStatus(string status)
	{
		if (status?.StartsWith("CONNECTED:", StringComparison.OrdinalIgnoreCase) == true)
		{
			_connected?.TrySetResult();
			StateChanged?.Invoke(ConnectionStates.Connected);
		}
		else if (status.EqualsIgnoreCase("DISCONNECTED"))
			StateChanged?.Invoke(ConnectionStates.Disconnected);
		else if (status?.StartsWith("CONNECTING", StringComparison.OrdinalIgnoreCase) == true ||
			status?.StartsWith("DISCONNECTED:", StringComparison.OrdinalIgnoreCase) == true || status.EqualsIgnoreCase("STALLED"))
			StateChanged?.Invoke(ConnectionStates.Reconnecting);
	}

	private void OnError(Exception error)
	{
		_connected?.TrySetException(error);
		Error?.Invoke(error);
	}

	private void OnUpdate(StreamKinds kind, TimeSpan timeFrame, ItemUpdate update)
	{
		try
		{
			switch (kind)
			{
				case StreamKinds.Price:
					MarketReceived?.Invoke(ParseMarket(update));
					break;
				case StreamKinds.Tick:
					TickReceived?.Invoke(ParseTick(update));
					break;
				case StreamKinds.Candle:
					CandleReceived?.Invoke(ParseCandle(update, timeFrame));
					break;
				case StreamKinds.Account:
					AccountReceived?.Invoke(ParseAccount(update));
					break;
				case StreamKinds.Trade:
					TradeReceived?.Invoke(ParseTrade(update));
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
			}
		}
		catch (Exception ex)
		{
			OnError(ex);
		}
	}

	private static IgMarketUpdate ParseMarket(ItemUpdate update)
	{
		var parts = update.ItemName.Split(':');
		return new()
		{
			Epic = parts.Length >= 3 ? parts[^1] : update.ItemName,
			Time = ParseUnixMilliseconds(update.getValue("TIMESTAMP")) ?? DateTimeOffset.UtcNow,
			MidOpen = ParseDecimal(update.getValue("MID_OPEN")),
			High = ParseDecimal(update.getValue("HIGH")),
			Low = ParseDecimal(update.getValue("LOW")),
			MarketState = update.getValue("DLG_FLAG")?.Trim(),
			BidQuoteId = update.getValue("BIDQUOTEID"),
			AskQuoteId = update.getValue("ASKQUOTEID"),
			Bids = ParseLevels(update, "BID"),
			Asks = ParseLevels(update, "ASK"),
		};
	}

	private static IgPriceLevel[] ParseLevels(ItemUpdate update, string side)
	{
		var levels = new List<IgPriceLevel>(5);
		for (var index = 1; index <= 5; index++)
		{
			var price = ParseDecimal(update.getValue($"{side}PRICE{index}"));
			if (price == null)
				continue;
			levels.Add(new() { Price = price.Value, Volume = ParseDecimal(update.getValue($"{side}SIZE{index}")) });
		}
		return [.. levels];
	}

	private static IgTickUpdate ParseTick(ItemUpdate update)
		=> new()
		{
			Epic = GetEpic(update.ItemName),
			Time = ParseUnixMilliseconds(update.getValue("UTM")) ?? DateTimeOffset.UtcNow,
			Bid = ParseDecimal(update.getValue("BID")),
			Offer = ParseDecimal(update.getValue("OFR")),
			Last = ParseDecimal(update.getValue("LTP")),
			LastVolume = ParseDecimal(update.getValue("LTV")),
			TotalVolume = ParseDecimal(update.getValue("TTV")),
		};

	private static IgCandleUpdate ParseCandle(ItemUpdate update, TimeSpan timeFrame)
		=> new()
		{
			Epic = GetEpic(update.ItemName),
			TimeFrame = timeFrame,
			Time = ParseUnixMilliseconds(update.getValue("UTM")) ?? DateTimeOffset.UtcNow,
			Open = ParseCandleValue(update, "OPEN"),
			High = ParseCandleValue(update, "HIGH"),
			Low = ParseCandleValue(update, "LOW"),
			Close = ParseCandleValue(update, "CLOSE"),
			Volume = ParseDecimal(update.getValue("TTV")) ?? ParseDecimal(update.getValue("LTV")),
			IsFinished = update.getValue("CONS_END") == "1",
		};

	private static decimal? ParseCandleValue(ItemUpdate update, string part)
	{
		var last = ParseDecimal(update.getValue($"LTP_{part}"));
		if (last != null)
			return last;
		var bid = ParseDecimal(update.getValue($"BID_{part}"));
		var offer = ParseDecimal(update.getValue($"OFR_{part}"));
		return bid is { } b && offer is { } o ? (b + o) / 2 : bid ?? offer;
	}

	private IgAccountUpdate ParseAccount(ItemUpdate update)
		=> new()
		{
			AccountId = _session.AccountId,
			Time = DateTimeOffset.UtcNow,
			ProfitLoss = ParseDecimal(update.getValue("PNL")),
			Deposit = ParseDecimal(update.getValue("DEPOSIT")),
			UsedMargin = ParseDecimal(update.getValue("USED_MARGIN")) ?? ParseDecimal(update.getValue("MARGIN")),
			AmountDue = ParseDecimal(update.getValue("AMOUNT_DUE")) ?? ParseDecimal(update.getValue("FUNDS")),
			AvailableCash = ParseDecimal(update.getValue("AVAILABLE_CASH")) ?? ParseDecimal(update.getValue("AVAILABLE_TO_DEAL")),
		};

	private IgStreamingTradeUpdate ParseTrade(ItemUpdate update)
		=> new()
		{
			AccountId = _session.AccountId,
			Confirmation = Deserialize<IgConfirmation>(update.getValue("CONFIRMS")),
			Position = Deserialize<IgTradeUpdate>(update.getValue("OPU")),
			WorkingOrder = Deserialize<IgTradeUpdate>(update.getValue("WOU")),
		};

	private static T Deserialize<T>(string value)
		where T : class
		=> value.IsEmpty() ? null : JsonConvert.DeserializeObject<T>(value);

	private static decimal? ParseDecimal(string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;

	private static DateTimeOffset? ParseUnixMilliseconds(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
			? DateTimeOffset.FromUnixTimeMilliseconds(result)
			: null;

	private static string GetEpic(string item)
	{
		var parts = item.Split(':');
		return parts.Length > 1 ? parts[1] : item;
	}

	public void Disconnect()
	{
		if (_client == null)
			return;
		_client.disconnect();
		_client = null;
		_subscriptions.Clear();
		_connected = null;
	}

	protected override void DisposeManaged()
	{
		if (_disposed)
			return;
		_disposed = true;
		Disconnect();
		base.DisposeManaged();
	}
}
