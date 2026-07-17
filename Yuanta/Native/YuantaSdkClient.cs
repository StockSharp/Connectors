namespace StockSharp.Yuanta.Native;

sealed class YuantaSdkClient : BaseLogReceiver
{
	private sealed class BookState
	{
		public object Sync { get; } = new();
		public decimal[] BidPrices { get; } = new decimal[5];
		public decimal[] BidVolumes { get; } = new decimal[5];
		public decimal[] AskPrices { get; } = new decimal[5];
		public decimal[] AskVolumes { get; } = new decimal[5];
	}

	private readonly string _sdkPath;
	private readonly string _requestedAccount;
	private readonly string _password;
	private readonly string _certificatePath;
	private readonly string _certificatePassword;
	private readonly YuantaEnvironments _environment;
	private readonly string _logPath;
	private readonly int _reconnectAttempts;
	private readonly SemaphoreSlim _sdkSync = new(1, 1);
	private readonly SemaphoreSlim _subscriptionSync = new(1, 1);
	private readonly ConcurrentDictionary<long, YuantaSubscription> _subscriptions = [];
	private readonly ConcurrentDictionary<string, BookState> _books = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, YuantaOrderRequest> _orders = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, decimal> _filledVolumes = new(StringComparer.OrdinalIgnoreCase);
	private YuantaSdkBridge _bridge;
	private YuantaAccountInfo _account;
	private CancellationTokenSource _lifetime;
	private TaskCompletionSource<object> _openResponse;
	private TaskCompletionSource<object> _loginResponse;
	private int _nativeId;
	private int _isDisconnecting;
	private int _isReconnecting;

	public YuantaSdkClient(string sdkPath, string account, SecureString password,
		string certificatePath, SecureString certificatePassword, YuantaEnvironments environment,
		string logPath, int reconnectAttempts)
	{
		_sdkPath = sdkPath;
		_requestedAccount = account.ThrowIfEmpty(nameof(account));
		_password = password?.UnSecure();
		_certificatePath = certificatePath;
		_certificatePassword = certificatePassword?.UnSecure();
		_environment = environment;
		_logPath = logPath;
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(Yuanta) + "_" + nameof(YuantaSdkClient);

	public event Func<YuantaSubscription, YuantaLevel1Update, CancellationToken, ValueTask> Level1Received;
	public event Func<YuantaSubscription, YuantaTradeUpdate, CancellationToken, ValueTask> TradeReceived;
	public event Func<YuantaSubscription, YuantaBookUpdate, CancellationToken, ValueTask> BookReceived;
	public event Func<YuantaOrderUpdate, CancellationToken, ValueTask> OrderReceived;
	public event Func<YuantaOrderTrade, CancellationToken, ValueTask> FillReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<Exception, CancellationToken, ValueTask> ConnectionLost;

	public string Version => _bridge?.Version;
	public YuantaAccountInfo Account => _account;

	public async Task ConnectAsync(CancellationToken cancellationToken)
	{
		if (_bridge != null)
			throw new InvalidOperationException("The Yuanta SDK is already connected.");
		if (_password.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.PasswordNotSpecified);
		if (_reconnectAttempts < 0)
			throw new ArgumentOutOfRangeException(nameof(_reconnectAttempts), _reconnectAttempts,
				"Reconnect attempts cannot be negative.");
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _certificatePath.IsEmpty())
			throw new InvalidOperationException(
				"Yuanta requires a PFX certificate path when the official SDK runs on Linux or macOS.");

		Interlocked.Exchange(ref _isDisconnecting, 0);
		_lifetime = new();
		try
		{
			await ConnectBridgeAsync(cancellationToken);
		}
		catch
		{
			DisconnectBridge();
			_lifetime.Dispose();
			_lifetime = null;
			throw;
		}
	}

	public Task DisconnectAsync()
	{
		if (Interlocked.Exchange(ref _isDisconnecting, 1) != 0)
			return Task.CompletedTask;
		_lifetime?.Cancel();
		DisconnectBridge();
		_lifetime?.Dispose();
		_lifetime = null;
		_account = null;
		_subscriptions.Clear();
		_books.Clear();
		_orders.Clear();
		_filledVolumes.Clear();
		return Task.CompletedTask;
	}

	public Task<YuantaSecurityInfo[]> GetSecuritiesAsync(IEnumerable<int> markets, string symbol,
		SecurityTypes? securityType, CancellationToken cancellationToken)
	{
		symbol.ThrowIfEmpty(nameof(symbol));
		var requested = markets.Distinct().Select(market => new YuantaSecurityInfo
		{
			Market = market,
			Symbol = symbol,
			SecurityType = securityType ?? market.ToSecurityType(symbol),
		}).ToArray();
		return RunSdkAsync(() =>
		{
			var result = EnsureBridge().GetQuotes(EnsureAccount().Account, requested);
			return EnsureBridge().Items(result, "QueryWatchList")
				.Where(item => !Text(item, "StkCode").IsEmpty())
				.Select(ReadSecurity)
				.ToArray();
		}, cancellationToken);
	}

	public Task<YuantaCandle[]> GetCandlesAsync(YuantaSecurityInfo security, TimeSpan timeFrame,
		DateTime from, DateTime to, CancellationToken cancellationToken)
		=> RunSdkAsync(() =>
		{
			var result = EnsureBridge().GetCandles(EnsureAccount().Account, security.Market,
				security.Symbol, timeFrame.ToYuantaKLineType(), from.ToTaipeiTime(), to.ToTaipeiTime());
			return EnsureBridge().Items(result, "KLineList").Select(item => new YuantaCandle
			{
				OpenTime = ReadSdkDateTime(EnsureBridge().Member(item, "TimeStamp")),
				Open = Number(item, "OpenPrice"),
				High = Number(item, "HighPrice"),
				Low = Number(item, "LowPrice"),
				Close = Number(item, "ClosePrice"),
				Volume = Number(item, "DealVol"),
			}).ToArray();
		}, cancellationToken);

	public Task<YuantaTradeUpdate[]> GetTicksAsync(YuantaSecurityInfo security, DateTime from,
		DateTime to, int count, CancellationToken cancellationToken)
		=> RunSdkAsync(() =>
		{
			var result = EnsureBridge().GetTicks(EnsureAccount().Account, security.Market,
				security.Symbol, from.ToTaipeiTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
				to.ToTaipeiTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture), count);
			return EnsureBridge().Items(result, "StickDetailList").Select(item => new YuantaTradeUpdate
			{
				Market = security.Market,
				Symbol = security.Symbol,
				Sequence = Long(item, "SeqNo"),
				ServerTime = ReadSdkDateTime(EnsureBridge().Member(item, "TimeStamp")),
				BuyPrice = Number(item, "BuyPrice"),
				SellPrice = Number(item, "SellPrice"),
				Price = Number(item, "DealPrice"),
				Volume = Number(item, "DealVol"),
				InOutFlag = Integer(item, "InOutFlag"),
			}).ToArray();
		}, cancellationToken);

	public async Task SubscribeAsync(YuantaSubscription subscription, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(subscription);
		await _subscriptionSync.WaitAsync(cancellationToken);
		try
		{
			if (!_subscriptions.TryAdd(subscription.TransactionId, subscription))
				return;
			if (_subscriptions.Values.Count(item => item.NativeKey.EqualsIgnoreCase(subscription.NativeKey)) > 1)
				return;
			try
			{
				await RunSdkAsync(() =>
				{
					EnsureBridge().Subscribe(EnsureAccount().Account, subscription);
					return true;
				}, cancellationToken);
			}
			catch
			{
				_subscriptions.TryRemove(subscription.TransactionId, out _);
				throw;
			}
		}
		finally
		{
			_subscriptionSync.Release();
		}
	}

	public async Task UnsubscribeAsync(long transactionId, CancellationToken cancellationToken)
	{
		await _subscriptionSync.WaitAsync(cancellationToken);
		try
		{
			if (!_subscriptions.TryRemove(transactionId, out var subscription))
				return;
			if (_subscriptions.Values.Any(item => item.NativeKey.EqualsIgnoreCase(subscription.NativeKey)))
				return;
			await RunSdkAsync(() =>
			{
				EnsureBridge().Unsubscribe(EnsureAccount().Account, subscription);
				return true;
			}, cancellationToken);
		}
		finally
		{
			_subscriptionSync.Release();
		}
	}

	public async Task SendHeartbeatAsync(CancellationToken cancellationToken)
	{
		try
		{
			await RunSdkAsync(() => EnsureBridge().GetSubscriptions(EnsureAccount().Account), cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception error)
		{
			this.AddWarningLog("Yuanta heartbeat failed: {0}", error.Message);
			await ReconnectAsync(error, cancellationToken);
		}
	}

	public Task<YuantaOrderUpdate> PlaceOrderAsync(YuantaOrderRequest request,
		CancellationToken cancellationToken)
	{
		var prepared = PrepareRequest(request, request.TransactionId);
		return RunSdkAsync(() =>
		{
			var result = EnsureBridge().SendOrder(prepared, 0);
			var update = ReadOrderResponse(result, prepared);
			_orders[update.OrderId] = CloneRequest(prepared, orderId: update.OrderId,
				tradeDate: update.ServerTime);
			return update;
		}, cancellationToken);
	}

	public Task<YuantaOrderUpdate> ReplaceOrderAsync(string orderId, long transactionId,
		decimal? price, long? volume, CancellationToken cancellationToken)
		=> RunSdkAsync(() =>
		{
			var original = ResolveOrder(orderId);
			if (price == null && volume == null)
				throw new InvalidOperationException("Yuanta replacement must change the price or quantity.");
			var current = PrepareRequest(CloneRequest(original,
				price: price ?? original.Price, volume: volume ?? original.Volume), transactionId);
			YuantaOrderUpdate update = null;
			if (price != null)
				update = ReadOrderResponse(EnsureBridge().SendOrder(current, 7), current);
			if (volume != null)
				update = ReadOrderResponse(EnsureBridge().SendOrder(current,
					current.SecurityType is SecurityTypes.Future or SecurityTypes.Option ? 5 : 3), current);
			_orders[orderId] = CloneRequest(current, orderId: orderId,
				tradeDate: update?.ServerTime ?? current.TradeDate);
			return update;
		}, cancellationToken);

	public Task<YuantaOrderUpdate> CancelOrderAsync(string orderId, long transactionId,
		CancellationToken cancellationToken)
		=> RunSdkAsync(() =>
		{
			var request = PrepareRequest(ResolveOrder(orderId), transactionId);
			return ReadOrderResponse(EnsureBridge().SendOrder(request, 4), request);
		}, cancellationToken);

	public Task<YuantaOrderSnapshot> GetOrderSnapshotAsync(CancellationToken cancellationToken)
		=> RunSdkAsync(() => ReadOrderSnapshot(
			EnsureBridge().GetOrders(EnsureAccount().Account)), cancellationToken);

	public Task<YuantaPortfolioSnapshot> GetPortfolioAsync(CancellationToken cancellationToken)
		=> RunSdkAsync(ReadPortfolio, cancellationToken);

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
		_subscriptionSync.Dispose();
		base.DisposeManaged();
	}

	private async Task ConnectBridgeAsync(CancellationToken cancellationToken)
	{
		var bridge = new YuantaSdkBridge(_sdkPath, _logPath);
		bridge.Response += OnResponse;
		_openResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_loginResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_bridge = bridge;
		try
		{
			await RunSdkAsync(() =>
			{
				bridge.Open(_environment);
				return true;
			}, cancellationToken);
			await _openResponse.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
			_openResponse = null;
			var accepted = await RunSdkAsync(() => bridge.Login(_requestedAccount, _password,
				_certificatePath, _certificatePassword), cancellationToken);
			if (!accepted)
				throw new InvalidOperationException("The Yuanta SDK rejected the login request.");
			var result = await _loginResponse.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
			var status = bridge.Member(result, "LoginStatus");
			var code = Text(status, "MsgCode");
			var message = Text(status, "MsgContent");
			var account = bridge.Items(result, "LoginList").Select(ReadAccount).FirstOrDefault(item =>
				item.Account.EqualsIgnoreCase(_requestedAccount));
			if (account == null)
				throw new InvalidOperationException(message.IsEmpty(
					$"Yuanta login {code.IsEmpty("failed")} returned no requested account."));
			_account = account;
			this.AddInfoLog("Yuanta SPARK {0} connected for {1}.", bridge.Version, account.Account);
		}
		catch
		{
			DisconnectBridge();
			throw;
		}
		finally
		{
			_openResponse = null;
			_loginResponse = null;
		}
	}

	private void DisconnectBridge()
	{
		var bridge = _bridge;
		if (bridge == null)
			return;
		_bridge = null;
		bridge.Response -= OnResponse;
		try
		{
			bridge.Logout();
		}
		catch (Exception error)
		{
			this.AddVerboseLog("Yuanta logout failed: {0}", error.Message);
		}
		try
		{
			bridge.Close();
		}
		catch (Exception error)
		{
			this.AddVerboseLog("Yuanta close failed: {0}", error.Message);
		}
		bridge.Dispose();
	}

	private async Task ReconnectAsync(Exception cause, CancellationToken cancellationToken)
	{
		if (Interlocked.CompareExchange(ref _isDisconnecting, 0, 0) != 0 ||
			Interlocked.Exchange(ref _isReconnecting, 1) != 0)
			return;
		try
		{
			Exception lastError = cause;
			for (var attempt = 1; attempt <= _reconnectAttempts; attempt++)
			{
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(Math.Min(attempt, 10)), cancellationToken);
					DisconnectBridge();
					await ConnectBridgeAsync(cancellationToken);
					foreach (var subscription in _subscriptions.Values
						.GroupBy(item => item.NativeKey, StringComparer.OrdinalIgnoreCase)
						.Select(group => group.First()))
					{
						await RunSdkAsync(() =>
						{
							EnsureBridge().Subscribe(EnsureAccount().Account, subscription);
							return true;
						}, cancellationToken);
					}
					this.AddInfoLog("Yuanta connection restored on attempt {0}.", attempt);
					return;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception error)
				{
					lastError = error;
					this.AddWarningLog("Yuanta reconnect attempt {0} failed: {1}", attempt, error.Message);
				}
			}
			await RaiseConnectionLostAsync(new InvalidOperationException(
				"Yuanta connection could not be restored.", lastError));
		}
		finally
		{
			Interlocked.Exchange(ref _isReconnecting, 0);
		}
	}

	private void OnResponse(int mark, uint index, string name, object handle, object value)
		=> _ = ProcessResponseSafeAsync(mark, index, name, handle, value);

	private async Task ProcessResponseSafeAsync(int mark, uint index, string name, object handle, object value)
	{
		try
		{
			await ProcessResponseAsync(mark, index, name, handle, value,
				_lifetime?.Token ?? CancellationToken.None);
		}
		catch (OperationCanceledException) when (_lifetime?.IsCancellationRequested == true)
		{
		}
		catch (Exception error)
		{
			await RaiseErrorAsync(error, CancellationToken.None);
		}
	}

	private async ValueTask ProcessResponseAsync(int mark, uint index, string name, object handle,
		object value, CancellationToken cancellationToken)
	{
		if (mark == 0)
		{
			this.AddInfoLog("Yuanta SDK status {0}: {1}", index, value);
			if (index == 1)
				_openResponse?.TrySetResult(value);
			else if (index == 0)
			{
				var error = new InvalidOperationException(
					value?.ToString().IsEmpty("Yuanta transport connection failed."));
				if (_openResponse != null)
					_openResponse.TrySetException(error);
				else if (Interlocked.CompareExchange(ref _isDisconnecting, 0, 0) == 0)
					await ReconnectAsync(error, cancellationToken);
			}
			return;
		}
		if (mark == 1)
		{
			if (name.EqualsIgnoreCase("Login"))
				_loginResponse?.TrySetResult(value);
			else if (name.IsEmpty() && _loginResponse != null)
				_loginResponse.TrySetException(new InvalidOperationException(
					value?.ToString().IsEmpty("Yuanta login failed.")));
			return;
		}
		if (mark != 2 || value == null)
			return;

		switch (name)
		{
			case "SubscribeWatchlistAll":
				await PublishLevel1Async(ReadLevel1(value), cancellationToken);
				break;
			case "SubscribeStockTick":
				await PublishTradeAsync(ReadTrade(value), cancellationToken);
				break;
			case "SubscribeFiveTickA":
				await PublishBookAsync(ReadBook(value), cancellationToken);
				break;
			case "RR_RealReport":
			case "RR_RealReportMerge":
				await PublishReportAsync(value, name == "RR_RealReportMerge", cancellationToken);
				break;
			default:
				this.AddVerboseLog("Ignored Yuanta callback {0} ({1}).", name, index);
				break;
		}
	}

	private async ValueTask PublishLevel1Async(YuantaLevel1Update update,
		CancellationToken cancellationToken)
	{
		if (update == null || Level1Received is not { } handler)
			return;
		foreach (var subscription in MatchSubscriptions(YuantaMarketDataKinds.Level1,
			update.Market, update.Symbol))
			await handler(subscription, update, cancellationToken);
	}

	private async ValueTask PublishTradeAsync(YuantaTradeUpdate update,
		CancellationToken cancellationToken)
	{
		if (update == null || TradeReceived is not { } handler)
			return;
		foreach (var subscription in MatchSubscriptions(YuantaMarketDataKinds.Trades,
			update.Market, update.Symbol))
			await handler(subscription, update, cancellationToken);
	}

	private async ValueTask PublishBookAsync(YuantaBookUpdate update,
		CancellationToken cancellationToken)
	{
		if (update == null || BookReceived is not { } handler)
			return;
		foreach (var subscription in MatchSubscriptions(YuantaMarketDataKinds.MarketDepth,
			update.Market, update.Symbol))
			await handler(subscription, update, cancellationToken);
	}

	private async ValueTask PublishReportAsync(object value, bool isMerged,
		CancellationToken cancellationToken)
	{
		var update = ReadRealtimeOrder(value, isMerged);
		if (OrderReceived is { } orderHandler)
			await orderHandler(update, cancellationToken);
		if (!isMerged)
			return;
		var total = Number(value, "OkQty");
		var price = Number(value, "LastDealPrice");
		if (total <= 0 || price <= 0)
			return;
		_filledVolumes.TryGetValue(update.OrderId, out var previous);
		if (total <= previous)
			return;
		_filledVolumes[update.OrderId] = total;
		var volume = total - previous;
		if (volume <= 0)
			return;
		if (FillReceived is { } fillHandler)
		{
			await fillHandler(new()
			{
				Account = update.Account,
				Market = update.Market,
				Symbol = update.Symbol,
				OrderId = update.OrderId,
				TradeId = Text(value, "TradeCode").IsEmpty(
					$"{update.OrderId}:{update.ServerTime.Ticks}:{total}"),
				SecurityType = update.SecurityType,
				Side = update.Side,
				Price = price,
				Volume = volume,
				ServerTime = update.ServerTime,
				IsFutures = update.IsFutures,
			}, cancellationToken);
		}
	}

	private YuantaSubscription[] MatchSubscriptions(YuantaMarketDataKinds kind, int market, string symbol)
		=> [.. _subscriptions.Values.Where(item => item.Kind == kind && item.Market == market &&
			item.Symbol.EqualsIgnoreCase(symbol))];

	private YuantaLevel1Update ReadLevel1(object value)
	{
		var composite22 = EnsureBridge().Member(value, "IndexFlag_22");
		var composite28 = EnsureBridge().Member(value, "IndexFlag_28");
		var composite29 = EnsureBridge().Member(value, "IndexFlag_29");
		return new()
		{
			Market = Integer(value, "MarketType"),
			Symbol = Text(value, "StkCode"),
			Field = Integer(value, "IndexFlag"),
			Value = Number(value, "Value"),
			ServerTime = composite29 == null
				? DateTime.UtcNow
				: ReadDateTime(null, EnsureBridge().Member(composite29, "Time")),
			BuyPrice = NullableNumber(composite28, "BuyPrice"),
			SellPrice = NullableNumber(composite28, "SellPrice"),
			LastPrice = NullableNumber(composite29, "Deal"),
			LastVolume = NullableNumber(composite29, "Vol"),
			TotalVolume = NullableNumber(composite29, "TotalVol"),
			TotalBuyVolume = NullableNumber(composite22, "BuyVol") ?? NullableNumber(composite29, "TotalInVol"),
			TotalSellVolume = NullableNumber(composite22, "SellVol") ?? NullableNumber(composite29, "TotalOutVol"),
		};
	}

	private YuantaTradeUpdate ReadTrade(object value)
		=> new()
		{
			Market = Integer(value, "MarketType"),
			Symbol = Text(value, "StkCode"),
			Sequence = Long(value, "SerialNo"),
			ServerTime = ReadDateTime(null, EnsureBridge().Member(value, "Time")),
			BuyPrice = Number(value, "BuyPrice"),
			SellPrice = Number(value, "SellPrice"),
			Price = Number(value, "DealPrice"),
			Volume = Number(value, "DealVol"),
			InOutFlag = Integer(value, "InOutFlag"),
		};

	private YuantaBookUpdate ReadBook(object value)
	{
		var market = Integer(value, "MarketType");
		var symbol = Text(value, "StkCode");
		if (symbol.IsEmpty())
			return null;
		var state = _books.GetOrAdd($"{market}|{symbol}", _ => new());
		lock (state.Sync)
		{
			var flag = Integer(value, "IndexFlag");
			var number = Number(value, "Value");
			if (flag is >= 0 and <= 4)
				state.BidVolumes[flag] = number;
			else if (flag is >= 5 and <= 9)
				state.BidPrices[flag - 5] = number;
			else if (flag is >= 10 and <= 14)
				state.AskVolumes[flag - 10] = number;
			else if (flag is >= 15 and <= 19)
				state.AskPrices[flag - 15] = number;
			else if (flag is 20 or 21)
				ReadOneSideBook(EnsureBridge().Member(value, $"IndexFlag_{flag}"),
					flag == 20 ? state.BidPrices : state.AskPrices,
					flag == 20 ? state.BidVolumes : state.AskVolumes);
			else if (flag == 50)
				ReadCombinedBook(EnsureBridge().Member(value, "IndexFlag_50"), state);

			return new()
			{
				Market = market,
				Symbol = symbol,
				ServerTime = DateTime.UtcNow,
				Bids = ReadLevels(state.BidPrices, state.BidVolumes, true),
				Asks = ReadLevels(state.AskPrices, state.AskVolumes, false),
			};
		}
	}

	private void ReadOneSideBook(object source, decimal[] prices, decimal[] volumes)
	{
		if (source == null)
			return;
		for (var index = 0; index < 5; index++)
		{
			prices[index] = Number(source, $"Price{index + 1}");
			volumes[index] = Number(source, $"Vol{index + 1}");
		}
	}

	private void ReadCombinedBook(object source, BookState state)
	{
		if (source == null)
			return;
		for (var index = 0; index < 5; index++)
		{
			state.BidPrices[index] = Number(source, $"BuyPrice{index + 1}");
			state.BidVolumes[index] = Number(source, $"BuyVol{index + 1}");
			state.AskPrices[index] = Number(source, $"SellPrice{index + 1}");
			state.AskVolumes[index] = Number(source, $"SellVol{index + 1}");
		}
	}

	private static IReadOnlyList<YuantaBookLevel> ReadLevels(decimal[] prices, decimal[] volumes, bool isBid)
		=> [.. prices.Select((price, index) => new YuantaBookLevel
		{
			Price = price,
			Volume = volumes[index],
		}).Where(level => level.Price > 0).OrderBy(level => isBid ? -level.Price : level.Price)];

	private YuantaOrderSnapshot ReadOrderSnapshot(object result)
	{
		var orders = new List<YuantaOrderUpdate>();
		var trades = new List<YuantaOrderTrade>();
		foreach (var value in EnsureBridge().Items(result, "StkOrderList"))
		{
			var order = ReadStockOrder(value);
			orders.Add(order);
			if (!order.OrderId.IsEmpty() && !_orders.ContainsKey(order.OrderId))
				_orders[order.OrderId] = ReadStockOrderRequest(value, order);
		}
		foreach (var value in EnsureBridge().Items(result, "FutOrderList"))
		{
			var order = ReadFuturesOrder(value);
			orders.Add(order);
			if (!order.OrderId.IsEmpty() && !_orders.ContainsKey(order.OrderId))
				_orders[order.OrderId] = ReadFuturesOrderRequest(value, order);
		}
		trades.AddRange(EnsureBridge().Items(result, "StkTradeList").Select(ReadStockTrade));
		trades.AddRange(EnsureBridge().Items(result, "FutTradeList").Select(ReadFuturesTrade));
		return new() { Orders = orders, Trades = trades };
	}

	private YuantaOrderRequest ReadStockOrderRequest(object value, YuantaOrderUpdate update)
		=> new()
		{
			Account = update.Account,
			Market = update.Market,
			Symbol = update.Symbol,
			SecurityType = update.SecurityType,
			Side = update.Side,
			OrderType = update.OrderType,
			TimeInForce = update.TimeInForce,
			Price = update.Price,
			Volume = decimal.ToInt64(update.Volume),
			StockMarketType = Integer(value, "APCode") switch
			{
				2 => YuantaStockMarketTypes.OddLot,
				4 => YuantaStockMarketTypes.IntradayOddLot,
				7 => YuantaStockMarketTypes.AfterHours,
				_ => YuantaStockMarketTypes.Regular,
			},
			StockOrderType = Integer(value, "OrderType") switch
			{
				3 => YuantaStockOrderTypes.Margin,
				4 => YuantaStockOrderTypes.Short,
				5 => YuantaStockOrderTypes.StrategyBorrowed,
				6 => YuantaStockOrderTypes.HedgeBorrowed,
				9 => YuantaStockOrderTypes.DayTrade,
				_ => YuantaStockOrderTypes.Cash,
			},
			SellerNo = (short)Integer(value, "Seller"),
			UserTag = Text(value, "BasketNo"),
			OrderId = update.OrderId,
			TradeDate = update.ServerTime,
		};

	private YuantaOrderRequest ReadFuturesOrderRequest(object value, YuantaOrderUpdate update)
		=> new()
		{
			Account = update.Account,
			Market = update.Market,
			Symbol = update.Symbol,
			OrderSymbol = Text(value, "Commodity1"),
			SecurityType = update.SecurityType,
			Side = update.Side,
			OrderType = update.OrderType,
			TimeInForce = update.TimeInForce,
			Price = update.Price,
			Volume = decimal.ToInt64(update.Volume),
			PositionEffect = Text(value, "OpenOffsetKind") switch
			{
				"0" => YuantaFuturesPositionEffects.Open,
				"1" => YuantaFuturesPositionEffects.Close,
				_ => YuantaFuturesPositionEffects.Auto,
			},
			FuturesPriceType = update.OrderType == OrderTypes.Market
				? YuantaFuturesPriceTypes.Market
				: YuantaFuturesPriceTypes.Limit,
			SettlementMonth = Integer(value, "SettlementMonth1"),
			OptionType = ReadOptionType(Text(value, "ProductType"), Text(value, "StkName1")),
			StrikePrice = Number(value, "StrikePrice1"),
			IsDayTrade = Text(value, "DayTradeID").EqualsIgnoreCase("Y"),
			SellerNo = (short)Integer(value, "Seller"),
			UserTag = Text(value, "BasketNo"),
			OrderId = update.OrderId,
			TradeDate = update.ServerTime,
		};

	private YuantaOrderUpdate ReadStockOrder(object value)
	{
		var time = ReadDateTime(EnsureBridge().Member(value, "UpdateDate"),
			EnsureBridge().Member(value, "UpdateTime"));
		if (time == default)
			time = ReadDateTime(EnsureBridge().Member(value, "AcceptDate"),
				EnsureBridge().Member(value, "AcceptTime"));
		return CreateOrderUpdate(value, false, Text(value, "CompanyNo"),
			Number(value, "Price"), Text(value, "PriceFlag").EqualsIgnoreCase("M")
				? OrderTypes.Market : OrderTypes.Limit,
			ToTimeInForce(Text(value, "Time_in_Force")), time);
	}

	private YuantaOrderUpdate ReadFuturesOrder(object value)
		=> CreateOrderUpdate(value, true, Text(value, "StkCode1").IsEmpty(Text(value, "Commodity1")),
			ParseNumber(Text(value, "OrderPrice")) ?? 0,
			Text(value, "OrderPrice").EqualsIgnoreCase("M") ? OrderTypes.Market : OrderTypes.Limit,
			ToTimeInForce(Text(value, "OrderCondition")),
			ReadDateTime(EnsureBridge().Member(value, "AcceptDate"),
				EnsureBridge().Member(value, "AcceptTime")));

	private YuantaOrderUpdate CreateOrderUpdate(object value, bool isFutures, string symbol,
		decimal price, OrderTypes orderType, TimeInForce timeInForce, DateTime time)
	{
		var market = Integer(value, isFutures ? "MarketNo1" : "MarketNo");
		if (market == 0)
			market = isFutures ? 3 : 1;
		var volume = Number(value, "AfterQty");
		if (volume <= 0)
			volume = Number(value, "BeforeQty");
		var filled = Number(value, "OkQty");
		var status = Integer(value, "OrderStatus");
		return new()
		{
			Account = Text(value, "Account"),
			Market = market,
			Symbol = symbol,
			Name = Text(value, isFutures ? "StkName1" : "StkName"),
			OrderId = Text(value, "OrderNo"),
			SecurityType = market.ToSecurityType(symbol,
				Text(value, isFutures ? "StkName1" : "StkName")),
			Side = ToSide(Text(value, isFutures ? "BuySellKind1" : "BS")),
			OrderType = orderType,
			TimeInForce = timeInForce,
			Price = price,
			Volume = volume,
			Balance = Math.Max(0, volume - filled),
			FilledVolume = filled,
			State = ToOrderState(status, volume, filled),
			ServerTime = time == default ? DateTime.UtcNow : time,
			Error = Text(value, "ErrorMessage").IsEmpty(Text(value, "ErrorNo")),
			IsFutures = isFutures,
		};
	}

	private YuantaOrderTrade ReadStockTrade(object value)
	{
		var orderId = Text(value, "OrderNo");
		var time = ReadSdkDateTime(EnsureBridge().Member(value, "DateTime"));
		return new()
		{
			Account = Text(value, "Account"),
			Market = Integer(value, "MarketNo"),
			Symbol = Text(value, "CompanyNo"),
			OrderId = orderId,
			TradeId = $"{orderId}:{time.Ticks}:{Number(value, "SPrice")}:{Number(value, "OkQty")}",
			SecurityType = SecurityTypes.Stock,
			Side = ToSide(Text(value, "BS")),
			Price = Number(value, "SPrice"),
			Volume = Number(value, "OkQty"),
			ServerTime = time,
		};
	}

	private YuantaOrderTrade ReadFuturesTrade(object value)
	{
		var orderId = Text(value, "OrderNo");
		var market = Integer(value, "MarketNo");
		var symbol = Text(value, "StkCode1").IsEmpty(Text(value, "Commodity1"));
		var time = ReadDateTime(EnsureBridge().Member(value, "MatchDate"),
			EnsureBridge().Member(value, "MatchTime"));
		return new()
		{
			Account = Text(value, "Account"),
			Market = market,
			Symbol = symbol,
			OrderId = orderId,
			TradeId = Text(value, "SubNo").IsEmpty(
				$"{orderId}:{time.Ticks}:{Number(value, "MatchPrice1")}:{Number(value, "OkQty")}"),
			SecurityType = market.ToSecurityType(symbol, Text(value, "StkName1")),
			Side = ToSide(Text(value, "BuySellKind1")),
			Price = Number(value, "MatchPrice1"),
			Volume = Number(value, "OkQty"),
			ServerTime = time,
			IsFutures = true,
		};
	}

	private YuantaOrderUpdate ReadRealtimeOrder(object value, bool isMerged)
	{
		var orderId = Text(value, "OrderNo");
		_orders.TryGetValue(orderId, out var request);
		var market = Integer(value, "MarketNo");
		var symbol = Text(value, "CompanyNo").IsEmpty(request?.Symbol);
		var volume = Number(value, "OrderQty");
		var filled = isMerged ? Number(value, "OkQty") : 0;
		var status = Integer(value, "OrderStatus");
		var error = Text(value, "StkErrorNo").IsEmpty(Text(value, "OrderErrorNo"));
		return new()
		{
			TransactionId = request?.TransactionId ?? 0,
			Account = Text(value, "Account").IsEmpty(request?.Account),
			Market = market,
			Symbol = symbol,
			Name = Text(value, "StkCName"),
			OrderId = orderId,
			SecurityType = request?.SecurityType ?? market.ToSecurityType(symbol, Text(value, "StkCName")),
			Side = request?.Side ?? ToSide(Text(value, "BS")),
			OrderType = request?.OrderType ?? (Text(value, "PriceType").EqualsIgnoreCase("M")
				? OrderTypes.Market : OrderTypes.Limit),
			TimeInForce = request?.TimeInForce ?? ToTimeInForce(Text(value, "OrderCond")),
			Price = Number(value, "Price"),
			Volume = volume,
			Balance = Math.Max(0, volume - filled),
			FilledVolume = filled,
			State = error.IsEmpty() ? ToOrderState(status, volume, filled) : OrderStates.Failed,
			ServerTime = ReadDateTime(EnsureBridge().Member(value, "OrderDate"),
				EnsureBridge().Member(value, "OrderTime")),
			Error = error,
			IsFutures = request?.SecurityType is SecurityTypes.Future or SecurityTypes.Option || market >= 3,
		};
	}

	private YuantaPortfolioSnapshot ReadPortfolio()
	{
		var account = EnsureAccount();
		return account.IsFutures ? ReadFuturesPortfolio(account) : ReadStockPortfolio(account);
	}

	private YuantaPortfolioSnapshot ReadStockPortfolio(YuantaAccountInfo account)
	{
		var positionsResult = EnsureBridge().GetStockPositions(account.Account);
		var positions = EnsureBridge().Items(positionsResult, "StkStoreList").Select(item =>
		{
			var quantity = Number(item, "StockQty");
			var tradable = Number(item, "TradingQty");
			var market = Integer(item, "MarketNo");
			return new YuantaPositionInfo
			{
				Account = account.Account,
				Market = market,
				Symbol = Text(item, "StkCode"),
				Name = Text(item, "StkName"),
				SecurityType = SecurityTypes.Stock,
				CurrentValue = quantity,
				AveragePrice = Number(item, "Price"),
				CurrentPrice = NullableNumber(item, "MarketPrice"),
				BlockedValue = Math.Max(0, quantity - tradable),
			};
		}).ToArray();
		var balanceResult = EnsureBridge().GetBankBalance(account.Account);
		var balances = EnsureBridge().Items(balanceResult, "BankBalanceList");
		var available = balances.Sum(item => Number(item, "AvailableBalance"));
		return new()
		{
			Portfolio = new()
			{
				Account = account.Account,
				Currency = "TWD",
				CurrentValue = available,
				AvailableValue = available,
			},
			Positions = positions,
		};
	}

	private YuantaPortfolioSnapshot ReadFuturesPortfolio(YuantaAccountInfo account)
	{
		var positionsResult = EnsureBridge().GetFuturesPositions(account.Account);
		var positions = EnsureBridge().Items(positionsResult, "FutStoreList").Select(item =>
		{
			var market = Integer(item, "MarketNo1");
			var symbol = Text(item, "StkCode1").IsEmpty(Text(item, "Commodity1"));
			var quantity = Number(item, "Qty");
			if (Text(item, "BS").EqualsIgnoreCase("S"))
				quantity = -quantity;
			return new YuantaPositionInfo
			{
				Account = account.Account,
				Market = market,
				Symbol = symbol,
				Name = Text(item, "StkName1"),
				SecurityType = market.ToSecurityType(symbol, Text(item, "StkName1")),
				CurrentValue = quantity,
				AveragePrice = quantity >= 0 ? Number(item, "BuyPrice1") : Number(item, "SellPrice1"),
				CurrentPrice = NullableNumber(item, "MarketPrice1"),
			};
		}).ToArray();
		var equity = EnsureBridge().GetFuturesEquity(account.Account);
		return new()
		{
			Portfolio = new()
			{
				Account = account.Account,
				Currency = Text(equity, "Currency").IsEmpty("TWD"),
				CurrentValue = NullableNumber(equity, "Equity"),
				AvailableValue = NullableNumber(equity, "CanuseMargin"),
				BlockedValue = NullableNumber(equity, "AllIm"),
				UnrealizedPnL = NullableNumber(equity, "OpenGlYes") + NullableNumber(equity, "GlToday"),
			},
			Positions = positions,
		};
	}

	private YuantaOrderUpdate ReadOrderResponse(object result, YuantaOrderRequest request)
	{
		var status = EnsureBridge().Member(result, "ResultCount");
		var item = EnsureBridge().Items(result, "ResultList").FirstOrDefault();
		var statusMessage = Text(status, "MsgContent");
		if (item == null)
			throw new InvalidOperationException(statusMessage.IsEmpty("Yuanta returned no order result."));
		var replyCode = Integer(item, "ReplyCode");
		var advisory = Text(item, "Advisory");
		var error = Text(item, "ErrNO").IsEmpty(Text(item, "ErrType")).IsEmpty(advisory);
		if (replyCode != 0)
			throw new InvalidOperationException(error.IsEmpty($"Yuanta rejected the order ({replyCode})."));
		var orderId = Text(item, "OrderNO");
		if (orderId.IsEmpty())
			throw new InvalidDataException("Yuanta accepted the request but returned no order number.");
		var tradeDate = ReadSdkDateTime(EnsureBridge().Member(item, "TradeDate"));
		return new()
		{
			TransactionId = request.TransactionId,
			Account = request.Account,
			Market = request.Market,
			Symbol = request.Symbol,
			OrderId = orderId,
			SecurityType = request.SecurityType,
			Side = request.Side,
			OrderType = request.OrderType,
			TimeInForce = request.TimeInForce,
			Price = request.Price,
			Volume = request.Volume,
			Balance = request.Volume,
			State = OrderStates.Pending,
			ServerTime = tradeDate == default ? DateTime.UtcNow : tradeDate,
			IsFutures = request.SecurityType is SecurityTypes.Future or SecurityTypes.Option,
		};
	}

	private YuantaSecurityInfo ReadSecurity(object value)
	{
		var market = Integer(value, "MarketNo");
		var symbol = Text(value, "StkCode");
		var decimals = (short)Integer(value, "Decimal");
		return new()
		{
			Market = market,
			Symbol = symbol,
			Name = Text(value, "StkName"),
			ExtendedName = Text(value, "ExtName"),
			Decimals = decimals,
			PreviousClose = Number(value, "YstPrice"),
			PriceStep = DecimalStep(decimals),
			SecurityType = market.ToSecurityType(symbol, Text(value, "StkName")),
		};
	}

	private YuantaAccountInfo ReadAccount(object value)
		=> new()
		{
			Account = Text(value, "Account"),
			Name = Text(value, "Name"),
			InvestorId = Text(value, "InvestorID"),
			SellerNo = (short)Integer(value, "SellerNo"),
		};

	private YuantaOrderRequest PrepareRequest(YuantaOrderRequest request, long transactionId)
	{
		ArgumentNullException.ThrowIfNull(request);
		var account = EnsureAccount();
		if (!request.Account.IsEmpty() && !request.Account.EqualsIgnoreCase(account.Account))
			throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
		return CloneRequest(request, transactionId: transactionId,
			nativeId: Interlocked.Increment(ref _nativeId), account: account.Account,
			sellerNo: account.SellerNo);
	}

	private YuantaOrderRequest ResolveOrder(string orderId)
	{
		orderId.ThrowIfEmpty(nameof(orderId));
		return _orders.TryGetValue(orderId, out var request)
			? request
			: throw new InvalidOperationException($"Yuanta order '{orderId}' was not found in the current session.");
	}

	private static YuantaOrderRequest CloneRequest(YuantaOrderRequest source, long? transactionId = null,
		int? nativeId = null, string account = null, decimal? price = null, long? volume = null,
		short? sellerNo = null, string orderId = null, DateTime? tradeDate = null)
		=> new()
		{
			NativeId = nativeId ?? source.NativeId,
			TransactionId = transactionId ?? source.TransactionId,
			Account = account ?? source.Account,
			Market = source.Market,
			Symbol = source.Symbol,
			OrderSymbol = source.OrderSymbol,
			SecurityType = source.SecurityType,
			Side = source.Side,
			OrderType = source.OrderType,
			TimeInForce = source.TimeInForce,
			Price = price ?? source.Price,
			Volume = volume ?? source.Volume,
			StockMarketType = source.StockMarketType,
			StockOrderType = source.StockOrderType,
			PositionEffect = source.PositionEffect,
			FuturesPriceType = source.FuturesPriceType,
			SettlementMonth = source.SettlementMonth,
			OptionType = source.OptionType,
			StrikePrice = source.StrikePrice,
			IsDayTrade = source.IsDayTrade,
			IsPreOrder = source.IsPreOrder,
			SellerNo = sellerNo ?? source.SellerNo,
			UserTag = source.UserTag,
			OrderId = orderId ?? source.OrderId,
			TradeDate = tradeDate ?? source.TradeDate,
		};

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

	private YuantaSdkBridge EnsureBridge()
		=> _bridge ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private YuantaAccountInfo EnsureAccount()
		=> _account ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private string Text(object target, string name)
		=> target == null ? null : EnsureBridge().Member(target, name)?.ToString();

	private decimal Number(object target, string name)
		=> NullableNumber(target, name) ?? 0;

	private decimal? NullableNumber(object target, string name)
	{
		var value = target == null ? null : EnsureBridge().Member(target, name);
		if (value == null)
			return null;
		return value is string text
			? ParseNumber(text)
			: Convert.ToDecimal(value, CultureInfo.InvariantCulture);
	}

	private int Integer(object target, string name)
	{
		var value = target == null ? null : EnsureBridge().Member(target, name);
		return value == null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
	}

	private long Long(object target, string name)
	{
		var value = target == null ? null : EnsureBridge().Member(target, name);
		return value == null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
	}

	private DateTime ReadDateTime(object date, object time)
	{
		var taipei = DateTime.UtcNow.ToTaipeiTime();
		var year = date == null ? taipei.Year : Integer(date, "ushtYear");
		var month = date == null ? taipei.Month : Integer(date, "bytMon");
		var day = date == null ? taipei.Day : Integer(date, "bytDay");
		if (year <= 0 || month <= 0 || day <= 0)
			return default;
		var hour = time == null ? 0 : Integer(time, "bytHour");
		var minute = time == null ? 0 : Integer(time, "bytMin");
		var second = time == null ? 0 : Integer(time, "bytSec");
		var millisecond = time == null ? 0 : Integer(time, "ushtMSec");
		return new DateTime(year, month, day, hour, minute, second, millisecond,
			DateTimeKind.Unspecified).FromTaipeiTime();
	}

	private DateTime ReadSdkDateTime(object value)
	{
		if (value is not DateTime dateTime)
			return default;
		if (dateTime.Kind == DateTimeKind.Utc)
			return dateTime;
		if (dateTime.Year <= 1900)
		{
			var today = DateTime.UtcNow.ToTaipeiTime();
			dateTime = new DateTime(today.Year, today.Month, today.Day,
				dateTime.Hour, dateTime.Minute, dateTime.Second, dateTime.Millisecond,
				DateTimeKind.Unspecified);
		}
		return dateTime.FromTaipeiTime();
	}

	private static decimal DecimalStep(short decimals)
	{
		if (decimals is < 0 or > 10)
			return 0;
		var value = 1m;
		for (var index = 0; index < decimals; index++)
			value /= 10m;
		return value;
	}

	private static decimal? ParseNumber(string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
			? result : null;

	private static Sides ToSide(string value)
		=> value.EqualsIgnoreCase("S") || value.EqualsIgnoreCase("Sell") ? Sides.Sell : Sides.Buy;

	private static OptionTypes? ReadOptionType(string productType, string name)
	{
		var text = $"{productType} {name}";
		if (text.Contains("CALL", StringComparison.OrdinalIgnoreCase) || text.Contains('買'))
			return OptionTypes.Call;
		if (text.Contains("PUT", StringComparison.OrdinalIgnoreCase) || text.Contains('賣'))
			return OptionTypes.Put;
		return null;
	}

	private static TimeInForce ToTimeInForce(string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"2" or "3" or "IOC" => TimeInForce.CancelBalance,
			"4" or "I" or "FOK" => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	private static OrderStates ToOrderState(int status, decimal volume, decimal filled)
		=> status switch
		{
			10 or 24 or 25 => OrderStates.Failed,
			30 => OrderStates.Done,
			0 or 5 => OrderStates.Pending,
			20 when volume > 0 && filled >= volume => OrderStates.Done,
			_ => OrderStates.Active,
		};

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error?.Invoke(error, cancellationToken) ?? default;

	private ValueTask RaiseConnectionLostAsync(Exception error)
		=> ConnectionLost?.Invoke(error, CancellationToken.None) ?? default;
}
