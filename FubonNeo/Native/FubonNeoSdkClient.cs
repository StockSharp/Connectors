namespace StockSharp.FubonNeo.Native;

sealed class FubonNeoSdkClient : BaseLogReceiver
{
	private sealed class NativeAccount
	{
		public object Value { get; init; }
		public FubonNeoAccountInfo Info { get; init; }
	}

	private sealed class NativeOrder
	{
		public object Value { get; set; }
		public NativeAccount Account { get; init; }
		public bool IsFutures { get; init; }
	}

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly string _sdkPath;
	private readonly string _personalId;
	private readonly string _password;
	private readonly string _apiKey;
	private readonly bool _isApiKeyLogin;
	private readonly string _certificatePath;
	private readonly string _certificatePassword;
	private readonly string _environmentUrl;
	private readonly FubonNeoRealtimeModes _realtimeMode;
	private readonly int _reconnectAttempts;
	private readonly SemaphoreSlim _sdkSync = new(1, 1);
	private readonly SemaphoreSlim _securitiesSync = new(1, 1);
	private readonly SemaphoreSlim _stockSocketSync = new(1, 1);
	private readonly SemaphoreSlim _futuresSocketSync = new(1, 1);
	private readonly ConcurrentDictionary<string, NativeAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, NativeOrder> _orders = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, FubonNeoSubscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, FubonNeoSubscription> _serverSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly Queue<string> _stockPending = [];
	private readonly Queue<string> _futuresPending = [];
	private readonly object _stockPendingSync = new();
	private readonly object _futuresPendingSync = new();
	private FubonNeoSdkBridge _bridge;
	private CancellationTokenSource _lifetime;
	private FubonNeoSecurityInfo[] _securities;
	private bool _isStockSocketConnected;
	private bool _isFuturesSocketConnected;
	private int _isStockReconnecting;
	private int _isFuturesReconnecting;
	private int _isDisconnecting;

	public FubonNeoSdkClient(string sdkPath, string personalId, SecureString password, SecureString apiKey,
		bool isApiKeyLogin, string certificatePath, SecureString certificatePassword, string environmentUrl,
		FubonNeoRealtimeModes realtimeMode, int reconnectAttempts)
	{
		_sdkPath = sdkPath;
		_personalId = personalId.ThrowIfEmpty(nameof(personalId));
		_password = password?.UnSecure();
		_apiKey = apiKey?.UnSecure();
		_isApiKeyLogin = isApiKeyLogin;
		_certificatePath = certificatePath.ThrowIfEmpty(nameof(certificatePath));
		_certificatePassword = certificatePassword?.UnSecure();
		_environmentUrl = environmentUrl;
		_realtimeMode = realtimeMode;
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(FubonNeo) + "_" + nameof(FubonNeoSdkClient);

	public event Func<FubonNeoSubscription, FubonNeoStreamData, CancellationToken, ValueTask> MarketDataReceived;
	public event Func<FubonNeoOrderUpdate, CancellationToken, ValueTask> OrderReceived;
	public event Func<FubonNeoFillUpdate, CancellationToken, ValueTask> FillReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<Exception, CancellationToken, ValueTask> ConnectionLost;

	public string Version => _bridge?.Version;
	public FubonNeoAccountInfo[] Accounts => [.. _accounts.Values.Select(item => item.Info)
		.GroupBy(item => item.PortfolioName, StringComparer.OrdinalIgnoreCase).Select(group => group.First())];

	public async Task ConnectAsync(CancellationToken cancellationToken)
	{
		if (_bridge != null)
			throw new InvalidOperationException("The Fubon SDK is already connected.");
		if (_reconnectAttempts < 0)
			throw new ArgumentOutOfRangeException(nameof(_reconnectAttempts), _reconnectAttempts,
				"Reconnect attempts cannot be negative.");
		var credential = _isApiKeyLogin ? _apiKey : _password;
		if (credential.IsEmpty())
			throw new InvalidOperationException(_isApiKeyLogin
				? "The Fubon Neo API key is not specified."
				: LocalizedStrings.PasswordNotSpecified);

		Interlocked.Exchange(ref _isDisconnecting, 0);
		_lifetime = new();
		var bridge = new FubonNeoSdkBridge(_sdkPath, _environmentUrl);
		AttachBridge(bridge);
		_bridge = bridge;
		try
		{
			var loginResponse = await RunSdkAsync(() => bridge.Login(
				_personalId, credential, _certificatePath, _certificatePassword, _isApiKeyLogin), cancellationToken);
			var nativeAccounts = bridge.ReadList(loginResponse, "login");
			if (nativeAccounts.Length == 0)
				throw new InvalidOperationException("Fubon login succeeded but returned no trading accounts.");
			foreach (var native in nativeAccounts)
			{
				var account = ReadAccount(native);
				_accounts[account.PortfolioName] = new() { Value = native, Info = account };
			}

			await RunSdkAsync(() =>
			{
				bridge.InitializeRealtime(_realtimeMode);
				var recovery = bridge.RecoverEventData();
				bridge.EnsureSuccess(recovery, "event recovery");
				return true;
			}, cancellationToken);
		}
		catch
		{
			await DisconnectCoreAsync();
			throw;
		}
	}

	public async Task DisconnectAsync()
	{
		if (Interlocked.Exchange(ref _isDisconnecting, 1) != 0)
			return;
		await DisconnectCoreAsync();
	}

	public async Task<FubonNeoSecurityInfo[]> GetSecuritiesAsync(CancellationToken cancellationToken)
	{
		if (_securities != null)
			return _securities;
		await _securitiesSync.WaitAsync(cancellationToken);
		try
		{
			if (_securities != null)
				return _securities;
			var result = new List<FubonNeoSecurityInfo>();
			foreach (var type in new[] { "Equity", "Index", "Warrant" })
			{
				var page = Deserialize<FubonNeoTickerListResponse>(
					await EnsureBridge().GetTickerListAsync(FubonNeoAssetKinds.Stock, type));
				foreach (var ticker in page.Data ?? [])
				{
					if (ticker?.Symbol.IsEmpty() != false)
						continue;
					result.Add(new()
					{
						Kind = FubonNeoAssetKinds.Stock,
						TickerType = ticker.Type.IsEmpty(page.Type).IsEmpty(type),
						Exchange = ticker.Exchange.IsEmpty(page.Exchange),
						Market = ticker.Market.IsEmpty(page.Market),
						Symbol = ticker.Symbol,
						Name = ticker.Name,
						ReferencePrice = ticker.ReferencePrice,
					});
				}
			}

			foreach (var type in new[] { "Future", "Option" })
			{
				foreach (var session in new[] { "Regular", "AfterHours" })
				{
					var page = Deserialize<FubonNeoTickerListResponse>(
						await EnsureBridge().GetTickerListAsync(FubonNeoAssetKinds.FuturesOptions, type, session));
					foreach (var ticker in page.Data ?? [])
					{
						if (ticker?.Symbol.IsEmpty() != false)
							continue;
						result.Add(new()
						{
							Kind = FubonNeoAssetKinds.FuturesOptions,
							TickerType = ticker.Type.IsEmpty(page.Type).IsEmpty(type),
							Exchange = ticker.Exchange.IsEmpty(page.Exchange).IsEmpty("TAIFEX"),
							Market = ticker.Market.IsEmpty(page.Market),
							Session = page.Session.IsEmpty(session.ToUpperInvariant()),
							Symbol = ticker.Symbol,
							Name = ticker.Name,
							ContractType = ticker.ContractType.IsEmpty(page.ContractType),
							ReferencePrice = ticker.ReferencePrice,
							StartDate = ticker.StartDate,
							EndDate = ticker.EndDate,
							SettlementDate = ticker.SettlementDate,
						});
					}
				}
			}

			_securities = [.. result.GroupBy(item => item.ToNativeKey(), StringComparer.OrdinalIgnoreCase)
				.Select(group => group.First())];
			return _securities;
		}
		finally
		{
			_securitiesSync.Release();
		}
	}

	public async Task<FubonNeoCandle[]> GetCandlesAsync(FubonNeoSecurityInfo security, TimeSpan timeFrame,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var json = await EnsureBridge().GetCandlesAsync(security, timeFrame, from, to);
		return Deserialize<FubonNeoCandleResponse>(json).Data ?? [];
	}

	public async Task SubscribeAsync(FubonNeoSubscription subscription, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(subscription);
		subscription.Channel.ThrowIfEmpty(nameof(subscription.Channel));
		subscription.Symbol.ThrowIfEmpty(nameof(subscription.Symbol));
		if (_subscriptions.TryGetValue(subscription.Key, out var existing))
		{
			existing.TransactionId = subscription.TransactionId;
			existing.SecurityId = subscription.SecurityId;
			return;
		}
		_subscriptions[subscription.Key] = subscription;
		try
		{
			await EnsureSocketConnectedAsync(subscription.Kind, cancellationToken);
			await SendSubscriptionAsync(subscription);
		}
		catch
		{
			_subscriptions.Remove(subscription.Key);
			throw;
		}
	}

	public async Task UnsubscribeAsync(FubonNeoSubscription subscription)
	{
		if (!_subscriptions.TryGetAndRemove(subscription.Key, out var existing))
			return;
		if (!existing.ServerId.IsEmpty())
		{
			_serverSubscriptions.Remove(GetServerKey(existing.Kind, existing.ServerId));
			await EnsureBridge().UnsubscribeAsync(existing.Kind, existing.ServerId);
		}
	}

	public async Task SendHeartbeatAsync(CancellationToken cancellationToken)
	{
		var bridge = EnsureBridge();
		if (_isStockSocketConnected)
			await bridge.PingSocketAsync(FubonNeoAssetKinds.Stock);
		if (_isFuturesSocketConnected)
			await bridge.PingSocketAsync(FubonNeoAssetKinds.FuturesOptions);
		cancellationToken.ThrowIfCancellationRequested();
	}

	public Task<FubonNeoOrderUpdate> PlaceOrderAsync(FubonNeoOrderRequest request,
		CancellationToken cancellationToken)
		=> RunSdkAsync(() => PlaceOrder(request), cancellationToken);

	public async Task<FubonNeoOrderUpdate> ReplaceOrderAsync(string orderId, decimal? price, long? volume,
		CancellationToken cancellationToken)
	{
		var native = await ResolveOrderAsync(orderId, cancellationToken);
		return await RunSdkAsync(() =>
		{
			var bridge = EnsureBridge();
			var functions = native.IsFutures ? bridge.FutOpt : bridge.Stock;
			var result = native.Value;
			if (price is > 0)
			{
				var priceObject = bridge.Call(functions, "MakeModifyPriceObj", result,
					price.Value.ToString(CultureInfo.InvariantCulture), null);
				var response = bridge.Call(functions, "ModifyPrice", native.Account.Value, priceObject, null);
				result = bridge.ReadData(response, "order price modification");
			}
			if (volume is > 0)
			{
				var volumeObject = bridge.Call(functions,
					native.IsFutures ? "MakeModifyLotObj" : "MakeModifyQuantityObj", result, volume.Value);
				var response = bridge.Call(functions,
					native.IsFutures ? "ModifyLot" : "ModifyQuantity", native.Account.Value, volumeObject, null);
				result = bridge.ReadData(response, "order quantity modification");
			}
			native.Value = result;
			CacheOrder(native);
			return ReadOrder(result, native.IsFutures);
		}, cancellationToken);
	}

	public async Task<FubonNeoOrderUpdate> CancelOrderAsync(string orderId,
		CancellationToken cancellationToken)
	{
		var native = await ResolveOrderAsync(orderId, cancellationToken);
		return await RunSdkAsync(() =>
		{
			var bridge = EnsureBridge();
			var response = bridge.Call(native.IsFutures ? bridge.FutOpt : bridge.Stock,
				"CancelOrder", native.Account.Value, native.Value, null);
			var result = bridge.ReadData(response, "order cancellation");
			native.Value = result;
			CacheOrder(native);
			return ReadOrder(result, native.IsFutures);
		}, cancellationToken);
	}

	public Task<FubonNeoOrderUpdate[]> GetOrdersAsync(CancellationToken cancellationToken)
		=> RunSdkAsync(GetOrders, cancellationToken);

	public Task<FubonNeoFillUpdate[]> GetFillsAsync(CancellationToken cancellationToken)
		=> RunSdkAsync(GetFills, cancellationToken);

	public Task<FubonNeoPositionInfo[]> GetPositionsAsync(string portfolioName,
		CancellationToken cancellationToken)
		=> RunSdkAsync(() => GetPositions(portfolioName), cancellationToken);

	public Task<FubonNeoCashInfo[]> GetCashAsync(string portfolioName, CancellationToken cancellationToken)
		=> RunSdkAsync(() => GetCash(portfolioName), cancellationToken);

	protected override void DisposeManaged()
	{
		try
		{
			DisconnectAsync().GetAwaiter().GetResult();
		}
		catch
		{
		}
		_sdkSync.Dispose();
		_securitiesSync.Dispose();
		_stockSocketSync.Dispose();
		_futuresSocketSync.Dispose();
		base.DisposeManaged();
	}

	private FubonNeoOrderUpdate PlaceOrder(FubonNeoOrderRequest request)
	{
		var bridge = EnsureBridge();
		var account = GetAccount(request.PortfolioName, request.Kind);
		object order;
		object response;
		if (request.Kind == FubonNeoAssetKinds.Stock)
		{
			order = bridge.CreateSdkObject("FubonNeo.Sdk.Order",
				bridge.EnumValue("FubonNeo.Sdk.BsAction", request.Side == Sides.Buy ? "Buy" : "Sell"),
				request.Symbol,
				request.PriceType is FubonNeoPriceTypes.Market or FubonNeoPriceTypes.LimitUp or
					FubonNeoPriceTypes.LimitDown or FubonNeoPriceTypes.Reference
					? null : request.Price.ToString(CultureInfo.InvariantCulture),
				request.Volume,
				bridge.EnumValue("FubonNeo.Sdk.MarketType", ToSdkStockMarketType(request.StockMarketType)),
				bridge.EnumValue("FubonNeo.Sdk.PriceType", ToSdkPriceType(request.PriceType, false)),
				bridge.EnumValue("FubonNeo.Sdk.TimeInForce", ToSdkTimeInForce(request.TimeInForce)),
				bridge.EnumValue("FubonNeo.Sdk.OrderType", request.StockOrderType.ToString()),
				request.UserTag);
			response = bridge.Call(bridge.Stock, "PlaceOrder", account.Value, order, null);
		}
		else
		{
			var marketType = request.SecurityType == SecurityTypes.Option ? "Option" : "Future";
			if (request.IsAfterHours)
				marketType += "Night";
			order = bridge.CreateSdkObject("FubonNeo.Sdk.FutOptOrder",
				bridge.EnumValue("FubonNeo.Sdk.BsAction", request.Side == Sides.Buy ? "Buy" : "Sell"),
				request.Symbol, null, null,
				request.PriceType is FubonNeoPriceTypes.Market or FubonNeoPriceTypes.RangeMarket or
					FubonNeoPriceTypes.Reference ? null : request.Price.ToString(CultureInfo.InvariantCulture),
				request.Volume,
				bridge.EnumValue("FubonNeo.Sdk.FutOptMarketType", marketType),
				bridge.EnumValue("FubonNeo.Sdk.FutOptPriceType", ToSdkPriceType(request.PriceType, true)),
				bridge.EnumValue("FubonNeo.Sdk.TimeInForce", ToSdkTimeInForce(request.TimeInForce)),
				bridge.EnumValue("FubonNeo.Sdk.FutOptOrderType", ToSdkFuturesOrderType(request.FuturesOrderType)),
				request.UserTag);
			response = bridge.Call(bridge.FutOpt, "PlaceOrder", account.Value, order, null);
		}

		var data = bridge.ReadData(response, "order registration");
		var native = new NativeOrder { Value = data, Account = account, IsFutures = request.Kind == FubonNeoAssetKinds.FuturesOptions };
		CacheOrder(native);
		return ReadOrder(data, native.IsFutures);
	}

	private FubonNeoOrderUpdate[] GetOrders()
	{
		var bridge = EnsureBridge();
		var result = new List<FubonNeoOrderUpdate>();
		foreach (var account in _accounts.Values.DistinctBy(item => item.Info.PortfolioName))
		{
			var response = account.Info.IsFutures
				? bridge.Call(bridge.FutOpt, "GetOrderResultsDetail", account.Value, null)
				: bridge.Call(bridge.Stock, "GetOrderResultsDetail", account.Value);
			foreach (var data in bridge.ReadList(response, "order query"))
			{
				var native = new NativeOrder { Value = data, Account = account, IsFutures = account.Info.IsFutures };
				CacheOrder(native);
				result.Add(ReadOrder(data, native.IsFutures));
			}
		}
		return [.. result];
	}

	private FubonNeoFillUpdate[] GetFills()
	{
		var bridge = EnsureBridge();
		var result = new List<FubonNeoFillUpdate>();
		var date = DateTime.UtcNow.AddHours(8).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		foreach (var account in _accounts.Values.DistinctBy(item => item.Info.PortfolioName))
		{
			if (!account.Info.IsFutures)
			{
				var response = bridge.Call(bridge.Stock, "FilledHistory", account.Value, null, null);
				result.AddRange(bridge.ReadList(response, "filled-order query").Select(data => ReadFill(data, false)));
				continue;
			}

			foreach (var marketType in new[] { "Future", "Option", "FutureNight", "OptionNight" })
			{
				try
				{
					var response = bridge.Call(bridge.FutOpt, "FilledHistory", account.Value,
						bridge.EnumValue("FubonNeo.Sdk.FutOptMarketType", marketType), date, null);
					result.AddRange(bridge.ReadList(response, $"{marketType} filled-order query")
						.Select(data => ReadFill(data, true)));
				}
				catch (InvalidOperationException error)
				{
					this.AddWarningLog("Fubon {0} fill query was unavailable: {1}", marketType, error.Message);
				}
			}
		}
		return [.. result.GroupBy(item => $"{item.OrderId}|{item.FillId}", StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())];
	}

	private FubonNeoPositionInfo[] GetPositions(string portfolioName)
	{
		var bridge = EnsureBridge();
		var result = new List<FubonNeoPositionInfo>();
		foreach (var account in FilterAccounts(portfolioName))
		{
			if (account.Info.IsFutures)
			{
				var response = bridge.Call(bridge.FutOptAccounting, "QuerySinglePosition", account.Value);
				foreach (var data in bridge.ReadList(response, "futures/options position query"))
				{
					var side = Text(data, "buySell");
					var volume = Number(data, "tradableLot") ?? Number(data, "origLots") ?? 0;
					result.Add(new()
					{
						IsFutures = true,
						IsOption = bridge.Property(data, "callPut") != null || bridge.Property(data, "strikePrice") != null,
						PortfolioName = account.Info.PortfolioName,
						Symbol = Text(data, "symbol"),
						Side = side,
						CurrentValue = side.EqualsIgnoreCase("Sell") ? -volume : volume,
						AveragePrice = Number(data, "price"),
						CurrentPrice = ParseNumber(Text(data, "marketPrice")),
						UnrealizedPnL = Number(data, "profitOrLoss"),
						BlockedValue = Number(data, "initialMargin"),
						OrderType = Text(data, "orderType"),
					});
				}
			}
			else
			{
				var response = bridge.Call(bridge.Accounting, "UnrealizedGainsAndLoses", account.Value);
				foreach (var data in bridge.ReadList(response, "stock position query"))
				{
					var side = Text(data, "buySell");
					var volume = (Number(data, "tradableQty") ?? 0) + (Number(data, "todayQty") ?? 0);
					result.Add(new()
					{
						PortfolioName = account.Info.PortfolioName,
						Symbol = Text(data, "stockNo"),
						Side = side,
						CurrentValue = side.EqualsIgnoreCase("Sell") ? -volume : volume,
						AveragePrice = Number(data, "costPrice"),
						UnrealizedPnL = (Number(data, "unrealizedProfit") ?? 0) - (Number(data, "unrealizedLoss") ?? 0),
						OrderType = Text(data, "orderType"),
					});
				}
			}
		}
		return [.. result];
	}

	private FubonNeoCashInfo[] GetCash(string portfolioName)
	{
		var bridge = EnsureBridge();
		var result = new List<FubonNeoCashInfo>();
		foreach (var account in FilterAccounts(portfolioName))
		{
			if (account.Info.IsFutures)
			{
				var response = bridge.Call(bridge.FutOptAccounting, "QueryMarginEquity", account.Value);
				foreach (var data in bridge.ReadList(response, "futures/options equity query"))
				{
					result.Add(new()
					{
						PortfolioName = account.Info.PortfolioName,
						Currency = Text(data, "currency").IsEmpty("TWD"),
						CurrentValue = Number(data, "todayEquity") ?? 0,
						AvailableValue = Number(data, "availableMargin"),
						BlockedValue = Number(data, "initialMargin"),
						UnrealizedPnL = (Number(data, "futUnrealizedPnl") ?? 0) + (Number(data, "optPnl") ?? 0),
					});
				}
			}
			else
			{
				var response = bridge.Call(bridge.Accounting, "BankRemain", account.Value);
				var data = bridge.ReadData(response, "bank balance query");
				if (data != null)
				{
					result.Add(new()
					{
						PortfolioName = account.Info.PortfolioName,
						Currency = Text(data, "currency").IsEmpty("TWD"),
						CurrentValue = Number(data, "balance") ?? 0,
						AvailableValue = Number(data, "availableBalance"),
					});
				}
			}
		}
		return [.. result];
	}

	private async Task<NativeOrder> ResolveOrderAsync(string orderId, CancellationToken cancellationToken)
	{
		orderId.ThrowIfEmpty(nameof(orderId));
		if (_orders.TryGetValue(orderId, out var native))
			return native;
		_ = await GetOrdersAsync(cancellationToken);
		return _orders.TryGetValue(orderId, out native)
			? native
			: throw new InvalidOperationException($"Fubon order '{orderId}' was not found.");
	}

	private void CacheOrder(NativeOrder native)
	{
		var orderId = Text(native.Value, "orderNo");
		var sequence = Text(native.Value, "seqNo");
		if (!orderId.IsEmpty())
			_orders[orderId] = native;
		if (!sequence.IsEmpty())
			_orders[sequence] = native;
	}

	private FubonNeoOrderUpdate ReadOrder(object data, bool isFutures)
	{
		var volume = Number(data, isFutures ? "lot" : "quantity") ?? 0;
		var afterVolume = Number(data, isFutures ? "afterLot" : "afterQty");
		return new()
		{
			IsFutures = isFutures,
			OrderId = Text(data, "orderNo").IsEmpty(Text(data, "seqNo")),
			Sequence = Text(data, "seqNo"),
			Symbol = Text(data, isFutures ? "symbol" : "stockNo"),
			PortfolioName = $"{Text(data, "branchNo")}-{Text(data, "account")}",
			Market = Text(data, "market"),
			AssetType = Integer(data, "assetType"),
			MarketType = Text(data, "marketType"),
			Side = Text(data, "buySell"),
			PriceType = Text(data, "afterPriceType").IsEmpty(Text(data, "priceType")),
			OrderType = Text(data, "orderType"),
			TimeInForce = Text(data, "timeInForce"),
			Price = Number(data, "afterPrice") ?? Number(data, "price"),
			Volume = decimal.ToInt64(afterVolume ?? volume),
			FilledVolume = decimal.ToInt64(Number(data, isFutures ? "filledLot" : "filledQty") ?? 0),
			FilledMoney = Number(data, "filledMoney"),
			Status = Integer(data, "status"),
			FunctionType = Integer(data, "functionType"),
			IsPreOrder = Boolean(data, "isPreOrder"),
			Error = Text(data, "errorMessage"),
			Date = Text(data, "date"),
			LastTime = Text(data, "lastTime"),
			UserTag = Text(data, "userDef"),
		};
	}

	private FubonNeoFillUpdate ReadFill(object data, bool isFutures)
		=> new()
		{
			IsFutures = isFutures,
			IsOption = isFutures && _bridge?.Property(data, "callPut") != null,
			OrderId = Text(data, "orderNo").IsEmpty(Text(data, "seqNo")),
			Sequence = Text(data, "seqNo"),
			FillId = Text(data, "filledNo"),
			Symbol = Text(data, isFutures ? "symbol" : "stockNo"),
			PortfolioName = $"{Text(data, "branchNo")}-{Text(data, "account")}",
			Side = Text(data, "buySell"),
			Price = Number(data, "filledPrice") ?? 0,
			Volume = decimal.ToInt64(Number(data, isFutures ? "filledLot" : "filledQty") ?? 0),
			AveragePrice = Number(data, "filledAvgPrice") ?? 0,
			Date = Text(data, "date"),
			Time = Text(data, "filledTime"),
			UserTag = Text(data, "userDef"),
		};

	private FubonNeoAccountInfo ReadAccount(object data)
		=> new()
		{
			Name = Text(data, "name"),
			BranchNo = Text(data, "branchNo"),
			Account = Text(data, "account"),
			AccountType = Text(data, "accountType"),
		};

	private NativeAccount GetAccount(string portfolioName, FubonNeoAssetKinds kind)
	{
		if (!portfolioName.IsEmpty())
			return _accounts.TryGetValue(portfolioName, out var account)
				? account
				: throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
		var isFutures = kind == FubonNeoAssetKinds.FuturesOptions;
		return _accounts.Values.FirstOrDefault(item => item.Info.IsFutures == isFutures)
			?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
	}

	private NativeAccount[] FilterAccounts(string portfolioName)
	{
		if (portfolioName.IsEmpty())
			return [.. _accounts.Values.DistinctBy(item => item.Info.PortfolioName)];
		return _accounts.TryGetValue(portfolioName, out var account)
			? [account]
			: throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
	}

	private async Task EnsureSocketConnectedAsync(FubonNeoAssetKinds kind, CancellationToken cancellationToken)
	{
		if (IsSocketConnected(kind))
			return;
		var sync = kind == FubonNeoAssetKinds.Stock ? _stockSocketSync : _futuresSocketSync;
		await sync.WaitAsync(cancellationToken);
		try
		{
			if (IsSocketConnected(kind))
				return;
			await EnsureBridge().ConnectSocketAsync(kind);
			SetSocketConnected(kind, true);
		}
		finally
		{
			sync.Release();
		}
	}

	private Task SendSubscriptionAsync(FubonNeoSubscription subscription)
	{
		lock (GetPendingSync(subscription.Kind))
			GetPendingQueue(subscription.Kind).Enqueue(subscription.Key);
		return EnsureBridge().SubscribeAsync(subscription);
	}

	private void OnMarketMessage(FubonNeoAssetKinds kind, string json)
		=> _ = ProcessMarketMessageSafeAsync(kind, json);

	private async Task ProcessMarketMessageSafeAsync(FubonNeoAssetKinds kind, string json)
	{
		try
		{
			await ProcessMarketMessageAsync(kind, json, _lifetime?.Token ?? CancellationToken.None);
		}
		catch (OperationCanceledException) when (_lifetime?.IsCancellationRequested == true)
		{
		}
		catch (Exception error)
		{
			await RaiseErrorAsync(error, CancellationToken.None);
		}
	}

	private async Task ProcessMarketMessageAsync(FubonNeoAssetKinds kind, string json,
		CancellationToken cancellationToken)
	{
		if (json.IsEmpty())
			return;
		var envelope = Deserialize<FubonNeoSocketEnvelope>(json);
		switch (envelope.Event?.ToLowerInvariant())
		{
			case "subscribed":
				await ProcessSubscribedAsync(kind, json);
				break;
			case "data":
			{
				var message = Deserialize<FubonNeoStreamMessage>(json);
				if (message.Data != null && _serverSubscriptions.TryGetValue(GetServerKey(kind, message.Id), out var subscription) &&
					MarketDataReceived is { } handler)
					await handler(subscription, message.Data, cancellationToken);
				break;
			}
			case "error":
			{
				var status = Deserialize<FubonNeoSocketStatusMessage>(json);
				await RaiseErrorAsync(new InvalidOperationException(
					$"Fubon WebSocket error: {status.Data?.Message.IsEmpty("unknown error")}"), cancellationToken);
				break;
			}
			case "authenticated":
			case "pong":
			case "heartbeat":
			case "unsubscribed":
			case "subscriptions":
				break;
			default:
				this.AddVerboseLog("Ignored Fubon WebSocket event {0}.", envelope.Event);
				break;
		}
	}

	private async Task ProcessSubscribedAsync(FubonNeoAssetKinds kind, string json)
	{
		var acknowledgement = Deserialize<FubonNeoSubscriptionMessage>(json);
		var data = acknowledgement.Data;
		if (data?.Id.IsEmpty() != false)
			throw new InvalidDataException("Fubon subscription acknowledgement has no channel id.");
		string key = null;
		lock (GetPendingSync(kind))
		{
			var pending = GetPendingQueue(kind);
			if (pending.Count > 0)
				key = pending.Dequeue();
		}
		FubonNeoSubscription subscription = null;
		if (!key.IsEmpty())
			_subscriptions.TryGetValue(key, out subscription);
		if (subscription == null || !subscription.Channel.EqualsIgnoreCase(data.Channel) ||
			!subscription.Symbol.EqualsIgnoreCase(data.Symbol))
		{
			subscription = _subscriptions.Values.FirstOrDefault(item => item.Kind == kind && item.ServerId.IsEmpty() &&
				item.Channel.EqualsIgnoreCase(data.Channel) && item.Symbol.EqualsIgnoreCase(data.Symbol));
		}
		if (subscription == null)
		{
			await EnsureBridge().UnsubscribeAsync(kind, data.Id);
			return;
		}
		subscription.ServerId = data.Id;
		_serverSubscriptions[GetServerKey(kind, data.Id)] = subscription;
	}

	private void OnSocketDisconnected(FubonNeoAssetKinds kind, string message)
	{
		if (Interlocked.CompareExchange(ref _isDisconnecting, 0, 0) != 0)
			return;
		SetSocketConnected(kind, false);
		ClearServerSubscriptions(kind);
		ref var field = ref (kind == FubonNeoAssetKinds.Stock
			? ref _isStockReconnecting
			: ref _isFuturesReconnecting);
		if (Interlocked.Exchange(ref field, 1) != 0)
			return;
		_ = ReconnectSocketAsync(kind, message);
	}

	private async Task ReconnectSocketAsync(FubonNeoAssetKinds kind, string message)
	{
		try
		{
			var cancellationToken = _lifetime?.Token ?? CancellationToken.None;
			Exception lastError = null;
			for (var attempt = 1; attempt <= _reconnectAttempts && !cancellationToken.IsCancellationRequested; attempt++)
			{
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(Math.Min(attempt, 10)), cancellationToken);
					await EnsureSocketConnectedAsync(kind, cancellationToken);
					foreach (var subscription in _subscriptions.Values.Where(item => item.Kind == kind))
						await SendSubscriptionAsync(subscription);
					this.AddInfoLog("Fubon {0} WebSocket reconnected on attempt {1}.", kind, attempt);
					return;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception error)
				{
					lastError = error;
					this.AddWarningLog("Fubon {0} WebSocket reconnect attempt {1} failed: {2}",
						kind, attempt, error.Message);
				}
			}
			await RaiseConnectionLostAsync(lastError ?? new InvalidOperationException(
				$"Fubon {kind} WebSocket disconnected: {message.IsEmpty("unknown reason")}."));
		}
		finally
		{
			if (kind == FubonNeoAssetKinds.Stock)
				Interlocked.Exchange(ref _isStockReconnecting, 0);
			else
				Interlocked.Exchange(ref _isFuturesReconnecting, 0);
		}
	}

	private void ClearServerSubscriptions(FubonNeoAssetKinds kind)
	{
		foreach (var item in _serverSubscriptions.ToArray())
		{
			if (item.Value.Kind == kind)
				_serverSubscriptions.Remove(item.Key);
		}
		foreach (var subscription in _subscriptions.Values.Where(item => item.Kind == kind))
			subscription.ServerId = null;
		lock (GetPendingSync(kind))
			GetPendingQueue(kind).Clear();
	}

	private void OnOrder(string code, object data, bool isFutures)
		=> _ = ProcessOrderSafeAsync(code, data, isFutures);

	private async Task ProcessOrderSafeAsync(string code, object data, bool isFutures)
	{
		if (Interlocked.CompareExchange(ref _isDisconnecting, 0, 0) != 0)
			return;

		try
		{
			if (data == null)
			{
				if (!code.IsEmpty())
					await RaiseErrorAsync(new InvalidOperationException($"Fubon order callback: {code}"), CancellationToken.None);
				return;
			}
			var account = FindAccount(data);
			var native = new NativeOrder { Value = data, Account = account, IsFutures = isFutures };
			CacheOrder(native);
			if (OrderReceived is { } handler)
				await handler(ReadOrder(data, isFutures), _lifetime?.Token ?? CancellationToken.None);
		}
		catch (Exception error)
		{
			await RaiseErrorAsync(error, CancellationToken.None);
		}
	}

	private void OnFill(string code, object data, bool isFutures)
		=> _ = ProcessFillSafeAsync(code, data, isFutures);

	private async Task ProcessFillSafeAsync(string code, object data, bool isFutures)
	{
		if (Interlocked.CompareExchange(ref _isDisconnecting, 0, 0) != 0)
			return;

		try
		{
			if (data == null)
			{
				if (!code.IsEmpty())
					await RaiseErrorAsync(new InvalidOperationException($"Fubon fill callback: {code}"), CancellationToken.None);
				return;
			}
			if (FillReceived is { } handler)
				await handler(ReadFill(data, isFutures), _lifetime?.Token ?? CancellationToken.None);
		}
		catch (Exception error)
		{
			await RaiseErrorAsync(error, CancellationToken.None);
		}
	}

	private NativeAccount FindAccount(object data)
	{
		var portfolio = $"{Text(data, "branchNo")}-{Text(data, "account")}";
		return _accounts.TryGetValue(portfolio, out var account)
			? account
			: throw new InvalidDataException($"Fubon callback references unknown account '{portfolio}'.");
	}

	private void AttachBridge(FubonNeoSdkBridge bridge)
	{
		bridge.Order += OnOrder;
		bridge.Filled += OnFill;
		bridge.Event += OnEvent;
		bridge.MarketMessage += OnMarketMessage;
		bridge.SocketException += OnSocketException;
		bridge.SocketDisconnected += OnSocketDisconnected;
	}

	private void DetachBridge(FubonNeoSdkBridge bridge)
	{
		bridge.Order -= OnOrder;
		bridge.Filled -= OnFill;
		bridge.Event -= OnEvent;
		bridge.MarketMessage -= OnMarketMessage;
		bridge.SocketException -= OnSocketException;
		bridge.SocketDisconnected -= OnSocketDisconnected;
	}

	private void OnEvent(string code, string content)
		=> this.AddInfoLog("Fubon SDK event {0}: {1}", code, content);

	private void OnSocketException(FubonNeoAssetKinds kind, Exception error)
	{
		if (Interlocked.CompareExchange(ref _isDisconnecting, 0, 0) == 0)
			_ = RaiseErrorAsync(new InvalidOperationException($"Fubon {kind} WebSocket error.", error), CancellationToken.None);
	}

	private async Task DisconnectCoreAsync()
	{
		var bridge = _bridge;
		if (bridge == null)
			return;
		_lifetime?.Cancel();
		try
		{
			if (_isStockSocketConnected)
				await bridge.DisconnectSocketAsync(FubonNeoAssetKinds.Stock);
			if (_isFuturesSocketConnected)
				await bridge.DisconnectSocketAsync(FubonNeoAssetKinds.FuturesOptions);
		}
		finally
		{
			DetachBridge(bridge);
			bridge.Dispose();
			_bridge = null;
			_lifetime?.Dispose();
			_lifetime = null;
			_isStockSocketConnected = false;
			_isFuturesSocketConnected = false;
			_accounts.Clear();
			_orders.Clear();
			_subscriptions.Clear();
			_serverSubscriptions.Clear();
			_securities = null;
		}
	}

	private async Task<T> RunSdkAsync<T>(Func<T> action, CancellationToken cancellationToken)
	{
		await _sdkSync.WaitAsync(cancellationToken);
		try
		{
			return await Task.Run(action, cancellationToken);
		}
		finally
		{
			_sdkSync.Release();
		}
	}

	private FubonNeoSdkBridge EnsureBridge()
		=> _bridge ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private bool IsSocketConnected(FubonNeoAssetKinds kind)
		=> kind == FubonNeoAssetKinds.Stock ? _isStockSocketConnected : _isFuturesSocketConnected;

	private void SetSocketConnected(FubonNeoAssetKinds kind, bool value)
	{
		if (kind == FubonNeoAssetKinds.Stock)
			_isStockSocketConnected = value;
		else
			_isFuturesSocketConnected = value;
	}

	private Queue<string> GetPendingQueue(FubonNeoAssetKinds kind)
		=> kind == FubonNeoAssetKinds.Stock ? _stockPending : _futuresPending;

	private object GetPendingSync(FubonNeoAssetKinds kind)
		=> kind == FubonNeoAssetKinds.Stock ? _stockPendingSync : _futuresPendingSync;

	private static string GetServerKey(FubonNeoAssetKinds kind, string serverId)
		=> $"{kind}|{serverId}";

	private string Text(object target, string name)
		=> _bridge?.Property(target, name)?.ToString();

	private decimal? Number(object target, string name)
	{
		var value = _bridge?.Property(target, name);
		return value == null ? null : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
	}

	private int? Integer(object target, string name)
	{
		var value = _bridge?.Property(target, name);
		return value == null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
	}

	private bool Boolean(object target, string name)
	{
		var value = _bridge?.Property(target, name);
		return value != null && Convert.ToBoolean(value, CultureInfo.InvariantCulture);
	}

	private static decimal? ParseNumber(string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;

	private static T Deserialize<T>(string json)
		where T : class
		=> JsonConvert.DeserializeObject<T>(json, _jsonSettings)
			?? throw new InvalidDataException($"Fubon returned an invalid {typeof(T).Name} payload.");

	private static string ToSdkStockMarketType(FubonNeoStockMarketTypes value)
		=> value switch
		{
			FubonNeoStockMarketTypes.Emerging => "Emg",
			FubonNeoStockMarketTypes.EmergingOdd => "EmgOdd",
			_ => value.ToString(),
		};

	private static string ToSdkPriceType(FubonNeoPriceTypes value, bool isFutures)
		=> value switch
		{
			FubonNeoPriceTypes.Auto => "Limit",
			FubonNeoPriceTypes.RangeMarket when isFutures => "RangeMarket",
			FubonNeoPriceTypes.LimitUp when !isFutures => "LimitUp",
			FubonNeoPriceTypes.LimitDown when !isFutures => "LimitDown",
			FubonNeoPriceTypes.Reference => "Reference",
			FubonNeoPriceTypes.Market => "Market",
			_ => "Limit",
		};

	private static string ToSdkTimeInForce(TimeInForce value)
		=> value switch
		{
			TimeInForce.CancelBalance => "Ioc",
			TimeInForce.MatchOrCancel => "Fok",
			_ => "Rod",
		};

	private static string ToSdkFuturesOrderType(FubonNeoFuturesOrderTypes value)
		=> value == FubonNeoFuturesOrderTypes.DayTrade ? "FdayTrade" : value.ToString();

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error?.Invoke(error, cancellationToken) ?? default;

	private ValueTask RaiseConnectionLostAsync(Exception error)
		=> ConnectionLost?.Invoke(error, CancellationToken.None) ?? default;
}
