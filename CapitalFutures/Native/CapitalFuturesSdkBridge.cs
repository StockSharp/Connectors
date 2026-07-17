namespace StockSharp.CapitalFutures.Native;

sealed class CapitalFuturesSdkBridge : IDisposable
{
	private sealed class EventBinding
	{
		public object Target { get; init; }
		public EventInfo Event { get; init; }
		public Delegate Handler { get; init; }
	}

	private static readonly MethodInfo _dispatchMethod = typeof(CapitalFuturesSdkBridge)
		.GetMethod(nameof(DispatchEvent), BindingFlags.Instance | BindingFlags.NonPublic);

	private readonly string _sdkPath;
	private readonly string _login;
	private readonly string _password;
	private readonly CapitalFuturesEnvironments _environment;
	private readonly bool _isTradingEnabled;
	private readonly string _logPath;
	private readonly List<EventBinding> _eventBindings = [];
	private readonly List<CapitalAccountInfo> _accounts = [];
	private readonly ConcurrentDictionary<string, CapitalInstrumentInfo> _instrumentsByIndex = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, CapitalInstrumentInfo> _instrumentsBySymbol = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _level1Symbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, short> _tickPages = new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _portfolioSync = new(1, 1);
	private CapitalFuturesStaDispatcher _dispatcher;
	private Assembly _assembly;
	private Type _stockType;
	private Type _futureOrderType;
	private object _center;
	private object _quote;
	private object _order;
	private object _reply;
	private TaskCompletionSource<object> _quoteReady;
	private TaskCompletionSource<object> _accountReady;
	private TaskCompletionSource<object> _replyReady;
	private TaskCompletionSource<CapitalPortfolioInfo> _portfolioResponse;
	private TaskCompletionSource<IReadOnlyList<CapitalPositionInfo>> _positionsResponse;
	private int _marketPrice = int.MinValue + 1;
	private int _isDisposed;

	public CapitalFuturesSdkBridge(string sdkPath, string login, SecureString password,
		CapitalFuturesEnvironments environment, bool isTradingEnabled, string logPath)
	{
		_sdkPath = sdkPath.ThrowIfEmpty(nameof(sdkPath));
		_login = login.ThrowIfEmpty(nameof(login)).Trim().ToUpperInvariant();
		_password = password?.UnSecure();
		_environment = environment;
		_isTradingEnabled = isTradingEnabled;
		_logPath = logPath;
	}

	public event Action<CapitalInstrumentInfo> QuoteReceived;
	public event Action<CapitalTradeUpdate> TradeReceived;
	public event Action<CapitalBookUpdate> BookReceived;
	public event Action<CapitalOrderReport> OrderReceived;
	public event Action<Exception> Error;
	public event Action<Exception> ConnectionLost;

	public string Version { get; private set; }
	public IReadOnlyList<CapitalAccountInfo> Accounts
	{
		get
		{
			lock (_accounts)
				return [.. _accounts];
		}
	}

	public async Task ConnectAsync(CancellationToken cancellationToken)
	{
		if (_dispatcher != null)
			throw new InvalidOperationException("Capital Futures SDK is already connected.");
		if (_password.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.PasswordNotSpecified);

		_quoteReady = NewCompletion();
		if (_isTradingEnabled)
		{
			_accountReady = NewCompletion();
			_replyReady = NewCompletion();
		}

		_dispatcher = new();
		try
		{
			await _dispatcher.InvokeAsync(Initialize, cancellationToken);
			await _quoteReady.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
			if (_isTradingEnabled)
			{
				await _accountReady.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
				await _replyReady.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
				await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
			}
		}
		catch
		{
			await DisconnectAsync();
			throw;
		}
		finally
		{
			_quoteReady = null;
			_accountReady = null;
			_replyReady = null;
		}
	}

	public async Task DisconnectAsync()
	{
		var dispatcher = _dispatcher;
		if (dispatcher == null)
			return;

		_dispatcher = null;
		try
		{
			await dispatcher.InvokeAsync(Teardown, CancellationToken.None);
		}
		catch
		{
		}
		finally
		{
			dispatcher.Dispose();
		}
	}

	public Task<CapitalInstrumentInfo> GetInstrumentAsync(string symbol,
		SecurityTypes? securityType, CancellationToken cancellationToken)
		=> InvokeAsync(() => GetInstrument(symbol, securityType), cancellationToken);

	public Task SubscribeLevel1Async(string symbol, CancellationToken cancellationToken)
		=> InvokeAsync(() => SubscribeLevel1(symbol), cancellationToken);

	public Task UnsubscribeLevel1Async(string symbol, CancellationToken cancellationToken)
		=> InvokeAsync(() => UnsubscribeLevel1(symbol), cancellationToken);

	public Task SubscribeTicksAsync(string symbol, CancellationToken cancellationToken)
		=> InvokeAsync(() => SubscribeTicks(symbol), cancellationToken);

	public Task UnsubscribeTicksAsync(string symbol, CancellationToken cancellationToken)
		=> InvokeAsync(() => UnsubscribeTicks(symbol), cancellationToken);

	public Task KeepAliveAsync(CancellationToken cancellationToken)
		=> InvokeAsync(() => CheckCode(ToInt(Invoke(_quote, "SKQuoteLib_RequestServerTime")),
			"SKQuoteLib_RequestServerTime"), cancellationToken);

	public Task<CapitalOrderResponse> PlaceOrderAsync(CapitalOrderRequest request,
		CancellationToken cancellationToken)
		=> InvokeAsync(() => PlaceOrder(request), cancellationToken);

	public Task<CapitalOrderResponse> CancelOrderAsync(string account, string orderId,
		CancellationToken cancellationToken)
		=> InvokeAsync(() => InvokeOrderCommand("CancelOrderBySeqNo",
			[_login, false, account, orderId, null]), cancellationToken);

	public Task<CapitalOrderResponse> ReplacePriceAsync(string account, string orderId,
		decimal price, TimeInForce timeInForce, CancellationToken cancellationToken)
		=> InvokeAsync(() => InvokeOrderCommand("CorrectPriceBySeqNo",
			[_login, false, account, orderId, FormatPrice(price), (int)timeInForce.ToTradeType(), null]),
			cancellationToken);

	public Task<CapitalOrderResponse> DecreaseOrderAsync(string account, string orderId,
		int decreaseVolume, CancellationToken cancellationToken)
		=> InvokeAsync(() => InvokeOrderCommand("DecreaseOrderBySeqNo",
			[_login, false, account, orderId, decreaseVolume, null]), cancellationToken);

	public async Task<CapitalPortfolioSnapshot> GetPortfolioAsync(string account,
		CancellationToken cancellationToken)
	{
		await _portfolioSync.WaitAsync(cancellationToken);
		try
		{
			_portfolioResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
			_positionsResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
			await InvokeAsync(() =>
			{
				CheckCode(ToInt(Invoke(_order, "GetFutureRights", _login, account, (short)1)),
					"GetFutureRights");
				CheckCode(ToInt(Invoke(_order, "GetOpenInterest", _login, account)),
					"GetOpenInterest");
			}, cancellationToken);

			return new()
			{
				Portfolio = await _portfolioResponse.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken),
				Positions = await _positionsResponse.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken),
			};
		}
		finally
		{
			_portfolioResponse = null;
			_positionsResponse = null;
			_portfolioSync.Release();
		}
	}

	public Task<bool> IsConnectedAsync(CancellationToken cancellationToken)
		=> InvokeAsync(() => ToInt(Invoke(_quote, "SKQuoteLib_IsConnected")) != 0, cancellationToken);

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
			return;
		try
		{
			DisconnectAsync().GetAwaiter().GetResult();
		}
		catch
		{
		}
		_portfolioSync.Dispose();
	}

	private void Initialize()
	{
		var interopPath = ResolveInteropPath(_sdkPath);
		_assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(interopPath);
		_stockType = RequireType("SKCOMLib.SKSTOCKLONG");
		_futureOrderType = RequireType("SKCOMLib.FUTUREORDER");
		_center = CreateComObject("SKCOMLib.SKCenterLibClass");
		_quote = CreateComObject("SKCOMLib.SKQuoteLibClass");

		Attach(_quote, "OnConnection");
		Attach(_quote, "OnNotifyQuoteLONG");
		Attach(_quote, "OnNotifyTicksLONG");
		Attach(_quote, "OnNotifyBest5LONG");

		if (_isTradingEnabled)
		{
			_order = CreateComObject("SKCOMLib.SKOrderLibClass");
			_reply = CreateComObject("SKCOMLib.SKReplyLibClass");
			Attach(_order, "OnAccount");
			Attach(_order, "OnFutureRights");
			Attach(_order, "OnFutureRightsStatus", false);
			Attach(_order, "OnOpenInterestJson");
			Attach(_order, "OnOpenInterestGWStatus", false);
			Attach(_reply, "OnConnect");
			Attach(_reply, "OnDisconnect");
			Attach(_reply, "OnComplete");
			Attach(_reply, "OnNewData");
			Attach(_reply, "OnSolaceReplyConnection", false);
			Attach(_reply, "OnSolaceReplyDisconnect", false);
		}

		if (!_logPath.IsEmpty())
			CheckCode(ToInt(Invoke(_center, "SKCenterLib_SetLogPath", _logPath)), "SKCenterLib_SetLogPath");
		CheckCode(ToInt(Invoke(_center, "SKCenterLib_SetAuthority", (int)_environment)),
			"SKCenterLib_SetAuthority");

		var loginCode = ToInt(Invoke(_center, "SKCenterLib_Login", _login, _password));
		if (loginCode != 0 && loginCode is not (>= 600 and <= 699))
			CheckCode(loginCode, "SKCenterLib_Login");

		Version = Convert.ToString(Invoke(_center, "SKCenterLib_GetSKAPIVersionAndBit", _login),
			CultureInfo.InvariantCulture).IsEmpty(GetFileVersion(interopPath));

		try
		{
			_marketPrice = ToInt(Invoke(_quote, "SKQuoteLib_GetMarketPriceTS"));
		}
		catch
		{
			_marketPrice = int.MinValue + 1;
		}

		CheckCode(ToInt(Invoke(_quote, "SKQuoteLib_EnterMonitorLONG")), "SKQuoteLib_EnterMonitorLONG");

		if (!_isTradingEnabled)
			return;

		CheckCode(ToInt(Invoke(_order, "SKOrderLib_Initialize")), "SKOrderLib_Initialize");
		CheckCode(ToInt(Invoke(_order, "ReadCertByID", _login)), "ReadCertByID");
		CheckCode(ToInt(Invoke(_order, "GetUserAccount")), "GetUserAccount");
		CheckCode(ToInt(Invoke(_reply, "SKReplyLib_ConnectByID", _login)), "SKReplyLib_ConnectByID");
	}

	private void Teardown()
	{
		if (_reply != null)
		{
			TryInvoke(_reply, "SKReplyLib_SolaceCloseByID", _login);
			TryInvoke(_reply, "SKReplyLib_CloseByID", _login);
		}
		if (_quote != null)
			TryInvoke(_quote, "SKQuoteLib_LeaveMonitor");

		for (var i = _eventBindings.Count - 1; i >= 0; i--)
		{
			var binding = _eventBindings[i];
			try
			{
				binding.Event.RemoveEventHandler(binding.Target, binding.Handler);
			}
			catch
			{
			}
		}
		_eventBindings.Clear();

		ReleaseComObject(ref _reply);
		ReleaseComObject(ref _order);
		ReleaseComObject(ref _quote);
		ReleaseComObject(ref _center);
		_assembly = null;
		_stockType = null;
		_futureOrderType = null;
		_instrumentsByIndex.Clear();
		_instrumentsBySymbol.Clear();
		_level1Symbols.Clear();
		_tickPages.Clear();
		lock (_accounts)
			_accounts.Clear();
	}

	private CapitalInstrumentInfo GetInstrument(string symbol, SecurityTypes? securityType)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant();
		if (securityType is SecurityTypes type)
			return GetInstrument(type.ToMarketNo(), symbol, true);

		foreach (var marketNo in new short[] { 2, 3 })
		{
			var instrument = GetInstrument(marketNo, symbol, false);
			if (instrument != null)
				return instrument;
		}

		throw new InvalidOperationException($"Capital Futures instrument '{symbol}' was not found in markets 2 or 3.");
	}

	private CapitalInstrumentInfo GetInstrument(short marketNo, string symbol, bool throwOnError)
	{
		if (_instrumentsBySymbol.TryGetValue(GetSymbolKey(marketNo, symbol), out var cached))
			return cached;

		var native = Activator.CreateInstance(_stockType);
		var arguments = new object[] { marketNo, symbol, native };
		var code = ToInt(Invoke(_quote, "SKQuoteLib_GetStockByMarketAndNo", arguments));
		if (code != 0)
		{
			if (throwOnError)
				CheckCode(code, "SKQuoteLib_GetStockByMarketAndNo");
			return null;
		}

		return ReadInstrument(arguments[2] ?? native, marketNo);
	}

	private CapitalInstrumentInfo GetInstrument(short marketNo, int nativeIndex, bool refresh = false)
	{
		var key = GetIndexKey(marketNo, nativeIndex);
		if (!refresh && _instrumentsByIndex.TryGetValue(key, out var instrument))
			return instrument;

		var native = Activator.CreateInstance(_stockType);
		var arguments = new object[] { marketNo, nativeIndex, native };
		CheckCode(ToInt(Invoke(_quote, "SKQuoteLib_GetStockByIndexLONG", arguments)),
			"SKQuoteLib_GetStockByIndexLONG");
		return ReadInstrument(arguments[2] ?? native, marketNo);
	}

	private CapitalInstrumentInfo ReadInstrument(object native, short fallbackMarketNo)
	{
		var marketCode = Text(native, "bstrMarketNo");
		var marketNo = short.TryParse(marketCode, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var parsedMarket) ? parsedMarket : fallbackMarketNo;
		var decimals = Short(native, "sDecimal");
		var date = Integer(native, "nTradingDay");
		var time = Integer(native, "nDealTime");
		var instrument = new CapitalInstrumentInfo
		{
			MarketNo = marketNo,
			NativeIndex = Integer(native, "nStockIdx"),
			MarketCode = marketCode,
			Symbol = Text(native, "bstrStockNo").Trim(),
			Name = Text(native, "bstrStockName").Trim(),
			Decimals = decimals,
			SecurityType = marketNo.ToSecurityType(),
			Open = Price(Integer(native, "nOpen"), decimals),
			High = Price(Integer(native, "nHigh"), decimals),
			Low = Price(Integer(native, "nLow"), decimals),
			Close = Price(Integer(native, "nClose"), decimals),
			PreviousClose = Price(Integer(native, "nRef"), decimals),
			BestBidPrice = Price(Integer(native, "nBid"), decimals),
			BestBidVolume = Positive(Integer(native, "nBc")),
			BestAskPrice = Price(Integer(native, "nAsk"), decimals),
			BestAskVolume = Positive(Integer(native, "nAc")),
			LastVolume = Positive(Integer(native, "nTickQty")),
			TotalVolume = Positive(Integer(native, "nTQty")),
			OpenInterest = Positive(Integer(native, "nFutureOI")),
			MaxPrice = Price(Integer(native, "nUp"), decimals),
			MinPrice = Price(Integer(native, "nDown"), decimals),
			ServerTime = ParseTaipeiDateTime(date, time, 0),
			IsSimulated = Integer(native, "nSimulate") != 0,
		};

		if (instrument.Symbol.IsEmpty())
			throw new InvalidOperationException("Capital API returned an instrument without a symbol.");
		_instrumentsByIndex[GetIndexKey(marketNo, instrument.NativeIndex)] = instrument;
		_instrumentsBySymbol[GetSymbolKey(marketNo, instrument.Symbol)] = instrument;
		return instrument;
	}

	private CapitalOrderResponse PlaceOrder(CapitalOrderRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var native = Activator.CreateInstance(_futureOrderType);
		SetField(native, "bstrFullAccount", request.Account);
		SetField(native, "bstrStockNo", request.Symbol);
		SetField(native, "bstrPrice", request.PriceType switch
		{
			CapitalFuturesPriceTypes.Market => "M",
			CapitalFuturesPriceTypes.MarketWithProtection => "P",
			_ => request.OrderType == OrderTypes.Market ? "M" : FormatPrice(request.Price),
		});
		SetField(native, "nQty", request.Volume);
		SetField(native, "sBuySell", (short)(request.Side == Sides.Buy ? 0 : 1));
		SetField(native, "sTradeType", request.TimeInForce.ToTradeType());
		SetField(native, "sDayTrade", (short)(request.IsDayTrade ? 1 : 0));
		SetField(native, "sNewClose", (short)request.PositionEffect);
		SetField(native, "sReserved", (short)(request.IsPreOrder ? 1 : 0));
		SetFieldIfExists(native, "bstrTrigger", string.Empty);
		SetFieldIfExists(native, "bstrDealPrice", string.Empty);
		SetFieldIfExists(native, "bstrMovingPoint", string.Empty);

		var method = request.SecurityType == SecurityTypes.Option
			? "SendOptionOrder"
			: "SendFutureOrderCLR";
		return InvokeOrderCommand(method, [_login, false, native, null]);
	}

	private CapitalOrderResponse InvokeOrderCommand(string method, object[] arguments)
	{
		if (_order == null)
			throw new InvalidOperationException("Capital Futures trading services are disabled.");
		var code = ToInt(Invoke(_order, method, arguments));
		var message = Convert.ToString(arguments[^1], CultureInfo.InvariantCulture)?.Trim();
		CheckCode(code, method, message);
		return new()
		{
			SequenceId = message,
			Message = message,
			ServerTime = DateTime.UtcNow,
		};
	}

	private void SubscribeLevel1(string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant();
		if (!_level1Symbols.Add(symbol))
			return;
		if (_level1Symbols.Count > 100)
		{
			_level1Symbols.Remove(symbol);
			throw new InvalidOperationException("Capital API permits at most 100 instruments in the Level1 subscription.");
		}

		try
		{
			var arguments = new object[]
			{
				(short)1,
				string.Join(",", _level1Symbols.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)),
			};
			CheckCode(ToInt(Invoke(_quote, "SKQuoteLib_RequestStocks", arguments)),
				"SKQuoteLib_RequestStocks");
		}
		catch
		{
			_level1Symbols.Remove(symbol);
			throw;
		}
	}

	private void UnsubscribeLevel1(string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant();
		if (!_level1Symbols.Contains(symbol))
			return;
		CheckCode(ToInt(Invoke(_quote, "SKQuoteLib_CancelRequestStocks", symbol)),
			"SKQuoteLib_CancelRequestStocks");
		_level1Symbols.Remove(symbol);
	}

	private void SubscribeTicks(string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant();
		if (_tickPages.ContainsKey(symbol))
			return;

		var page = Enumerable.Range(0, 10)
			.Select(static value => (short)value)
			.FirstOrDefault(candidate => !_tickPages.ContainsValue(candidate), (short)-1);
		if (page < 0)
			throw new InvalidOperationException("Capital API permits ticks and depth for at most 10 instruments.");

		var arguments = new object[] { page, symbol };
		CheckCode(ToInt(Invoke(_quote, "SKQuoteLib_RequestTicks", arguments)),
			"SKQuoteLib_RequestTicks");
		_tickPages[symbol] = Convert.ToInt16(arguments[0], CultureInfo.InvariantCulture);
	}

	private void UnsubscribeTicks(string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant();
		if (!_tickPages.ContainsKey(symbol))
			return;
		CheckCode(ToInt(Invoke(_quote, "SKQuoteLib_CancelRequestTicks", symbol)),
			"SKQuoteLib_CancelRequestTicks");
		_tickPages.Remove(symbol);
	}

	private void DispatchEvent(string eventName, object[] arguments)
	{
		try
		{
			switch (eventName)
			{
				case "OnConnection":
					OnQuoteConnection(ToInt(arguments[0]), ToInt(arguments[1]));
					break;
				case "OnNotifyQuoteLONG":
					QuoteReceived?.Invoke(GetInstrument(ToShort(arguments[0]), ToInt(arguments[1]), true));
					break;
				case "OnNotifyTicksLONG":
					OnTick(arguments);
					break;
				case "OnNotifyBest5LONG":
					OnBest5(arguments);
					break;
				case "OnAccount":
					OnAccount(Text(arguments[0]), Text(arguments[1]));
					break;
				case "OnNewData":
					OnOrderReport(Text(arguments[1]));
					break;
				case "OnConnect":
				case "OnSolaceReplyConnection":
					OnReplyConnection(ToInt(arguments[1]));
					break;
				case "OnComplete":
					_replyReady?.TrySetResult(null);
					break;
				case "OnDisconnect":
				case "OnSolaceReplyDisconnect":
					OnReplyDisconnect(ToInt(arguments[1]));
					break;
				case "OnFutureRights":
					OnFutureRights(Text(arguments[0]));
					break;
				case "OnFutureRightsStatus":
					OnQueryStatus(_portfolioResponse, ToInt(arguments[0]), Text(arguments[1]), "future rights");
					break;
				case "OnOpenInterestJson":
					OnOpenInterestJson(Text(arguments[0]));
					break;
				case "OnOpenInterestGWStatus":
					OnQueryStatus(_positionsResponse, ToInt(arguments[0]), Text(arguments[1]), "open interest");
					break;
			}
		}
		catch (Exception error)
		{
			Error?.Invoke(error);
		}
	}

	private void OnQuoteConnection(int kind, int code)
	{
		if (kind == 3003)
		{
			_quoteReady?.TrySetResult(null);
			return;
		}

		if (kind is not (3002 or 3021 or 3033))
			return;
		var error = CreateSdkError(code, $"Capital quote connection state {kind}");
		if (_quoteReady?.TrySetException(error) != true)
			ConnectionLost?.Invoke(error);
	}

	private void OnReplyConnection(int code)
	{
		if (code == 0)
			_replyReady?.TrySetResult(null);
		else
			_replyReady?.TrySetException(CreateSdkError(code, "Capital reply connection"));
	}

	private void OnReplyDisconnect(int code)
	{
		var error = CreateSdkError(code, "Capital reply connection was lost");
		if (_replyReady?.TrySetException(error) != true)
			ConnectionLost?.Invoke(error);
	}

	private void OnTick(object[] values)
	{
		var marketNo = ToShort(values[0]);
		var nativeIndex = ToInt(values[1]);
		var instrument = GetInstrument(marketNo, nativeIndex);
		var price = Price(ToInt(values[8]), instrument.Decimals);
		var volume = Math.Max(0, ToInt(values[9]));
		if (price == null || volume == 0)
			return;

		var update = new CapitalTradeUpdate
		{
			MarketNo = marketNo,
			NativeIndex = nativeIndex,
			Symbol = instrument.Symbol,
			Sequence = ToInt(values[2]),
			ServerTime = ParseTaipeiDateTime(ToInt(values[3]), ToInt(values[4]), ToInt(values[5])),
			BestBidPrice = Price(ToInt(values[6]), instrument.Decimals),
			BestAskPrice = Price(ToInt(values[7]), instrument.Decimals),
			Price = price.Value,
			Volume = volume,
			IsSimulated = ToInt(values[10]) != 0,
		};
		TradeReceived?.Invoke(update);
	}

	private void OnBest5(object[] values)
	{
		var marketNo = ToShort(values[0]);
		var nativeIndex = ToInt(values[1]);
		var instrument = GetInstrument(marketNo, nativeIndex);
		var bids = new List<CapitalBookLevel>(5);
		var asks = new List<CapitalBookLevel>(5);
		for (var i = 0; i < 5; i++)
		{
			AddLevel(bids, ToInt(values[2 + i * 2]), ToInt(values[3 + i * 2]), instrument.Decimals);
			AddLevel(asks, ToInt(values[14 + i * 2]), ToInt(values[15 + i * 2]), instrument.Decimals);
		}
		BookReceived?.Invoke(new()
		{
			MarketNo = marketNo,
			NativeIndex = nativeIndex,
			Symbol = instrument.Symbol,
			ServerTime = DateTime.UtcNow,
			Bids = bids,
			Asks = asks,
			IsSimulated = ToInt(values[26]) != 0,
		});
	}

	private void OnAccount(string login, string value)
	{
		var fields = value.Split(',');
		if (fields.Length < 4)
			throw new FormatException($"Capital API returned malformed account data: '{value}'.");
		var account = new CapitalAccountInfo
		{
			Login = login,
			Market = fields[0].Trim(),
			BrokerId = fields[1].Trim(),
			Branch = fields[2].Trim(),
			Account = fields[3].Trim(),
			CustomerId = Field(fields, 4),
			Name = Field(fields, 5),
		};
		lock (_accounts)
		{
			if (_accounts.All(item => !item.Market.EqualsIgnoreCase(account.Market) ||
				!item.FullAccount.EqualsIgnoreCase(account.FullAccount)))
				_accounts.Add(account);
		}
		if (account.IsDomesticFutures)
			_accountReady?.TrySetResult(null);
	}

	private void OnOrderReport(string value)
	{
		var fields = value.Split(',');
		if (fields.Length < 49)
			throw new FormatException($"Capital API order report has {fields.Length} fields instead of 49.");
		var marketType = fields[1].Trim().ToUpperInvariant();
		if (marketType is not ("TF" or "TO"))
			return;
		var flags = fields[6].Trim().ToUpperInvariant();
		var positionFlag = Char(flags, 1);
		var priceFlag = Char(flags, 3);
		var report = new CapitalOrderReport
		{
			KeyNumber = fields[0].Trim(),
			SequenceId = fields[47].Trim(),
			BookId = fields[10].Trim(),
			MarketType = marketType,
			ReportType = ToReportType(Char(fields[2], 0)),
			IsError = Char(fields[3], 0) is 'Y' or 'T',
			Account = fields[4].Trim() + fields[5].Trim(),
			Symbol = fields[8].Trim(),
			SecurityType = marketType == "TO" ? SecurityTypes.Option : SecurityTypes.Future,
			Side = Char(flags, 0) is '1' or 'S' ? Sides.Sell : Sides.Buy,
			PositionEffect = positionFlag switch
			{
				'O' => CapitalFuturesPositionEffects.Close,
				'N' => CapitalFuturesPositionEffects.Open,
				_ => CapitalFuturesPositionEffects.Auto,
			},
			PriceType = priceFlag switch
			{
				'1' => CapitalFuturesPriceTypes.Market,
				'3' => CapitalFuturesPriceTypes.MarketWithProtection,
				_ => CapitalFuturesPriceTypes.Auto,
			},
			TimeInForce = Char(flags, 2).ToTimeInForce(),
			OrderType = priceFlag == '2' ? OrderTypes.Limit : OrderTypes.Market,
			Price = Decimal(fields[11]),
			Volume = Decimal(fields[20]),
			ServerTime = ParseReportTime(fields[23], fields[24]),
			TradeId = fields[38].Trim().IsEmpty(fields[25].Trim()),
			IsDayTrade = positionFlag == 'Y',
			IsPreOrder = fields[31].Trim().EqualsIgnoreCase("B"),
			Error = string.Join(" ", new[] { fields[44].Trim(), fields[46].Trim() }
				.Where(item => !item.IsEmpty())),
		};
		OrderReceived?.Invoke(report);
	}

	private void OnFutureRights(string value)
	{
		if (_portfolioResponse == null || value.IsEmpty() || value.StartsWith("##", StringComparison.Ordinal))
			return;
		var fields = value.Split(',');
		if (fields.Length < 41)
			throw new FormatException($"Capital API future-rights report has {fields.Length} fields instead of 41.");
		_portfolioResponse.TrySetResult(new()
		{
			AccountBalance = NullableDecimal(fields[0]),
			UnrealizedPnL = NullableDecimal(fields[1]),
			Equity = NullableDecimal(fields[6]),
			InitialMargin = NullableDecimal(fields[13]),
			RealizedPnL = NullableDecimal(fields[11]),
			Currency = fields[25].Trim(),
			Available = NullableDecimal(fields[31]),
			Account = fields[40].Trim(),
		});
	}

	private void OnOpenInterestJson(string value)
	{
		if (_positionsResponse == null)
			return;
		var records = JsonSerializer.Deserialize<string[]>(value) ?? [];
		var positions = new List<CapitalPositionInfo>(records.Length);
		foreach (var record in records)
		{
			var fields = record.Split(',');
			if (fields.Length >= 2 && fields[0].Trim() == "001")
				continue;
			if (fields.Length < 7)
				throw new FormatException($"Capital API open-interest record has {fields.Length} fields instead of at least 7.");
			positions.Add(new()
			{
				Account = fields[1].Trim(),
				Symbol = fields[2].Trim(),
				Side = fields[3].Trim() is "1" or "S" or "s"
					? Sides.Sell
					: Sides.Buy,
				CurrentValue = Decimal(fields[4]),
				DayTradeValue = Decimal(fields[5]),
				AveragePrice = Decimal(fields[6]),
			});
		}
		_positionsResponse.TrySetResult(positions);
	}

	private static void OnQueryStatus<T>(TaskCompletionSource<T> completion, int status,
		string message, string query)
	{
		if (status != 0)
			completion?.TrySetException(new InvalidOperationException(
				message.IsEmpty($"Capital Futures {query} query failed.")));
	}

	private void Attach(object target, string eventName, bool required = true)
	{
		var eventInfo = target.GetType().GetEvent(eventName);
		if (eventInfo == null)
		{
			if (required)
				throw new MissingMemberException(target.GetType().FullName, eventName);
			return;
		}
		var invoke = eventInfo.EventHandlerType.GetMethod("Invoke");
		var parameters = invoke.GetParameters()
			.Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
			.ToArray();
		if (parameters.Any(parameter => parameter.Type.IsByRef))
			throw new NotSupportedException($"Capital event {eventName} uses a by-reference callback.");
		var body = Expression.Call(Expression.Constant(this), _dispatchMethod,
			Expression.Constant(eventName),
			Expression.NewArrayInit(typeof(object), parameters.Select(parameter =>
				Expression.Convert(parameter, typeof(object)))));
		var handler = Expression.Lambda(eventInfo.EventHandlerType, body, parameters).Compile();
		eventInfo.AddEventHandler(target, handler);
		_eventBindings.Add(new() { Target = target, Event = eventInfo, Handler = handler });
	}

	private object CreateComObject(string typeName)
		=> Activator.CreateInstance(RequireType(typeName))
			?? throw new InvalidOperationException($"Cannot create Capital COM object {typeName}.");

	private Type RequireType(string typeName)
		=> _assembly.GetType(typeName, true, false);

	private object Invoke(object target, string methodName, params object[] arguments)
	{
		if (target == null)
			throw new InvalidOperationException("Capital Futures SDK object is not initialized.");
		var method = target.GetType().GetMethod(methodName)
			?? throw new MissingMethodException(target.GetType().FullName, methodName);
		try
		{
			return method.Invoke(target, arguments);
		}
		catch (TargetInvocationException error) when (error.InnerException != null)
		{
			throw new InvalidOperationException($"Capital API {methodName} failed: {error.InnerException.Message}",
				error.InnerException);
		}
	}

	private void TryInvoke(object target, string methodName, params object[] arguments)
	{
		try
		{
			_ = Invoke(target, methodName, arguments);
		}
		catch
		{
		}
	}

	private Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken)
		=> (_dispatcher ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk))
			.InvokeAsync(action, cancellationToken);

	private Task InvokeAsync(Action action, CancellationToken cancellationToken)
		=> (_dispatcher ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk))
			.InvokeAsync(action, cancellationToken);

	private void CheckCode(int code, string operation, string detail = null)
	{
		if (code == 0)
			return;
		throw CreateSdkError(code, detail.IsEmpty(operation));
	}

	private Exception CreateSdkError(int code, string operation)
	{
		string message = null;
		try
		{
			if (_center != null)
				message = Convert.ToString(Invoke(_center, "SKCenterLib_GetReturnCodeMessage", code),
					CultureInfo.InvariantCulture);
		}
		catch
		{
		}
		return new InvalidOperationException($"{operation} failed with Capital API code {code}: " +
			message.IsEmpty("unknown error"));
	}

	private static void ReleaseComObject(ref object value)
	{
		var current = value;
		value = null;
		if (!OperatingSystem.IsWindows() || current == null || !Marshal.IsComObject(current))
			return;
		try
		{
			_ = Marshal.FinalReleaseComObject(current);
		}
		catch
		{
		}
	}

	private static string ResolveInteropPath(string sdkPath)
	{
		var fullPath = Path.GetFullPath(sdkPath);
		if (File.Exists(fullPath))
		{
			if (Path.GetFileName(fullPath).EqualsIgnoreCase("Interop.SKCOMLib.dll"))
				return fullPath;
			var sibling = Path.Combine(Path.GetDirectoryName(fullPath), "Interop.SKCOMLib.dll");
			if (File.Exists(sibling))
				return sibling;
			throw new FileNotFoundException("Interop.SKCOMLib.dll was not found beside the selected SDK file.", sibling);
		}
		if (!Directory.Exists(fullPath))
			throw new DirectoryNotFoundException($"Capital Futures SDK path '{fullPath}' does not exist.");

		var architecture = Environment.Is64BitProcess ? "x64" : "x86";
		var preferred = new[]
		{
			Path.Combine(fullPath, "Interop.SKCOMLib.dll"),
			Path.Combine(fullPath, architecture, "Interop.SKCOMLib.dll"),
			Path.Combine(fullPath, "元件", architecture, "Interop.SKCOMLib.dll"),
		};
		foreach (var candidate in preferred)
		{
			if (File.Exists(candidate))
				return Path.GetFullPath(candidate);
		}

		var discovered = Directory.EnumerateFiles(fullPath, "Interop.SKCOMLib.dll", SearchOption.AllDirectories)
			.OrderByDescending(path => Path.GetDirectoryName(path)
				.EndsWith(architecture, StringComparison.OrdinalIgnoreCase))
			.ThenByDescending(path => File.Exists(Path.Combine(Path.GetDirectoryName(path), "SKCOM.dll")))
			.FirstOrDefault();
		return discovered ?? throw new FileNotFoundException(
			$"Interop.SKCOMLib.dll was not found under '{fullPath}'.");
	}

	private static string GetFileVersion(string interopPath)
	{
		var sdkFile = Path.Combine(Path.GetDirectoryName(interopPath), "SKCOM.dll");
		return File.Exists(sdkFile)
			? FileVersionInfo.GetVersionInfo(sdkFile).FileVersion
			: AssemblyName.GetAssemblyName(interopPath).Version?.ToString();
	}

	private static void SetField(object target, string name, object value)
		=> (target.GetType().GetField(name) ?? throw new MissingFieldException(target.GetType().FullName, name))
			.SetValue(target, value);

	private static void SetFieldIfExists(object target, string name, object value)
		=> target.GetType().GetField(name)?.SetValue(target, value);

	private static object Field(object target, string name)
		=> target?.GetType().GetField(name)?.GetValue(target);

	private static string Text(object target, string name)
		=> Convert.ToString(Field(target, name), CultureInfo.InvariantCulture) ?? string.Empty;

	private static string Text(object value)
		=> Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

	private static int Integer(object target, string name)
		=> ToInt(Field(target, name));

	private static short Short(object target, string name)
		=> ToShort(Field(target, name));

	private static int ToInt(object value)
		=> Convert.ToInt32(value, CultureInfo.InvariantCulture);

	private static short ToShort(object value)
		=> Convert.ToInt16(value, CultureInfo.InvariantCulture);

	private static decimal Decimal(string value)
		=> decimal.TryParse(value?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
			? result
			: 0;

	private static decimal? NullableDecimal(string value)
		=> decimal.TryParse(value?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	private static decimal? Positive(int value)
		=> value > 0 ? value : null;

	private decimal? Price(int value, short decimals)
	{
		if (value == _marketPrice || value == 0)
			return null;
		decimal divisor = 1;
		for (var i = 0; i < Math.Max(0, (int)decimals); i++)
			divisor *= 10;
		return value / divisor;
	}

	private decimal PriceRequired(int value, short decimals)
		=> Price(value, decimals) ?? 0;

	private void AddLevel(List<CapitalBookLevel> levels, int rawPrice, int rawVolume, short decimals)
	{
		var price = PriceRequired(rawPrice, decimals);
		if (price == 0 || rawVolume <= 0)
			return;
		levels.Add(new() { Price = price, Volume = rawVolume });
	}

	private static DateTime ParseTaipeiDateTime(int date, int time, int micros)
	{
		var text = $"{date:00000000}{time:000000}";
		if (!DateTime.TryParseExact(text, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
			DateTimeStyles.None, out var local))
			return DateTime.UtcNow;
		local = local.AddTicks(Math.Clamp(micros, 0, 999999) * 10L);
		return local.FromTaipeiTime();
	}

	private static DateTime ParseReportTime(string date, string time)
	{
		var normalizedTime = new string((time ?? string.Empty).Where(char.IsDigit).ToArray());
		if (normalizedTime.Length > 6)
			normalizedTime = normalizedTime[..6];
		if (normalizedTime.Length < 6)
			normalizedTime = normalizedTime.PadLeft(6, '0');
		return int.TryParse(date?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericDate) &&
			int.TryParse(normalizedTime, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericTime)
			? ParseTaipeiDateTime(numericDate, numericTime, 0)
			: DateTime.UtcNow;
	}

	private static CapitalReportTypes ToReportType(char value)
		=> value switch
		{
			'N' => CapitalReportTypes.New,
			'C' => CapitalReportTypes.Cancel,
			'U' => CapitalReportTypes.Decrease,
			'P' => CapitalReportTypes.Replace,
			'D' => CapitalReportTypes.Trade,
			'B' => CapitalReportTypes.ReplaceAndDecrease,
			'S' => CapitalReportTypes.ExchangeCancel,
			_ => CapitalReportTypes.Unknown,
		};

	private static char Char(string value, int index)
		=> value?.Length > index ? char.ToUpperInvariant(value[index]) : '\0';

	private static string Field(string[] fields, int index)
		=> fields.Length > index ? fields[index].Trim() : string.Empty;

	private static string GetIndexKey(short marketNo, int nativeIndex)
		=> $"{marketNo}|{nativeIndex}";

	private static string GetSymbolKey(short marketNo, string symbol)
		=> $"{marketNo}|{symbol?.ToUpperInvariant()}";

	private static string FormatPrice(decimal price)
		=> price.ToString("0.############################", CultureInfo.InvariantCulture);

	private static TaskCompletionSource<object> NewCompletion()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);
}
