namespace StockSharp.TradingTechnologies.Native;

using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

internal sealed class TradingTechnologiesSdkClient : IDisposable
{
	private sealed class ExternalSubscription
	{
		public ulong InstrumentId { get; init; }
		public TradingTechnologiesMarketDataKinds Kind { get; init; }
	}

	private sealed class PriceState
	{
		public object Instrument { get; init; }
		public TradingTechnologiesInstrument Info { get; init; }
		public object Subscription { get; init; }
		public Delegate Handler { get; set; }
		public HashSet<long> Level1Subscriptions { get; } = [];
		public HashSet<long> DepthSubscriptions { get; } = [];
	}

	private sealed class TickState
	{
		public object Instrument { get; init; }
		public TradingTechnologiesInstrument Info { get; init; }
		public object Subscription { get; init; }
		public Delegate Handler { get; set; }
		public HashSet<long> Subscriptions { get; } = [];
	}

	private readonly string _sdkPath;
	private readonly string _appSecretKey;
	private readonly string _environment;
	private readonly int _initializationTimeout;
	private readonly int _marketDepth;
	private readonly bool _isBinaryProtocol;
	private readonly bool _isOptionsEnabled;

	private readonly Dictionary<long, ExternalSubscription> _externalSubscriptions = [];
	private readonly Dictionary<ulong, PriceState> _priceStates = [];
	private readonly Dictionary<ulong, TickState> _tickStates = [];
	private readonly Dictionary<ulong, object> _instruments = [];
	private readonly Dictionary<ulong, TradingTechnologiesInstrument> _instrumentInfos = [];
	private readonly List<(object Target, string Name, Delegate Handler)> _sdkEvents = [];

	private Assembly _assembly;
	private object _dispatcher;
	private object _api;
	private object _tradeSubscription;
	private Thread _workerThread;
	private Delegate _initializationHandler;
	private Delegate _shutdownHandler;
	private TaskCompletionSource _ready;
	private int _isDisconnecting;
	private int _isReady;
	private bool _isOrderBookSynchronized;

	public TradingTechnologiesSdkClient(
		string sdkPath,
		string appSecretKey,
		string environment,
		int initializationTimeout,
		int marketDepth,
		bool isBinaryProtocol,
		bool isOptionsEnabled)
	{
		_sdkPath = sdkPath;
		_appSecretKey = appSecretKey.ThrowIfEmpty(nameof(appSecretKey));
		_environment = environment.ThrowIfEmpty(nameof(environment));
		_initializationTimeout = initializationTimeout;
		_marketDepth = marketDepth;
		_isBinaryProtocol = isBinaryProtocol;
		_isOptionsEnabled = isOptionsEnabled;
	}

	public event Func<TradingTechnologiesLevel1Update, CancellationToken, ValueTask> Level1Received;
	public event Func<TradingTechnologiesDepthUpdate, CancellationToken, ValueTask> DepthReceived;
	public event Func<TradingTechnologiesTick, CancellationToken, ValueTask> TickReceived;
	public event Func<TradingTechnologiesOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<TradingTechnologiesFill, CancellationToken, ValueTask> FillReceived;
	public event Func<TradingTechnologiesPosition, CancellationToken, ValueTask> PositionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<bool, string, CancellationToken, ValueTask> ConnectionStateChanged;

	public string Version => _assembly?.GetName().Version?.ToString() ?? "unknown";

	public async Task ConnectAsync(CancellationToken cancellationToken)
	{
		if (_workerThread != null)
			throw new InvalidOperationException("The TT .NET SDK client is already connected.");

		Interlocked.Exchange(ref _isDisconnecting, 0);
		Interlocked.Exchange(ref _isReady, 0);
		_ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_assembly = LoadAssembly(_sdkPath);
		_workerThread = new Thread(RunDispatcher)
		{
			IsBackground = true,
			Name = "StockSharp TT .NET SDK",
		};
		_workerThread.Start();
		await _ready.Task.WaitAsync(cancellationToken);
	}

	private void RunDispatcher()
	{
		try
		{
			_dispatcher = InvokeStatic(GetSdkType("tt_net_sdk.Dispatcher"), "AttachWorkerDispatcher")
				?? throw new InvalidOperationException("TT .NET SDK did not create a worker dispatcher.");
			Invoke(_dispatcher, "DispatchAction", (Action)InitializeApi);
			Invoke(_dispatcher, "Run");
		}
		catch (Exception error)
		{
			_ready?.TrySetException(error);
			Publish(Error, error);
		}
	}

	private void InitializeApi()
	{
		try
		{
			var environment = ParseEnum("tt_net_sdk.ServiceEnvironment", _environment);
			var options = Create(GetSdkType("tt_net_sdk.TTAPIOptions"), environment, _appSecretKey, _initializationTimeout);
			SetProperty(options, "BinaryProtocol", _isBinaryProtocol);
			SetProperty(options, "EnableOrderExecution", true);
			SetProperty(options, "EnableFillFeeds", true);
			SetProperty(options, "EnablePositions", true);
			SetProperty(options, "EnableOptions", _isOptionsEnabled);
			SetProperty(options, "UseDecimalPrices", true);

			var apiType = GetSdkType("tt_net_sdk.TTAPI");
			var create = apiType.GetMethods(BindingFlags.Public | BindingFlags.Static)
				.FirstOrDefault(method => method.Name == "CreateTTAPI" && method.GetParameters().Length is 3 or 4)
				?? throw new MissingMethodException(apiType.FullName, "CreateTTAPI");
			_initializationHandler = CreateCallback(create.GetParameters()[2].ParameterType, ApiInitialized);
			var args = create.GetParameters().Length == 4
				? new[] { _dispatcher, options, _initializationHandler, null }
				: [_dispatcher, options, _initializationHandler];
			InvokeMethod(create, null, args);
		}
		catch (Exception error)
		{
			_ready.TrySetException(error);
			TryShutdownDispatcher();
		}
	}

	private void ApiInitialized(object[] args)
	{
		var api = args.Length > 0 ? args[0] : null;
		var initializationError = args.Length > 1 ? args[1] : null;
		if (initializationError != null)
		{
			var message = GetString(initializationError, "Message").IsEmpty(initializationError.ToString());
			var error = new InvalidOperationException($"TT .NET SDK initialization: {message}");
			if (GetBool(initializationError, "IsRecoverable"))
			{
				Publish(Error, error);
				return;
			}

			_ready.TrySetException(error);
			TryShutdownDispatcher();
			return;
		}

		try
		{
			_api = api ?? throw new InvalidOperationException("TT .NET SDK returned no TTAPI instance.");
			TrackEvent(_api, "TTAPIStatusUpdate", ProcessApiStatus);
			TrackEvent(_api, "OrderbookSynced", ProcessOrderBookSynchronized);
			TrackEvent(_api, "ProfitLossChanged", ProcessProfitLossChanged);
			TrackEvent(_api, "TTApiError", ProcessApiError);
			Invoke(_api, "Start");
		}
		catch (Exception error)
		{
			_ready.TrySetException(error);
			TryShutdownDispatcher();
		}
	}

	private void ProcessApiStatus(object[] args)
	{
		var eventArgs = args.LastOrDefault();
		var status = GetString(eventArgs, "StatusMessage").IsEmpty(eventArgs?.ToString());
		if (GetBool(eventArgs, "IsReady"))
		{
			try
			{
				EnsureTradeSubscription();
				var wasReady = Interlocked.Exchange(ref _isReady, 1) != 0;
				var isInitialConnection = _ready.TrySetResult();
				if (!isInitialConnection || wasReady)
					Publish(ConnectionStateChanged, true, status);
			}
			catch (Exception error)
			{
				_ready.TrySetException(error);
				Publish(Error, error);
			}
		}
		else if (GetBool(eventArgs, "IsDown"))
		{
			if (Interlocked.Exchange(ref _isReady, 0) != 0)
				Publish(ConnectionStateChanged, false, status);
		}
	}

	private void EnsureTradeSubscription()
	{
		if (_tradeSubscription != null)
			return;

		_tradeSubscription = Create(GetSdkType("tt_net_sdk.TradeSubscription"), _dispatcher, false);
		TrackEvent(_tradeSubscription, "OrderAdded", ProcessOrderAdded);
		TrackEvent(_tradeSubscription, "OrderUpdated", ProcessOrderUpdated);
		TrackEvent(_tradeSubscription, "OrderDeleted", ProcessOrderDeleted);
		TrackEvent(_tradeSubscription, "OrderFilled", ProcessOrderFilled);
		TrackEvent(_tradeSubscription, "OrderRejected", ProcessOrderRejected);
		TrackEvent(_tradeSubscription, "OrderBookDownload", _ => _isOrderBookSynchronized = true);
		Invoke(_tradeSubscription, "Start");
	}

	private void ProcessOrderBookSynchronized(object[] args)
	{
		_isOrderBookSynchronized = true;
		PublishPositions();
	}

	private void ProcessProfitLossChanged(object[] args)
	{
		try
		{
			var eventArgs = args.LastOrDefault();
			var position = Invoke(_api, "GetPositionSnapshot",
				GetProperty(eventArgs, "AccountKey"),
				GetProperty(eventArgs, "InstrumentKey"));
			PublishPosition(position);
		}
		catch (Exception error)
		{
			Publish(Error, error);
		}
	}

	private void ProcessApiError(object[] args)
	{
		var eventArgs = args.LastOrDefault();
		var error = TryGetProperty(eventArgs, "Error") as Exception
			?? TryGetProperty(eventArgs, "Exception") as Exception
			?? new InvalidOperationException(GetString(eventArgs, "Message").IsEmpty(eventArgs?.ToString()));
		Publish(Error, error);
	}

	public Task<TradingTechnologiesInstrument[]> SearchInstrumentsAsync(
		string query,
		string market,
		int count,
		CancellationToken cancellationToken)
		=> RunOnDispatcherAsync(() => SearchInstruments(query, market, count), cancellationToken);

	private TradingTechnologiesInstrument[] SearchInstruments(string query, string market, int count)
	{
		var marketIds = CreateMarketList(market);
		var productType = ParseEnum("tt_net_sdk.ProductType", "NotSet");
		var searcher = Create(GetSdkType("tt_net_sdk.InstrumentSearcher"),
			_dispatcher, query.ThrowIfEmpty(nameof(query)), marketIds, productType, 0);
		try
		{
			var result = Invoke(searcher, "Get")?.ToString();
			if (!result.EqualsIgnoreCase("Found"))
				return [];

			var found = new List<TradingTechnologiesInstrument>();
			foreach (var item in Enumerate(GetProperty(searcher, "Results")).Take(Math.Max(0, count)))
			{
				var id = GetUInt64(item, "Id");
				if (id == 0)
					continue;
				try
				{
					var instrument = LookupInstrument(id.Value);
					if (instrument != null)
						found.Add(GetInstrumentInfo(instrument));
				}
				catch (Exception error)
				{
					Publish(Error, error);
				}
			}
			return [.. found];
		}
		finally
		{
			DisposeObject(searcher);
		}
	}

	public Task<TradingTechnologiesInstrument> SubscribeAsync(
		long subscriptionId,
		TradingTechnologiesMarketDataKinds kind,
		ulong? instrumentId,
		string symbol,
		string market,
		CancellationToken cancellationToken)
		=> RunOnDispatcherAsync(() => Subscribe(subscriptionId, kind, instrumentId, symbol, market), cancellationToken);

	private TradingTechnologiesInstrument Subscribe(
		long subscriptionId,
		TradingTechnologiesMarketDataKinds kind,
		ulong? instrumentId,
		string symbol,
		string market)
	{
		if (_externalSubscriptions.ContainsKey(subscriptionId))
			throw new InvalidOperationException($"TT subscription {subscriptionId} already exists.");

		var instrument = ResolveInstrument(instrumentId, symbol, market);
		var info = GetInstrumentInfo(instrument);
		_externalSubscriptions.Add(subscriptionId, new() { InstrumentId = info.Id, Kind = kind });

		try
		{
			if (kind == TradingTechnologiesMarketDataKinds.Ticks)
			{
				if (!_tickStates.TryGetValue(info.Id, out var state))
				{
					var subscription = Create(GetSdkType("tt_net_sdk.TimeAndSalesSubscription"), instrument, _dispatcher);
					state = new TickState
					{
						Instrument = instrument,
						Info = info,
						Subscription = subscription,
					};
					state.Handler = AddEvent(subscription, "Update", args => ProcessTicks(state, args));
					_tickStates.Add(info.Id, state);
					Invoke(subscription, "Start");
				}
				state.Subscriptions.Add(subscriptionId);
			}
			else
			{
				if (!_priceStates.TryGetValue(info.Id, out var state))
				{
					var subscription = Create(GetSdkType("tt_net_sdk.PriceSubscription"), instrument, _dispatcher);
					var settings = Create(GetSdkType("tt_net_sdk.PriceSubscriptionSettings"),
						ParseEnum("tt_net_sdk.PriceSubscriptionType", "MarketDepth"), _marketDepth);
					SetProperty(subscription, "Settings", settings);
					state = new PriceState
					{
						Instrument = instrument,
						Info = info,
						Subscription = subscription,
					};
					state.Handler = AddEvent(subscription, "FieldsUpdated", args => ProcessPrice(state, args));
					_priceStates.Add(info.Id, state);
					Invoke(subscription, "Start");
				}

				(kind == TradingTechnologiesMarketDataKinds.Level1
					? state.Level1Subscriptions
					: state.DepthSubscriptions).Add(subscriptionId);
			}
			return info;
		}
		catch
		{
			_externalSubscriptions.Remove(subscriptionId);
			throw;
		}
	}

	public Task UnsubscribeAsync(long subscriptionId, CancellationToken cancellationToken)
		=> RunOnDispatcherAsync(() => Unsubscribe(subscriptionId), cancellationToken);

	private void Unsubscribe(long subscriptionId)
	{
		if (!_externalSubscriptions.Remove(subscriptionId, out var external))
			return;

		if (external.Kind == TradingTechnologiesMarketDataKinds.Ticks)
		{
			if (!_tickStates.TryGetValue(external.InstrumentId, out var state))
				return;
			state.Subscriptions.Remove(subscriptionId);
			if (state.Subscriptions.Count == 0)
			{
				RemoveEvent(state.Subscription, "Update", state.Handler);
				DisposeObject(state.Subscription);
				_tickStates.Remove(external.InstrumentId);
			}
			return;
		}

		if (!_priceStates.TryGetValue(external.InstrumentId, out var priceState))
			return;
		(external.Kind == TradingTechnologiesMarketDataKinds.Level1
			? priceState.Level1Subscriptions
			: priceState.DepthSubscriptions).Remove(subscriptionId);
		if (priceState.Level1Subscriptions.Count == 0 && priceState.DepthSubscriptions.Count == 0)
		{
			RemoveEvent(priceState.Subscription, "FieldsUpdated", priceState.Handler);
			DisposeObject(priceState.Subscription);
			_priceStates.Remove(external.InstrumentId);
		}
	}

	private void ProcessPrice(PriceState state, object[] args)
	{
		try
		{
			var eventArgs = args.LastOrDefault();
			if (TryCreateEventError(eventArgs, out var error))
			{
				Publish(Error, error);
				return;
			}

			var fields = TryGetProperty(eventArgs, "Fields") ?? eventArgs;
			var serverTime = NanosecondsToUtc(GetInt64(fields, "ServerRecvTime")) ?? DateTime.UtcNow;

			if (state.Level1Subscriptions.Count > 0)
			{
				Publish(Level1Received, new TradingTechnologiesLevel1Update
				{
					SubscriptionIds = [.. state.Level1Subscriptions],
					Instrument = state.Info,
					ServerTime = serverTime,
					BidPrice = ReadField(fields, "GetBestBidPriceField"),
					BidVolume = ReadField(fields, "GetBestBidQuantityField"),
					AskPrice = ReadField(fields, "GetBestAskPriceField"),
					AskVolume = ReadField(fields, "GetBestAskQuantityField"),
					LastPrice = ReadField(fields, "GetLastTradedPriceField"),
					LastVolume = ReadField(fields, "GetLastTradedQuantityField"),
					OpenPrice = ReadField(fields, "GetOpenPriceField"),
					HighPrice = ReadField(fields, "GetHighPriceField"),
					LowPrice = ReadField(fields, "GetLowPriceField"),
					ClosePrice = ReadField(fields, "GetClosePriceField"),
					SettlementPrice = ReadField(fields, "GetSettlementPriceField"),
					Volume = ReadField(fields, "GetTotalTradedQuantityField"),
					OpenInterest = ReadField(fields, "GetOpenInterestField"),
					TradingStatus = ReadFieldText(fields, "GetSeriesStatusField"),
				});
			}

			if (state.DepthSubscriptions.Count > 0)
			{
				var depthCount = Math.Min(_marketDepth, GetInt32(TryInvoke(fields, "GetLargestCurrentDepthLevel")) ?? 0);
				var bids = new List<TradingTechnologiesDepthLevel>();
				var asks = new List<TradingTechnologiesDepthLevel>();

				for (var level = 0; level < depthCount; level++)
				{
					AddDepthLevel(bids, fields, "GetBestBidPriceField", "GetBestBidQuantityField", level);
					AddDepthLevel(asks, fields, "GetBestAskPriceField", "GetBestAskQuantityField", level);
				}

				Publish(DepthReceived, new TradingTechnologiesDepthUpdate
				{
					SubscriptionIds = [.. state.DepthSubscriptions],
					Instrument = state.Info,
					ServerTime = serverTime,
					Bids = [.. bids],
					Asks = [.. asks],
				});
			}
		}
		catch (Exception error)
		{
			Publish(Error, error);
		}
	}

	private static void AddDepthLevel(
		ICollection<TradingTechnologiesDepthLevel> destination,
		object fields,
		string priceMethod,
		string quantityMethod,
		int level)
	{
		var price = ReadField(fields, priceMethod, level);
		var volume = ReadField(fields, quantityMethod, level);
		if (price is not decimal priceValue || volume is not decimal volumeValue)
			return;

		destination.Add(new()
		{
			Price = priceValue,
			Volume = volumeValue,
		});
	}

	private void ProcessTicks(TickState state, object[] args)
	{
		try
		{
			var eventArgs = args.LastOrDefault();
			if (TryCreateEventError(eventArgs, out var error))
			{
				Publish(Error, error);
				return;
			}

			var index = 0;
			foreach (var item in Enumerate(TryGetProperty(eventArgs, "Data")))
			{
				var epoch = GetInt64(item, "EpochTimestamp") ?? 0;
				var serverTime = MicrosecondsToUtc(epoch)
					?? GetDateTime(item, "TimeStamp")
					?? DateTime.UtcNow;
				var price = GetDecimal(item, "TradePrice");
				var volume = GetDecimal(item, "TradeQuantity");
				if (price == null || volume == null)
					continue;

				Publish(TickReceived, new TradingTechnologiesTick
				{
					SubscriptionIds = [.. state.Subscriptions],
					Instrument = state.Info,
					TradeId = $"{state.Info.Id}:{epoch}:{index++}",
					ServerTime = serverTime,
					Price = price.Value,
					Volume = volume.Value,
					Direction = GetString(item, "Direction"),
					IsImplied = GetBool(item, "IsImplied"),
				});
			}
		}
		catch (Exception error)
		{
			Publish(Error, error);
		}
	}

	private void ProcessOrderAdded(object[] args)
		=> PublishOrder(TryGetProperty(args.LastOrDefault(), "Order"));

	private void ProcessOrderUpdated(object[] args)
	{
		var eventArgs = args.LastOrDefault();
		PublishOrder(TryGetProperty(eventArgs, "NewOrder") ?? TryGetProperty(eventArgs, "Order"));
	}

	private void ProcessOrderDeleted(object[] args)
	{
		var eventArgs = args.LastOrDefault();
		PublishOrder(
			TryGetProperty(eventArgs, "DeletedUpdate") ?? TryGetProperty(eventArgs, "OldOrder"),
			"Canceled");
	}

	private void ProcessOrderFilled(object[] args)
	{
		var eventArgs = args.LastOrDefault();
		PublishOrder(TryGetProperty(eventArgs, "Order") ?? TryGetProperty(eventArgs, "NewOrder"));
		PublishFill(TryGetProperty(eventArgs, "Fill"));
	}

	private void ProcessOrderRejected(object[] args)
	{
		var eventArgs = args.LastOrDefault();
		var message = GetString(eventArgs, "Message")
			.IsEmpty(GetString(eventArgs, "OrderRejectReason"))
			.IsEmpty(GetString(eventArgs, "RejectReason"))
			.IsEmpty(GetString(eventArgs, "RejectSource"))
			.IsEmpty("TT rejected the order.");
		PublishOrder(TryGetProperty(eventArgs, "Order"), "Rejected", message);
	}

	private void PublishOrder(object value, string status = null, string error = null)
	{
		try
		{
			var order = ConvertOrder(value, status, error);
			if (order != null)
				Publish(OrderReceived, order);
		}
		catch (Exception ex)
		{
			Publish(Error, ex);
		}
	}

	private void PublishFill(object value)
	{
		try
		{
			var fill = ConvertFill(value);
			if (fill != null)
				Publish(FillReceived, fill);
		}
		catch (Exception error)
		{
			Publish(Error, error);
		}
	}

	public Task<TradingTechnologiesAccount[]> GetAccountsAsync(CancellationToken cancellationToken)
		=> RunOnDispatcherAsync(GetAccounts, cancellationToken);

	private TradingTechnologiesAccount[] GetAccounts()
		=> [.. EnumerateValues(TryGetProperty(_api, "Accounts"))
			.Select(ConvertAccount)
			.Where(account => account != null)];

	public Task<TradingTechnologiesPosition[]> GetPositionsAsync(CancellationToken cancellationToken)
		=> RunOnDispatcherAsync(GetPositions, cancellationToken);

	private TradingTechnologiesPosition[] GetPositions()
	{
		var snapshots = TryInvoke(_api, "GetPositionSnapshots")
			?? TryGetProperty(_api, "PositionSnapshots");
		return [.. EnumerateValues(snapshots)
			.Select(ConvertPosition)
			.Where(position => position != null)];
	}

	public Task<TradingTechnologiesOrder[]> GetOrdersAsync(CancellationToken cancellationToken)
		=> RunOnDispatcherAsync(() => EnumerateValues(TryGetProperty(_tradeSubscription, "Orders"))
				.Select(value => ConvertOrder(value))
				.Where(order => order != null)
				.ToArray(), cancellationToken);

	public Task<TradingTechnologiesFill[]> GetFillsAsync(CancellationToken cancellationToken)
		=> RunOnDispatcherAsync(() => EnumerateValues(TryGetProperty(_tradeSubscription, "Fills"))
				.Select(ConvertFill)
				.Where(fill => fill != null)
				.ToArray(), cancellationToken);

	private void PublishPositions()
	{
		try
		{
			foreach (var position in GetPositions())
				Publish(PositionReceived, position);
		}
		catch (Exception error)
		{
			Publish(Error, error);
		}
	}

	private void PublishPosition(object value)
	{
		try
		{
			var position = ConvertPosition(value);
			if (position != null)
				Publish(PositionReceived, position);
		}
		catch (Exception error)
		{
			Publish(Error, error);
		}
	}

	private TradingTechnologiesAccount ConvertAccount(object value)
	{
		if (value == null)
			return null;

		var key = TryGetProperty(value, "Key") ?? TryGetProperty(value, "AccountKey");
		var name = GetString(value, "AccountName")
			.IsEmpty(GetString(value, "Name"))
			.IsEmpty(GetString(value, "TTAccountName"));
		if (name.IsEmpty())
			return null;

		return new()
		{
			Id = GetUInt64(value, "AccountId")
				?? GetUInt64(value, "Id")
				?? GetUInt64(key, "Id")
				?? 0,
			Name = name,
			Broker = GetString(value, "BrokerName").IsEmpty(GetString(value, "Company")),
		};
	}

	private TradingTechnologiesOrder ConvertOrder(object value, string forcedStatus = null, string forcedError = null)
	{
		if (value == null)
			return null;

		var instrument = ResolveInstrumentFromValue(value);
		if (instrument == null)
			return null;

		var account = ConvertAccount(TryGetProperty(value, "Account"));
		var siteOrderKey = GetString(value, "SiteOrderKey")
			.IsEmpty(GetString(value, "OrderKey"));
		if (siteOrderKey.IsEmpty())
			return null;

		var orderTag = GetString(value, "OrderTag");
		_ = long.TryParse(orderTag, out var transactionId);
		var volume = GetDecimal(value, "OrderQuantity")
			?? GetDecimal(value, "Quantity")
			?? 0;
		var filled = GetDecimal(value, "FillQuantity")
			?? GetDecimal(value, "FilledQuantity")
			?? 0;
		var balance = GetDecimal(value, "WorkingQuantity")
			?? GetDecimal(value, "LeavesQuantity")
			?? Math.Max(0, volume - filled);
		var status = forcedStatus
			.IsEmpty(GetString(value, "OrdStatus"))
			.IsEmpty(GetString(value, "OrderStatus"))
			.IsEmpty(GetString(value, "Status"));
		var error = forcedError;
		if (error.IsEmpty() && (GetBool(value, "IsRejected") || status.EqualsIgnoreCase("Rejected")))
		{
			error = GetString(value, "RejectReason")
				.IsEmpty(GetString(value, "OrderRejectReason"))
				.IsEmpty(GetString(value, "RejectSource"))
				.IsEmpty("TT rejected the order.");
		}

		return new()
		{
			SiteOrderKey = siteOrderKey,
			ExchangeOrderId = GetString(value, "ExchangeOrderId")
				.IsEmpty(GetString(value, "OrderId")),
			TransactionId = transactionId,
			Instrument = GetInstrumentInfo(instrument),
			Account = account?.Name
				.IsEmpty(GetString(value, "TTAccountName"))
				.IsEmpty(GetString(value, "AccountName")),
			Side = GetString(value, "BuySell").IsEmpty(GetString(value, "Side")),
			OrderType = GetString(value, "OrderType"),
			TimeInForce = GetString(value, "TimeInForce"),
			Status = status,
			PositionEffect = GetString(value, "PositionEffect"),
			Price = GetDecimal(value, "LimitOrderPrice") ?? GetDecimal(value, "LimitPrice"),
			StopPrice = GetDecimal(value, "TriggerPrice") ?? GetDecimal(value, "StopPrice"),
			Volume = volume,
			Balance = balance,
			FilledVolume = filled,
			AveragePrice = GetDecimal(value, "AverageFillPrice"),
			ServerTime = GetDateTime(value, "Received")
				?? GetDateTime(value, "SDKProcess")
				?? GetDateTime(value, "Sent")
				?? DateTime.UtcNow,
			Error = error,
		};
	}

	private TradingTechnologiesFill ConvertFill(object value)
	{
		if (value == null)
			return null;

		var instrument = ResolveInstrumentFromValue(value);
		if (instrument == null)
			return null;

		var account = ConvertAccount(TryGetProperty(value, "Account"));
		var fillId = GetString(value, "FillKey")
			.IsEmpty(GetString(value, "TradingVenueTradeIdCode"))
			.IsEmpty(GetString(value, "ExchangeTransactionNumber"));
		if (fillId.IsEmpty())
			return null;

		return new()
		{
			FillId = fillId,
			SiteOrderKey = GetString(value, "SiteOrderKey").IsEmpty(GetString(value, "OrderKey")),
			Instrument = GetInstrumentInfo(instrument),
			Account = account?.Name
				.IsEmpty(GetString(value, "TTAccountName"))
				.IsEmpty(GetString(value, "AccountName")),
			Side = GetString(value, "BuySell").IsEmpty(GetString(value, "Side")),
			Price = GetDecimal(value, "MatchPrice") ?? GetDecimal(value, "Price") ?? 0,
			Volume = GetDecimal(value, "Quantity") ?? GetDecimal(value, "FillQuantity") ?? 0,
			ServerTime = GetDateTime(value, "TransactionDateTime")
				?? GetDateTime(value, "OCReceivedTime")
				?? GetDateTime(value, "OrderDateTime")
				?? DateTime.UtcNow,
		};
	}

	private TradingTechnologiesPosition ConvertPosition(object value)
	{
		if (value == null)
			return null;
		if (TryGetProperty(value, "Valid") is bool isValid && !isValid)
			return null;

		var account = ConvertAccount(TryGetProperty(value, "Account"));
		if (account == null)
		{
			var accountKey = TryGetProperty(value, "AccountKey");
			account = FindAccountInfo(accountKey);
		}

		var instrument = ResolveInstrumentFromValue(value);
		if (account == null || instrument == null)
			return null;

		return new()
		{
			Account = account,
			Instrument = GetInstrumentInfo(instrument),
			CurrentValue = GetDecimal(value, "NetPosition") ?? GetDecimal(value, "Quantity") ?? 0,
			AveragePrice = GetDecimal(value, "OpenAveragePrice") ?? GetDecimal(value, "AveragePrice") ?? 0,
			UnrealizedPnL = GetDecimal(value, "OpenPL") ?? GetDecimal(value, "UnrealizedPL") ?? 0,
			RealizedPnL = GetDecimal(value, "RealizedPL") ?? 0,
		};
	}

	public Task<string> PlaceOrderAsync(TradingTechnologiesOrderRequest request, CancellationToken cancellationToken)
		=> RunOnDispatcherAsync(() => PlaceOrder(request), cancellationToken);

	private string PlaceOrder(TradingTechnologiesOrderRequest request)
	{
		EnsureOrderEntryReady();
		var instrument = ResolveInstrument(request.InstrumentId, request.Symbol, request.Market);
		var account = FindAccount(request.Account)
			?? throw new InvalidOperationException($"TT account '{request.Account}' was not found.");
		var profile = CreateOrderProfile(instrument);

		try
		{
			SetProperty(profile, "BuySell", ParseEnum("tt_net_sdk.BuySell", request.Side));
			SetProperty(profile, "Account", account);
			SetProperty(profile, "OrderQuantity", CreateQuantity(instrument, request.Volume));
			SetProperty(profile, "OrderType", ParseEnum("tt_net_sdk.OrderType", request.OrderType));
			SetProperty(profile, "TimeInForce", ParseEnum("tt_net_sdk.TimeInForce", request.TimeInForce));
			TrySetProperty(profile, "OrderTag", request.TransactionId.ToString());

			if (!request.PositionEffect.IsEmpty())
				TrySetProperty(profile, "PositionEffect", ParseEnum("tt_net_sdk.PositionEffect", request.PositionEffect));
			if (request.Price is decimal price)
				SetFirstProperty(profile, CreatePrice(instrument, price), "LimitOrderPrice", "LimitPrice");
			if (request.StopPrice is decimal stopPrice)
				SetFirstProperty(profile, CreatePrice(instrument, stopPrice), "StopPrice");
			if (request.TillDate is DateTime tillDate)
			{
				var date = tillDate.ToUniversalTime();
				SetFirstProperty(profile, (ulong)(date.Year * 10000 + date.Month * 100 + date.Day), "ExpireDate");
			}

			var result = Invoke(_tradeSubscription, "SendOrder", profile);
			if (result is bool isSent && !isSent)
				throw new InvalidOperationException("TT rejected the order submission request.");

			var siteOrderKey = GetString(profile, "SiteOrderKey")
				.IsEmpty(GetString(profile, "OrderKey"));
			if (siteOrderKey.IsEmpty())
				throw new InvalidOperationException("TT did not assign a site order key.");
			return siteOrderKey;
		}
		finally
		{
			DisposeObject(profile);
		}
	}

	public ValueTask ReplaceOrderAsync(
		string siteOrderKey,
		long transactionId,
		decimal volume,
		decimal price,
		decimal? stopPrice,
		CancellationToken cancellationToken)
		=> new(RunOnDispatcherAsync(
			() => ChangeOrder(siteOrderKey, transactionId, false, volume, price, stopPrice),
			cancellationToken));

	public ValueTask CancelOrderAsync(string siteOrderKey, long transactionId, CancellationToken cancellationToken)
		=> new(RunOnDispatcherAsync(
			() => ChangeOrder(siteOrderKey, transactionId, true, null, null, null),
			cancellationToken));

	private void ChangeOrder(
		string siteOrderKey,
		long transactionId,
		bool isCancel,
		decimal? volume,
		decimal? price,
		decimal? stopPrice)
	{
		EnsureOrderEntryReady();
		var order = FindOrder(siteOrderKey)
			?? throw new InvalidOperationException($"TT order '{siteOrderKey}' was not found.");
		var profile = Invoke(order, "GetOrderProfile")
			?? throw new InvalidOperationException("TT did not create an order profile.");

		try
		{
			TrySetProperty(profile, "Action", ParseEnum("tt_net_sdk.OrderAction", isCancel ? "Delete" : "Change"));
			TrySetProperty(profile, "OrderTag", transactionId.ToString());

			if (!isCancel)
			{
				var instrument = ResolveInstrumentFromValue(order)
					?? throw new InvalidOperationException("The TT order has no instrument.");
				if (volume is > 0)
					SetFirstProperty(profile, CreateQuantity(instrument, volume.Value), "OrderQuantity", "Quantity");
				if (price is > 0)
					SetFirstProperty(profile, CreatePrice(instrument, price.Value), "LimitOrderPrice", "LimitPrice");
				if (stopPrice is > 0)
					SetFirstProperty(profile, CreatePrice(instrument, stopPrice.Value), "StopPrice");
			}

			var result = Invoke(_tradeSubscription, "SendOrder", profile);
			if (result is bool isSent && !isSent)
				throw new InvalidOperationException(isCancel
					? "TT rejected the cancellation request."
					: "TT rejected the replacement request.");
		}
		finally
		{
			DisposeObject(profile);
		}
	}

	private void EnsureOrderEntryReady()
	{
		EnsureTradeSubscription();
		if (!_isOrderBookSynchronized)
			throw new InvalidOperationException("TT order entry is unavailable until the order book is synchronized.");
	}

	private object ResolveInstrument(ulong? instrumentId, string symbol, string market)
	{
		if (instrumentId is > 0)
			return LookupInstrument(instrumentId.Value);
		if (symbol.IsEmpty())
			throw new InvalidOperationException("A TT instrument ID or symbol is required.");

		var matches = SearchInstruments(symbol, market, 100);
		var match = matches.FirstOrDefault(item => item.Alias.EqualsIgnoreCase(symbol))
			?? matches.FirstOrDefault(item => item.Name.EqualsIgnoreCase(symbol))
			?? matches.FirstOrDefault();
		if (match == null)
			throw new InvalidOperationException($"TT instrument '{symbol}' was not found.");
		return LookupInstrument(match.Id);
	}

	private object ResolveInstrumentFromValue(object value)
	{
		if (value == null)
			return null;

		var instrument = TryGetProperty(value, "Instrument");
		if (instrument != null)
			return instrument;

		var key = TryGetProperty(value, "InstrumentKey") ?? TryGetProperty(value, "Key");
		var id = GetInstrumentId(key);
		return id == 0 ? null : LookupInstrument(id);
	}

	private object LookupInstrument(ulong instrumentId)
	{
		if (_instruments.TryGetValue(instrumentId, out var cached))
			return cached;

		var key = Create(GetSdkType("tt_net_sdk.InstrumentKey"), instrumentId);
		var lookup = Create(GetSdkType("tt_net_sdk.InstrumentLookupSubscription"), _dispatcher, key);
		try
		{
			var result = Invoke(lookup, "Get")?.ToString();
			if (!result.EqualsIgnoreCase("Found"))
				throw new InvalidOperationException($"TT instrument {instrumentId} was not found ({result}).");

			var instrument = GetProperty(lookup, "Instrument")
				?? throw new InvalidOperationException($"TT instrument {instrumentId} lookup returned no instrument.");
			_instruments[instrumentId] = instrument;
			return instrument;
		}
		finally
		{
			DisposeObject(lookup);
		}
	}

	private TradingTechnologiesInstrument GetInstrumentInfo(object instrument)
	{
		var key = TryGetProperty(instrument, "Key") ?? TryGetProperty(instrument, "InstrumentKey");
		var details = TryGetProperty(instrument, "InstrumentDetails") ?? instrument;
		var product = TryGetProperty(instrument, "Product") ?? TryGetProperty(details, "Product");
		var id = GetInstrumentId(key);
		if (id == 0)
			id = GetUInt64(details, "Id") ?? GetUInt64(instrument, "Id") ?? 0;
		if (id == 0)
			throw new InvalidOperationException("TT returned an instrument without an ID.");
		if (_instrumentInfos.TryGetValue(id, out var cached))
			return cached;

		var market = GetString(details, "MarketName")
			.IsEmpty(GetString(key, "MarketId"))
			.IsEmpty(GetString(TryGetProperty(product, "Market"), "Name"))
			.IsEmpty(GetString(TryGetProperty(product, "Market"), "Id"));
		var alias = GetString(details, "Alias")
			.IsEmpty(GetString(instrument, "Alias"))
			.IsEmpty(GetString(details, "Name"));
		var name = GetString(details, "Name").IsEmpty(alias);

		var info = new TradingTechnologiesInstrument
		{
			Id = id,
			Alias = alias,
			Name = name,
			Market = market,
			Product = GetString(product, "Name").IsEmpty(GetString(details, "ProductName")),
			ProductType = GetString(product, "Type").IsEmpty(GetString(details, "ProductType")),
			Currency = GetString(details, "Currency"),
			Isin = GetString(details, "ISIN").IsEmpty(GetString(details, "Isin")),
			BloombergCode = GetString(details, "BloombergCode"),
			TickSize = GetDecimal(details, "TickSize"),
			TickValue = GetDecimal(details, "TickValue"),
			PointValue = GetDecimal(details, "PointValue"),
			LotSize = GetDecimal(details, "LotSize") ?? GetDecimal(details, "QuantityIncrement"),
			MinimumQuantity = GetDecimal(details, "MinQuantity") ?? GetDecimal(details, "MinimumQuantity"),
			ExpirationDate = GetDateTime(details, "ExpirationDate"),
			Strike = GetDecimal(details, "StrikePrice") ?? GetDecimal(details, "Strike"),
			OptionType = GetString(details, "OptionType"),
		};

		_instruments[id] = instrument;
		_instrumentInfos[id] = info;
		return info;
	}

	private ulong GetInstrumentId(object key)
	{
		if (key == null)
			return 0;

		return GetUInt64(key, "InstrumentId")
			?? GetUInt64(key, "Id")
			?? GetUInt64(TryGetProperty(key, "Key"), "Id")
			?? 0;
	}

	private object FindAccount(string name)
	{
		var accounts = EnumerateValues(TryGetProperty(_api, "Accounts")).ToArray();
		if (name.IsEmpty())
			return TryGetProperty(_api, "DefaultAccount") ?? accounts.FirstOrDefault();

		return accounts.FirstOrDefault(value =>
		{
			var account = ConvertAccount(value);
			return account?.Name.EqualsIgnoreCase(name) == true;
		});
	}

	private TradingTechnologiesAccount FindAccountInfo(object accountKey)
	{
		var expected = accountKey?.ToString();
		foreach (var value in EnumerateValues(TryGetProperty(_api, "Accounts")))
		{
			var key = TryGetProperty(value, "Key") ?? TryGetProperty(value, "AccountKey");
			if (ReferenceEquals(key, accountKey) || (!expected.IsEmpty() && key?.ToString().EqualsIgnoreCase(expected) == true))
				return ConvertAccount(value);
		}
		return null;
	}

	private object FindOrder(string siteOrderKey)
		=> EnumerateValues(TryGetProperty(_tradeSubscription, "Orders"))
			.FirstOrDefault(order =>
				GetString(order, "SiteOrderKey").EqualsIgnoreCase(siteOrderKey)
				|| GetString(order, "OrderKey").EqualsIgnoreCase(siteOrderKey));

	private object CreateOrderProfile(object instrument)
	{
		var type = GetSdkType("tt_net_sdk.OrderProfile");
		return TryCreate(type, instrument, false)
			?? TryCreate(type, instrument)
			?? throw new InvalidOperationException("TT did not create an order profile.");
	}

	private object CreateQuantity(object instrument, decimal value)
	{
		var type = GetSdkType("tt_net_sdk.Quantity");
		return TryInvokeStatic(type, "FromDecimal", instrument, value, ParseEnum("tt_net_sdk.Rounding", "None"))
			?? TryCreate(type, value)
			?? throw new InvalidOperationException("TT did not create a quantity value.");
	}

	private object CreatePrice(object instrument, decimal value)
	{
		var type = GetSdkType("tt_net_sdk.Price");
		return TryInvokeStatic(type, "FromDecimal", instrument, value, ParseEnum("tt_net_sdk.Rounding", "None"))
			?? TryCreate(type, value)
			?? throw new InvalidOperationException("TT did not create a price value.");
	}

	public async ValueTask DisconnectAsync()
	{
		if (_workerThread == null || Interlocked.Exchange(ref _isDisconnecting, 1) != 0)
			return;

		try
		{
			if (_dispatcher != null)
			{
				try
				{
					await RunOnDispatcherAsync(CleanupSdk, CancellationToken.None);
				}
				catch (Exception error)
				{
					Publish(Error, error);
					TryShutdownDispatcher();
				}
			}

			var worker = _workerThread;
			if (worker != null && worker != Thread.CurrentThread)
			{
				var isStopped = await Task.Run(() => worker.Join(TimeSpan.FromSeconds(10)));
				if (!isStopped)
				{
					TryShutdownDispatcher();
					await Task.Run(() => worker.Join(TimeSpan.FromSeconds(2)));
				}
			}
		}
		finally
		{
			RemoveShutdownHandler();
			_externalSubscriptions.Clear();
			_priceStates.Clear();
			_tickStates.Clear();
			_instruments.Clear();
			_instrumentInfos.Clear();
			_sdkEvents.Clear();
			_tradeSubscription = null;
			_api = null;
			_dispatcher = null;
			_workerThread = null;
			_initializationHandler = null;
			_shutdownHandler = null;
			_assembly = null;
			_isOrderBookSynchronized = false;
			Interlocked.Exchange(ref _isReady, 0);
		}
	}

	private void CleanupSdk()
	{
		foreach (var state in _priceStates.Values)
		{
			TryRemoveEvent(state.Subscription, "FieldsUpdated", state.Handler);
			DisposeObject(state.Subscription);
		}
		foreach (var state in _tickStates.Values)
		{
			TryRemoveEvent(state.Subscription, "Update", state.Handler);
			DisposeObject(state.Subscription);
		}

		for (var i = _sdkEvents.Count - 1; i >= 0; i--)
		{
			var subscription = _sdkEvents[i];
			TryRemoveEvent(subscription.Target, subscription.Name, subscription.Handler);
		}

		DisposeObject(_tradeSubscription);
		var apiType = GetSdkType("tt_net_sdk.TTAPI");
		var shutdownEvent = apiType.GetEvent("ShutdownCompleted", BindingFlags.Public | BindingFlags.Static)
			?? throw new MissingMemberException(apiType.FullName, "ShutdownCompleted");
		_shutdownHandler = CreateCallback(shutdownEvent.EventHandlerType, _ => CompleteSdkShutdown(shutdownEvent));
		shutdownEvent.AddEventHandler(null, _shutdownHandler);
		try
		{
			if (InvokeStatic(apiType, "Shutdown") is bool isStarted && !isStarted)
				CompleteSdkShutdown(shutdownEvent);
		}
		catch
		{
			CompleteSdkShutdown(shutdownEvent);
			throw;
		}
	}

	private void CompleteSdkShutdown(EventInfo shutdownEvent)
	{
		var handler = Interlocked.Exchange(ref _shutdownHandler, null);
		if (handler != null)
		{
			try
			{
				shutdownEvent.RemoveEventHandler(null, handler);
			}
			catch
			{
			}
		}
		TryShutdownDispatcher();
	}

	private void RemoveShutdownHandler()
	{
		var handler = Interlocked.Exchange(ref _shutdownHandler, null);
		if (handler == null || _assembly == null)
			return;

		try
		{
			_assembly
				.GetType("tt_net_sdk.TTAPI", false)
				?.GetEvent("ShutdownCompleted", BindingFlags.Public | BindingFlags.Static)
				?.RemoveEventHandler(null, handler);
		}
		catch
		{
		}
	}

	private void TryShutdownDispatcher()
	{
		try
		{
			TryInvoke(_dispatcher, "BeginInvokeShutdown");
		}
		catch
		{
		}
	}

	public void Dispose()
	{
		DisconnectAsync().AsTask().GetAwaiter().GetResult();
		GC.SuppressFinalize(this);
	}

	private Task<T> RunOnDispatcherAsync<T>(Func<T> action, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(action);
		cancellationToken.ThrowIfCancellationRequested();
		var dispatcher = _dispatcher ?? throw new InvalidOperationException("The TT worker dispatcher is not running.");
		var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		var registration = cancellationToken.CanBeCanceled
			? cancellationToken.Register(() => source.TrySetCanceled(cancellationToken))
			: default;

		try
		{
			Invoke(dispatcher, "DispatchAction", (Action)(() =>
			{
				if (source.Task.IsCompleted)
					return;
				try
				{
					source.TrySetResult(action());
				}
				catch (Exception error)
				{
					source.TrySetException(error);
				}
			}));
		}
		catch (Exception error)
		{
			source.TrySetException(error);
		}

		return AwaitDispatcherResult(source.Task, registration);
	}

	private async Task RunOnDispatcherAsync(Action action, CancellationToken cancellationToken)
		=> await RunOnDispatcherAsync(() =>
		{
			action();
			return true;
		}, cancellationToken);

	private static async Task<T> AwaitDispatcherResult<T>(Task<T> task, CancellationTokenRegistration registration)
	{
		using (registration)
			return await task;
	}

	private Assembly LoadAssembly(string sdkPath)
	{
		if (!sdkPath.IsEmpty())
		{
			var path = Directory.Exists(sdkPath)
				? Path.Combine(sdkPath, "tt-net-api.dll")
				: sdkPath;
			if (!File.Exists(path))
				throw new FileNotFoundException("TT .NET SDK library was not found.", path);
			return Assembly.LoadFrom(Path.GetFullPath(path));
		}

		var localPath = Path.Combine(AppContext.BaseDirectory, "tt-net-api.dll");
		return File.Exists(localPath)
			? Assembly.LoadFrom(localPath)
			: Assembly.Load("tt-net-api");
	}

	private Type GetSdkType(string name)
		=> _assembly?.GetType(name, false, false)
			?? throw new TypeLoadException($"Type '{name}' was not found in the TT .NET SDK assembly.");

	private Type TryGetSdkType(string name)
		=> _assembly?.GetType(name, false, false);

	private static object Create(Type type, params object[] args)
		=> TryCreate(type, args)
			?? throw new MissingMethodException(type?.FullName, ".ctor");

	private static object TryCreate(Type type, params object[] args)
	{
		if (type == null)
			return null;

		foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
			.Where(item => item.GetParameters().Length == args.Length))
		{
			if (!TryConvertArguments(constructor.GetParameters(), args, out var converted))
				continue;
			try
			{
				return constructor.Invoke(converted);
			}
			catch (TargetInvocationException error)
			{
				throw error.InnerException ?? error;
			}
		}
		return null;
	}

	private static object InvokeStatic(Type type, string name, params object[] args)
		=> InvokeMember(type, null, name, BindingFlags.Public | BindingFlags.Static, args, true);

	private static object TryInvokeStatic(Type type, string name, params object[] args)
		=> type == null ? null : InvokeMember(type, null, name, BindingFlags.Public | BindingFlags.Static, args, false);

	private static object Invoke(object target, string name, params object[] args)
	{
		if (target == null)
			throw new InvalidOperationException($"Cannot invoke '{name}' on a null TT SDK object.");
		return InvokeMember(target.GetType(), target, name, BindingFlags.Public | BindingFlags.Instance, args, true);
	}

	private static object TryInvoke(object target, string name, params object[] args)
		=> target == null
			? null
			: InvokeMember(target.GetType(), target, name, BindingFlags.Public | BindingFlags.Instance, args, false);

	private static object InvokeMember(
		Type type,
		object target,
		string name,
		BindingFlags flags,
		object[] args,
		bool isRequired)
	{
		foreach (var method in type.GetMethods(flags)
			.Where(item => item.Name == name && item.GetParameters().Length == args.Length))
		{
			if (!TryConvertArguments(method.GetParameters(), args, out var converted))
				continue;
			return InvokeMethod(method, target, converted, true);
		}

		if (isRequired)
			throw new MissingMethodException(type.FullName, name);
		return null;
	}

	private static object InvokeMethod(MethodInfo method, object target, object[] args)
	{
		if (!TryConvertArguments(method.GetParameters(), args, out var converted))
			throw new ArgumentException($"Arguments do not match {method.DeclaringType?.FullName}.{method.Name}.", nameof(args));
		return InvokeMethod(method, target, converted, true);
	}

	private static object InvokeMethod(MethodInfo method, object target, object[] args, bool isConverted)
	{
		try
		{
			return method.Invoke(target, args);
		}
		catch (TargetInvocationException error)
		{
			throw error.InnerException ?? error;
		}
	}

	private static bool TryConvertArguments(ParameterInfo[] parameters, object[] args, out object[] converted)
	{
		converted = new object[args.Length];
		for (var i = 0; i < args.Length; i++)
		{
			if (!TryConvertArgument(args[i], parameters[i].ParameterType, out converted[i]))
			{
				converted = null;
				return false;
			}
		}
		return true;
	}

	private static bool TryConvertArgument(object value, Type destinationType, out object converted)
	{
		if (destinationType.IsByRef)
			destinationType = destinationType.GetElementType();
		var underlyingType = Nullable.GetUnderlyingType(destinationType) ?? destinationType;

		if (value == null)
		{
			converted = null;
			return !destinationType.IsValueType || Nullable.GetUnderlyingType(destinationType) != null;
		}
		if (destinationType.IsInstanceOfType(value))
		{
			converted = value;
			return true;
		}

		try
		{
			if (underlyingType.IsEnum)
			{
				converted = value is string text
					? Enum.Parse(underlyingType, text, true)
					: Enum.ToObject(underlyingType, value);
				return true;
			}
			if (underlyingType == typeof(Guid))
			{
				converted = value is Guid guid ? guid : Guid.Parse(value.ToString());
				return true;
			}
			if (underlyingType == typeof(string))
			{
				converted = value.ToString();
				return true;
			}
			if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(underlyingType))
			{
				converted = Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
				return true;
			}
		}
		catch
		{
		}

		converted = null;
		return false;
	}

	private static object GetProperty(object target, string name)
		=> TryGetProperty(target, name)
			?? throw new MissingMemberException(target?.GetType().FullName, name);

	private static object TryGetProperty(object target, string name)
	{
		if (target == null || name.IsEmpty())
			return null;
		try
		{
			return target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
		}
		catch (TargetInvocationException error)
		{
			throw error.InnerException ?? error;
		}
	}

	private static void SetProperty(object target, string name, object value)
	{
		if (!TrySetProperty(target, name, value))
			throw new MissingMemberException(target?.GetType().FullName, name);
	}

	private static bool TrySetProperty(object target, string name, object value)
	{
		if (target == null || name.IsEmpty())
			return false;
		var property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
		if (property?.CanWrite != true || !TryConvertArgument(value, property.PropertyType, out var converted))
			return false;
		try
		{
			property.SetValue(target, converted);
			return true;
		}
		catch (TargetInvocationException error)
		{
			throw error.InnerException ?? error;
		}
	}

	private static void SetFirstProperty(object target, object value, params string[] names)
	{
		foreach (var name in names)
		{
			if (TrySetProperty(target, name, value))
				return;
		}
		throw new MissingMemberException(target?.GetType().FullName, string.Join(" or ", names));
	}

	private object ParseEnum(string typeName, string value)
	{
		var type = GetSdkType(typeName);
		try
		{
			return Enum.Parse(type, value, true);
		}
		catch (ArgumentException)
		{
			var normalized = value?.Replace("_", string.Empty).Replace("-", string.Empty);
			var name = Enum.GetNames(type).FirstOrDefault(item =>
				item.Replace("_", string.Empty).EqualsIgnoreCase(normalized));
			if (name == null)
				throw;
			return Enum.Parse(type, name, false);
		}
	}

	private object CreateMarketList(string market)
	{
		var marketType = GetSdkType("tt_net_sdk.MarketId");
		var listType = typeof(List<>).MakeGenericType(marketType);
		var list = (IList)Activator.CreateInstance(listType);
		if (!market.IsEmpty())
			list.Add(ParseEnum("tt_net_sdk.MarketId", market));
		return list;
	}

	private static decimal? ReadField(object fields, string method, params object[] args)
	{
		var field = TryInvoke(fields, method, args);
		if (field == null)
			return null;
		var hasValidValue = TryGetProperty(field, "HasValidValue");
		if (hasValidValue is bool isValid && !isValid)
			return null;
		return ToDecimal(TryGetProperty(field, "Value") ?? field);
	}

	private static string ReadFieldText(object fields, string method, params object[] args)
	{
		var field = TryInvoke(fields, method, args);
		if (field == null)
			return null;
		var hasValidValue = TryGetProperty(field, "HasValidValue");
		if (hasValidValue is bool isValid && !isValid)
			return null;
		return (TryGetProperty(field, "Value") ?? field).ToString();
	}

	private static string GetString(object target, string name)
		=> TryGetProperty(target, name)?.ToString();

	private static bool GetBool(object target, string name)
	{
		var value = TryGetProperty(target, name);
		if (value is bool result)
			return result;
		return value != null && bool.TryParse(value.ToString(), out result) && result;
	}

	private static ulong? GetUInt64(object target, string name)
	{
		var value = TryGetProperty(target, name);
		if (value == null)
			return null;
		try
		{
			return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
		}
		catch
		{
			return ulong.TryParse(value.ToString(), out var result) ? result : null;
		}
	}

	private static long? GetInt64(object target, string name)
	{
		var value = TryGetProperty(target, name);
		if (value == null)
			return null;
		try
		{
			return Convert.ToInt64(value, CultureInfo.InvariantCulture);
		}
		catch
		{
			return long.TryParse(value.ToString(), out var result) ? result : null;
		}
	}

	private static int? GetInt32(object value)
	{
		if (value == null)
			return null;
		try
		{
			return Convert.ToInt32(value, CultureInfo.InvariantCulture);
		}
		catch
		{
			return int.TryParse(value.ToString(), out var result) ? result : null;
		}
	}

	private static decimal? GetDecimal(object target, string name)
		=> ToDecimal(TryGetProperty(target, name));

	private static decimal? ToDecimal(object value)
	{
		if (value == null)
			return null;
		if (value is decimal decimalValue)
			return decimalValue;

		var nested = TryGetProperty(value, "Value");
		if (nested != null && !ReferenceEquals(nested, value))
			return ToDecimal(nested);
		try
		{
			return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
		}
		catch
		{
			return decimal.TryParse(
				value.ToString(),
				NumberStyles.Any,
				CultureInfo.InvariantCulture,
				out decimalValue)
				? decimalValue
				: null;
		}
	}

	private static DateTime? GetDateTime(object target, string name)
	{
		var value = TryGetProperty(target, name);
		if (value == null)
			return null;
		if (value is DateTime dateTime)
			return ToUtc(dateTime);

		var nested = TryGetProperty(value, "DateTime") ?? TryGetProperty(value, "Value");
		if (nested is DateTime nestedDateTime)
			return ToUtc(nestedDateTime);
		return DateTime.TryParse(
			value.ToString(),
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out dateTime)
			? dateTime
			: null;
	}

	private static DateTime ToUtc(DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
		};

	private static DateTime? NanosecondsToUtc(long? value)
	{
		if (value is not > 0)
			return null;
		try
		{
			return DateTime.UnixEpoch.AddTicks(value.Value / 100);
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
		}
	}

	private static DateTime? MicrosecondsToUtc(long value)
	{
		if (value <= 0)
			return null;
		try
		{
			return DateTime.UnixEpoch.AddTicks(value * 10);
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
		}
		catch (OverflowException)
		{
			return null;
		}
	}

	private static bool TryCreateEventError(object eventArgs, out Exception error)
	{
		var value = TryGetProperty(eventArgs, "Error") ?? TryGetProperty(eventArgs, "Exception");
		if (value is Exception exception)
		{
			error = exception;
			return true;
		}

		var text = value?.ToString();
		if (text.IsEmpty() || text.EqualsIgnoreCase("None") || text.EqualsIgnoreCase("NoError") || text.EqualsIgnoreCase("Success"))
		{
			error = null;
			return false;
		}

		error = new InvalidOperationException(text);
		return true;
	}

	private void TrackEvent(object target, string name, Action<object[]> callback)
	{
		var handler = AddEvent(target, name, callback);
		_sdkEvents.Add((target, name, handler));
	}

	private static Delegate AddEvent(object target, string name, Action<object[]> callback)
	{
		var eventInfo = target?.GetType().GetEvent(name, BindingFlags.Public | BindingFlags.Instance)
			?? throw new MissingMemberException(target?.GetType().FullName, name);
		var handler = CreateCallback(eventInfo.EventHandlerType, callback);
		eventInfo.AddEventHandler(target, handler);
		return handler;
	}

	private static void RemoveEvent(object target, string name, Delegate handler)
	{
		if (target == null || handler == null)
			return;
		var eventInfo = target.GetType().GetEvent(name, BindingFlags.Public | BindingFlags.Instance)
			?? throw new MissingMemberException(target.GetType().FullName, name);
		eventInfo.RemoveEventHandler(target, handler);
	}

	private static void TryRemoveEvent(object target, string name, Delegate handler)
	{
		try
		{
			RemoveEvent(target, name, handler);
		}
		catch
		{
		}
	}

	private static Delegate CreateCallback(Type delegateType, Action<object[]> callback)
	{
		var invoke = delegateType?.GetMethod("Invoke")
			?? throw new ArgumentException("The TT SDK event has no delegate signature.", nameof(delegateType));
		var parameters = invoke.GetParameters()
			.Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
			.ToArray();
		var values = Expression.NewArrayInit(
			typeof(object),
			parameters.Select(parameter => Expression.Convert(parameter, typeof(object))));
		var callbackExpression = callback.Target == null
			? Expression.Call(callback.Method, values)
			: Expression.Call(Expression.Constant(callback.Target), callback.Method, values);
		Expression body = invoke.ReturnType == typeof(void)
			? callbackExpression
			: Expression.Block(callbackExpression, Expression.Default(invoke.ReturnType));
		return Expression.Lambda(delegateType, body, parameters).Compile();
	}

	private static IEnumerable<object> Enumerate(object value)
	{
		if (value is not IEnumerable collection || value is string)
			yield break;
		foreach (var item in collection)
			yield return item;
	}

	private static IEnumerable<object> EnumerateValues(object value)
	{
		if (value is IDictionary dictionary)
		{
			foreach (DictionaryEntry item in dictionary)
				yield return item.Value;
			yield break;
		}

		foreach (var item in Enumerate(value))
		{
			var itemValue = TryGetProperty(item, "Value");
			yield return itemValue ?? item;
		}
	}

	private static void DisposeObject(object value)
	{
		if (value is IDisposable disposable)
			disposable.Dispose();
	}

	private static void Publish<T>(Func<T, CancellationToken, ValueTask> callbacks, T value)
	{
		if (callbacks == null)
			return;
		foreach (Func<T, CancellationToken, ValueTask> callback in callbacks.GetInvocationList())
			_ = InvokeCallback(callback, value);
	}

	private static async Task InvokeCallback<T>(Func<T, CancellationToken, ValueTask> callback, T value)
	{
		try
		{
			await callback(value, CancellationToken.None);
		}
		catch
		{
		}
	}

	private static void Publish(
		Func<bool, string, CancellationToken, ValueTask> callbacks,
		bool isReady,
		string status)
	{
		if (callbacks == null)
			return;
		foreach (Func<bool, string, CancellationToken, ValueTask> callback in callbacks.GetInvocationList())
			_ = InvokeCallback(callback, isReady, status);
	}

	private static async Task InvokeCallback(
		Func<bool, string, CancellationToken, ValueTask> callback,
		bool isReady,
		string status)
	{
		try
		{
			await callback(isReady, status, CancellationToken.None);
		}
		catch
		{
		}
	}
}
