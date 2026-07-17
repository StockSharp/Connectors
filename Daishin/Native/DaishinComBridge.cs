namespace StockSharp.Daishin.Native;

[SupportedOSPlatform("windows")]
sealed class DaishinComBridge : IDisposable
{
	private sealed class NativeFeed
	{
		public string Key { get; init; }
		public object Source { get; init; }
		public Guid EventInterface { get; init; }
		public Action Handler { get; init; }
	}

	private static readonly Guid _dibEvents = new("B8944520-09C3-11D4-8232-00105A7C4F8C");
	private static readonly Guid _sysDibEvents = new("60D7702A-57BA-4869-AF3F-292FDC909D75");
	private static readonly Guid _cybosEvents = new("17F70631-56E5-40FC-B94F-44ADD3A850B1");
	private readonly string _requestedAccount;
	private readonly DaishinStockMarkets _stockMarket;
	private readonly bool _isTradingEnabled;
	private readonly DaishinStaDispatcher _dispatcher;
	private readonly Dictionary<string, NativeFeed> _feeds = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, decimal> _lastVolumes = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, DaishinSecurityInfo> _securities = new(StringComparer.OrdinalIgnoreCase);
	private object _cybos;
	private object _codeManager;
	private object _futureCodes;
	private object _optionCodes;
	private object _tradeUtility;
	private object _stockConclusion;
	private object _futuresConclusion;
	private Action _disconnectHandler;
	private Action _stockConclusionHandler;
	private Action _futuresConclusionHandler;
	private IReadOnlyList<DaishinAccountInfo> _accounts = [];
	private int _isDisposed;

	public DaishinComBridge(string requestedAccount, DaishinStockMarkets stockMarket,
		bool isTradingEnabled)
	{
		if (!OperatingSystem.IsWindows())
			throw new PlatformNotSupportedException("Daishin CYBOS Plus requires Windows.");
		if (Environment.Is64BitProcess)
			throw new PlatformNotSupportedException(
				"Daishin CYBOS Plus is a 32-bit in-process COM API. Run StockSharp in an x86 process.");

		_requestedAccount = requestedAccount;
		_stockMarket = stockMarket;
		_isTradingEnabled = isTradingEnabled;
		_dispatcher = new();
	}

	public event Action<DaishinLevel1Update> Level1Received;
	public event Action<DaishinBookUpdate> BookReceived;
	public event Action<DaishinOrderUpdate> OrderReceived;
	public event Action<Exception> Error;
	public event Action<Exception> ConnectionLost;

	public string Version { get; private set; }
	public IReadOnlyList<DaishinAccountInfo> Accounts => _accounts;

	public Task ConnectAsync(CancellationToken cancellationToken)
		=> _dispatcher.InvokeAsync(Connect, cancellationToken);

	private void Connect()
	{
		if (_cybos != null)
			throw new InvalidOperationException("Daishin CYBOS Plus is already connected.");

		try
		{
			_cybos = Create("CpUtil.CpCybos");
			if (Get<int>(_cybos, "IsConnect") == 0)
				throw new InvalidOperationException(
					"CYBOS Plus or CREON Plus is not logged in. Start the vendor terminal and sign in first.");

			_disconnectHandler = OnDisconnected;
			Combine(_cybos, _cybosEvents, _disconnectHandler);
			Version = $"CYBOS Plus server {Get<short>(_cybos, "ServerType")}";
			_codeManager = Create("CpUtil.CpCodeMgr");
			_futureCodes = Create("CpUtil.CpFutureCode");
			_optionCodes = Create("CpUtil.CpOptionCode");

			if (_isTradingEnabled)
			{
				_tradeUtility = Create("CpTrade.CpTdUtil");
				var tradeResult = ConvertInt(Invoke(_tradeUtility, "TradeInit", 0));
				if (tradeResult != 0)
					throw new InvalidOperationException(
						$"CYBOS Plus TradeInit failed with code {tradeResult}. Unlock trading in the vendor terminal.");

				_accounts = ReadAccounts();
				if (!_requestedAccount.IsEmpty() && !_accounts.Any(item =>
					item.Account.EqualsIgnoreCase(_requestedAccount)))
					throw new InvalidOperationException(
						$"CYBOS Plus did not return requested account '{_requestedAccount}'.");
				if (_accounts.Count == 0)
					throw new InvalidOperationException("CYBOS Plus returned no stock or derivatives accounts.");

				_stockConclusion = Create("Dscbo1.CpConclusion");
				_stockConclusionHandler = OnStockConclusion;
				Combine(_stockConclusion, _dibEvents, _stockConclusionHandler);
				Invoke(_stockConclusion, "Subscribe");

				_futuresConclusion = Create("Dscbo1.CpFConclusion");
				_futuresConclusionHandler = OnFuturesConclusion;
				Combine(_futuresConclusion, _dibEvents, _futuresConclusionHandler);
				Invoke(_futuresConclusion, "Subscribe");
			}
		}
		catch
		{
			Disconnect();
			throw;
		}
	}

	public Task<bool> IsConnectedAsync(CancellationToken cancellationToken)
		=> _dispatcher.InvokeAsync(() => _cybos != null && Get<int>(_cybos, "IsConnect") != 0,
			cancellationToken);

	public Task DisconnectAsync(CancellationToken cancellationToken)
		=> _dispatcher.InvokeAsync(Disconnect, cancellationToken);

	public Task<IReadOnlyList<DaishinSecurityInfo>> GetSecuritiesAsync(string code,
		ISet<SecurityTypes> securityTypes, CancellationToken cancellationToken)
		=> _dispatcher.InvokeAsync<IReadOnlyList<DaishinSecurityInfo>>(() =>
		{
			EnsureConnected();
			LoadSecurities(securityTypes);
			if (code.IsEmpty())
				return [.. _securities.Values.Where(item => securityTypes.Count == 0 ||
					securityTypes.Contains(item.SecurityType)).OrderBy(item => item.Code)];

			var nativeCode = code.ToNativeStockCode();
			return [.. _securities.Values.Where(item =>
				(item.Code.EqualsIgnoreCase(code) || item.Code.EqualsIgnoreCase(nativeCode) ||
					item.Code.TrimStart('A').EqualsIgnoreCase(code)) &&
				(securityTypes.Count == 0 || securityTypes.Contains(item.SecurityType)))];
		}, cancellationToken);

	public Task<DaishinLevel1Update> GetSnapshotAsync(DaishinSecurityInfo security,
		CancellationToken cancellationToken)
		=> _dispatcher.InvokeAsync(() => GetSnapshot(security, cancellationToken), cancellationToken);

	private DaishinLevel1Update GetSnapshot(DaishinSecurityInfo security,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(security);
		var request = Create("CpSysDib.MarketEye");
		try
		{
			int[] fields = [0, 1, 4, 5, 6, 7, 8, 9, 10, 11, 15, 16, 27];
			SetInput(request, 0, fields);
			SetInput(request, 1, security.Code);
			SetInput(request, 3, _stockMarket.ToNativeMarket());
			Request(request, 1, cancellationToken);
			if (Header<int>(request, 2) == 0)
				throw new InvalidOperationException($"CYBOS Plus returned no quote for '{security.Code}'.");

			return new()
			{
				Code = Data<string>(request, 0, 0).IsEmpty(security.Code),
				SecurityType = security.SecurityType,
				ServerTime = (Data<int>(request, 1, 0) * 100).ParseKoreaTime(DateTime.UtcNow),
				LastPrice = Positive(Data<decimal>(request, 2, 0)),
				OpenPrice = Positive(Data<decimal>(request, 3, 0)),
				HighPrice = Positive(Data<decimal>(request, 4, 0)),
				LowPrice = Positive(Data<decimal>(request, 5, 0)),
				BestAskPrice = Positive(Data<decimal>(request, 6, 0)),
				BestBidPrice = Positive(Data<decimal>(request, 7, 0)),
				TotalVolume = NonNegative(Data<decimal>(request, 8, 0)),
				Turnover = NonNegative(Data<decimal>(request, 9, 0)),
				BestAskVolume = NonNegative(Data<decimal>(request, 10, 0)),
				BestBidVolume = NonNegative(Data<decimal>(request, 11, 0)),
				OpenInterest = NonNegative(Data<decimal>(request, 12, 0)),
			};
		}
		finally
		{
			Release(request);
		}
	}

	public Task SubscribeAsync(DaishinSubscription subscription,
		CancellationToken cancellationToken)
		=> _dispatcher.InvokeAsync(() => Subscribe(subscription), cancellationToken);

	private void Subscribe(DaishinSubscription subscription)
	{
		ArgumentNullException.ThrowIfNull(subscription);
		EnsureConnected();
		if (_feeds.ContainsKey(subscription.NativeKey))
			return;
		if (_feeds.Count >= 400)
			throw new InvalidOperationException(
				"CYBOS Plus permits at most 400 simultaneous realtime subscriptions per PC.");

		var (programId, eventInterface) = GetFeedContract(subscription);
		var source = Create(programId);
		Action handler = () => OnFeed(subscription, source);
		try
		{
			SetInput(source, 0, subscription.Code);
			Combine(source, eventInterface, handler);
			Invoke(source, "Subscribe");
			_feeds.Add(subscription.NativeKey, new()
			{
				Key = subscription.NativeKey,
				Source = source,
				EventInterface = eventInterface,
				Handler = handler,
			});
		}
		catch
		{
			Remove(source, eventInterface, handler);
			Release(source);
			throw;
		}
	}

	public Task UnsubscribeAsync(string nativeKey, CancellationToken cancellationToken)
		=> _dispatcher.InvokeAsync(() => Unsubscribe(nativeKey), cancellationToken);

	private void Unsubscribe(string nativeKey)
	{
		if (!_feeds.Remove(nativeKey, out var feed))
			return;
		try
		{
			Invoke(feed.Source, "Unsubscribe");
		}
		catch (Exception error)
		{
			Error?.Invoke(error);
		}
		finally
		{
			Remove(feed.Source, feed.EventInterface, feed.Handler);
			Release(feed.Source);
		}
	}

	private (string programId, Guid eventInterface) GetFeedContract(DaishinSubscription subscription)
		=> (subscription.SecurityType, subscription.Kind, subscription.StockMarket) switch
		{
			(SecurityTypes.Stock or SecurityTypes.Etf, DaishinMarketDataKinds.Current,
				DaishinStockMarkets.Consolidated) => ("Dscbo1.StockBsccnsCnld", _dibEvents),
			(SecurityTypes.Stock or SecurityTypes.Etf, DaishinMarketDataKinds.Current,
				DaishinStockMarkets.Nxt) => ("Dscbo1.StockBsccnsNxt", _dibEvents),
			(SecurityTypes.Stock or SecurityTypes.Etf, DaishinMarketDataKinds.Current,
				DaishinStockMarkets.Krx) => ("Dscbo1.StockCur", _dibEvents),
			(SecurityTypes.Stock or SecurityTypes.Etf, DaishinMarketDataKinds.MarketDepth,
				DaishinStockMarkets.Consolidated) => ("Dscbo1.StockJpBidCnld", _dibEvents),
			(SecurityTypes.Stock or SecurityTypes.Etf, DaishinMarketDataKinds.MarketDepth,
				DaishinStockMarkets.Nxt) => ("Dscbo1.StockJpBidNxt", _dibEvents),
			(SecurityTypes.Stock or SecurityTypes.Etf, DaishinMarketDataKinds.MarketDepth,
				DaishinStockMarkets.Krx) => ("Dscbo1.StockJpBid", _dibEvents),
			(SecurityTypes.Future, DaishinMarketDataKinds.Current, _) =>
				("Dscbo1.FutureCurOnly", _dibEvents),
			(SecurityTypes.Future, DaishinMarketDataKinds.MarketDepth, _) =>
				("CpSysDib.FutureJpBid", _sysDibEvents),
			(SecurityTypes.Option, DaishinMarketDataKinds.Current, _) =>
				("CpSysDib.OptionCurOnly", _sysDibEvents),
			(SecurityTypes.Option, DaishinMarketDataKinds.MarketDepth, _) =>
				("CpSysDib.OptionJpBid", _sysDibEvents),
			_ => throw new NotSupportedException(
				$"CYBOS Plus realtime feed is not mapped for {subscription.SecurityType}/{subscription.Kind}."),
		};

	private void OnFeed(DaishinSubscription subscription, object source)
	{
		try
		{
			if (subscription.Kind == DaishinMarketDataKinds.MarketDepth)
				BookReceived?.Invoke(ReadBook(subscription, source));
			else
				Level1Received?.Invoke(ReadCurrent(subscription, source));
		}
		catch (Exception error)
		{
			Error?.Invoke(error);
		}
	}

	private DaishinLevel1Update ReadCurrent(DaishinSubscription subscription, object source)
	{
		var now = DateTime.UtcNow;
		decimal totalVolume;
		DaishinLevel1Update update;
		if (subscription.SecurityType is SecurityTypes.Stock or SecurityTypes.Etf)
		{
			totalVolume = Header<decimal>(source, 9);
			update = new()
			{
				Code = Header<string>(source, 0).IsEmpty(subscription.Code),
				SecurityType = subscription.SecurityType,
				ServerTime = Header<int>(source, 18).ParseKoreaTime(now),
				LastPrice = Positive(Header<decimal>(source, 13)),
				LastVolume = Positive(Header<decimal>(source, 17)),
				OpenPrice = Positive(Header<decimal>(source, 4)),
				HighPrice = Positive(Header<decimal>(source, 5)),
				LowPrice = Positive(Header<decimal>(source, 6)),
				BestAskPrice = Positive(Header<decimal>(source, 7)),
				BestBidPrice = Positive(Header<decimal>(source, 8)),
				TotalVolume = NonNegative(totalVolume),
				Turnover = NonNegative(Header<decimal>(source, 10)),
				OriginSide = ParseOriginSide(HeaderCode(source, 14)),
			};
		}
		else if (subscription.SecurityType == SecurityTypes.Future)
		{
			totalVolume = Header<decimal>(source, 13);
			update = new()
			{
				Code = Header<string>(source, 0).IsEmpty(subscription.Code),
				SecurityType = subscription.SecurityType,
				ServerTime = Header<int>(source, 15).ParseKoreaTime(now),
				LastPrice = Positive(Header<decimal>(source, 1)),
				LastVolume = GetVolumeDelta(subscription.NativeKey, totalVolume),
				OpenPrice = Positive(Header<decimal>(source, 7)),
				HighPrice = Positive(Header<decimal>(source, 8)),
				LowPrice = Positive(Header<decimal>(source, 9)),
				BestAskPrice = Positive(Header<decimal>(source, 18)),
				BestBidPrice = Positive(Header<decimal>(source, 19)),
				BestAskVolume = NonNegative(Header<decimal>(source, 20)),
				BestBidVolume = NonNegative(Header<decimal>(source, 21)),
				TotalVolume = NonNegative(totalVolume),
				OpenInterest = NonNegative(Header<decimal>(source, 14)),
				OriginSide = ParseOriginSide(HeaderCode(source, 24)),
			};
		}
		else
		{
			totalVolume = Header<decimal>(source, 7);
			update = new()
			{
				Code = Header<string>(source, 0).IsEmpty(subscription.Code),
				SecurityType = subscription.SecurityType,
				ServerTime = Header<int>(source, 1).ParseKoreaTime(now),
				LastPrice = Positive(Header<decimal>(source, 2)),
				LastVolume = GetVolumeDelta(subscription.NativeKey, totalVolume),
				OpenPrice = Positive(Header<decimal>(source, 4)),
				HighPrice = Positive(Header<decimal>(source, 5)),
				LowPrice = Positive(Header<decimal>(source, 6)),
				BestAskPrice = Positive(Header<decimal>(source, 17)),
				BestBidPrice = Positive(Header<decimal>(source, 18)),
				BestAskVolume = NonNegative(Header<decimal>(source, 19)),
				BestBidVolume = NonNegative(Header<decimal>(source, 20)),
				TotalVolume = NonNegative(totalVolume),
				Turnover = NonNegative(Header<decimal>(source, 8)),
				OpenInterest = NonNegative(Header<decimal>(source, 16)),
				OriginSide = ParseOriginSide(HeaderCode(source, 21)),
			};
		}
		return update;
	}

	private DaishinBookUpdate ReadBook(DaishinSubscription subscription, object source)
	{
		var bids = new List<DaishinBookLevel>();
		var asks = new List<DaishinBookLevel>();
		var depth = subscription.SecurityType is SecurityTypes.Stock or SecurityTypes.Etf ? 10 : 5;
		for (var index = 0; index < depth; index++)
		{
			int askPriceField;
			int bidPriceField;
			int askVolumeField;
			int bidVolumeField;
			if (subscription.SecurityType is SecurityTypes.Stock or SecurityTypes.Etf)
			{
				var offset = index < 5 ? 3 + index * 4 : 27 + (index - 5) * 4;
				askPriceField = offset;
				bidPriceField = offset + 1;
				askVolumeField = offset + 2;
				bidVolumeField = offset + 3;
			}
			else
			{
				askPriceField = 2 + index;
				askVolumeField = 7 + index;
				bidPriceField = 19 + index;
				bidVolumeField = 24 + index;
			}

			var askPrice = Header<decimal>(source, askPriceField);
			var bidPrice = Header<decimal>(source, bidPriceField);
			if (askPrice > 0)
				asks.Add(new() { Price = askPrice, Volume = Math.Max(0, Header<decimal>(source, askVolumeField)) });
			if (bidPrice > 0)
				bids.Add(new() { Price = bidPrice, Volume = Math.Max(0, Header<decimal>(source, bidVolumeField)) });
		}

		return new()
		{
			Code = Header<string>(source, 0).IsEmpty(subscription.Code),
			SecurityType = subscription.SecurityType,
			ServerTime = Header<int>(source, 1).ParseKoreaTime(DateTime.UtcNow),
			Bids = bids,
			Asks = asks,
		};
	}

	public Task<IReadOnlyList<DaishinCandle>> GetCandlesAsync(DaishinSecurityInfo security,
		TimeSpan timeFrame, int count, CancellationToken cancellationToken)
		=> _dispatcher.InvokeAsync<IReadOnlyList<DaishinCandle>>(() =>
			GetCandles(security, timeFrame, count, cancellationToken), cancellationToken);

	private IReadOnlyList<DaishinCandle> GetCandles(DaishinSecurityInfo security,
		TimeSpan timeFrame, int count, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(security);
		var request = Create(security.SecurityType is SecurityTypes.Stock or SecurityTypes.Etf
			? "CpSysDib.StockChart"
			: "CpSysDib.FutOptChart");
		try
		{
			var (period, interval) = GetChartPeriod(timeFrame);
			SetInput(request, 0, security.Code);
			SetInput(request, 1, (int)'2');
			SetInput(request, 4, Math.Clamp(count, 1, 2000));
			SetInput(request, 5, new[] { 0, 1, 2, 3, 4, 5, 8, 9 });
			SetInput(request, 6, (int)period);
			SetInput(request, 7, interval);
			SetInput(request, 8, (int)'0');
			if (security.SecurityType is SecurityTypes.Stock or SecurityTypes.Etf)
			{
				SetInput(request, 9, (int)'1');
				SetInput(request, 12, (int)_stockMarket.ToNativeMarket());
			}

			Request(request, 1, cancellationToken);
			var received = Header<int>(request, 3);
			var candles = new List<DaishinCandle>(received);
			for (var index = 0; index < received; index++)
			{
				var date = Data<int>(request, 0, index);
				if (date <= 0)
					continue;
				candles.Add(new()
				{
					OpenTime = date.ParseKoreaDateTime(Data<int>(request, 1, index)),
					Open = Data<decimal>(request, 2, index),
					High = Data<decimal>(request, 3, index),
					Low = Data<decimal>(request, 4, index),
					Close = Data<decimal>(request, 5, index),
					Volume = Math.Max(0, Data<decimal>(request, 6, index)),
					Turnover = NonNegative(Data<decimal>(request, 7, index)),
				});
			}
			return [.. candles.OrderBy(item => item.OpenTime)];
		}
		finally
		{
			Release(request);
		}
	}

	public Task<DaishinOrderResponse> PlaceOrderAsync(DaishinOrderRequest order,
		CancellationToken cancellationToken)
		=> _dispatcher.InvokeAsync(() => PlaceOrder(order, cancellationToken), cancellationToken);

	private DaishinOrderResponse PlaceOrder(DaishinOrderRequest order,
		CancellationToken cancellationToken)
	{
		EnsureTrading();
		var request = Create(order.SecurityType is SecurityTypes.Stock or SecurityTypes.Etf
			? "CpTrade.CpTd0311"
			: "CpTrade.CpTd6831");
		try
		{
			if (order.SecurityType is SecurityTypes.Stock or SecurityTypes.Etf)
			{
				SetInput(request, 0, order.Side == Sides.Sell ? "1" : "2");
				SetInput(request, 1, order.Account);
				SetInput(request, 2, order.Product);
				SetInput(request, 3, order.Code);
				SetInput(request, 4, order.Volume);
				SetInput(request, 5, order.OrderType == OrderTypes.Market ? 0 : decimal.ToInt64(order.Price));
				SetInput(request, 7, order.TimeInForce.ToNativeTimeInForce());
				SetInput(request, 8, order.OrderType == OrderTypes.Market ? "03" : "01");
				SetInput(request, 10, order.StockOrderMarket.ToString(CultureInfo.InvariantCulture));
			}
			else
			{
				SetInput(request, 1, order.Account);
				SetInput(request, 2, order.Code);
				SetInput(request, 3, order.Volume);
				SetInput(request, 4, order.OrderType == OrderTypes.Market ? 0d : (double)order.Price);
				SetInput(request, 5, order.Side == Sides.Sell ? "1" : "2");
				SetInput(request, 6, order.OrderType == OrderTypes.Market ? "2" : "1");
				SetInput(request, 7, order.TimeInForce.ToNativeTimeInForce());
				SetInput(request, 8, order.Product);
			}

			Request(request, 0, cancellationToken);
			return new()
			{
				OrderId = Header<long>(request, 8).ToString(CultureInfo.InvariantCulture),
				ServerTime = DateTime.UtcNow,
				Message = GetMessage(request),
			};
		}
		finally
		{
			Release(request);
		}
	}

	public Task<DaishinOrderResponse> ReplaceOrderAsync(string originalOrderId,
		DaishinOrderRequest order, CancellationToken cancellationToken)
		=> _dispatcher.InvokeAsync(() => ReplaceOrder(originalOrderId, order, cancellationToken),
			cancellationToken);

	private DaishinOrderResponse ReplaceOrder(string originalOrderId, DaishinOrderRequest order,
		CancellationToken cancellationToken)
	{
		EnsureTrading();
		var nativeOrderId = long.Parse(originalOrderId, CultureInfo.InvariantCulture);
		var request = Create(order.SecurityType is SecurityTypes.Stock or SecurityTypes.Etf
			? "CpTrade.CpTd0313"
			: "CpTrade.CpTd6832");
		try
		{
			if (order.SecurityType is SecurityTypes.Stock or SecurityTypes.Etf)
			{
				SetInput(request, 1, nativeOrderId);
				SetInput(request, 2, order.Account);
				SetInput(request, 3, order.Product);
				SetInput(request, 4, order.Code);
				SetInput(request, 5, order.Volume);
				SetInput(request, 6, decimal.ToInt64(order.Price));
				SetInput(request, 7, order.StockOrderMarket.ToString(CultureInfo.InvariantCulture));
			}
			else
			{
				SetInput(request, 2, nativeOrderId);
				SetInput(request, 3, order.Account);
				SetInput(request, 4, order.Code);
				SetInput(request, 5, order.Volume);
				SetInput(request, 6, order.OrderType == OrderTypes.Market ? 0d : (double)order.Price);
				SetInput(request, 8, order.OrderType == OrderTypes.Market ? "2" : "1");
				SetInput(request, 9, order.TimeInForce.ToNativeTimeInForce());
				SetInput(request, 10, order.Product);
			}

			Request(request, 0, cancellationToken);
			var header = order.SecurityType is SecurityTypes.Stock or SecurityTypes.Etf ? 7 : 6;
			return new()
			{
				OrderId = Header<long>(request, header).ToString(CultureInfo.InvariantCulture),
				ServerTime = DateTime.UtcNow,
				Message = GetMessage(request),
			};
		}
		finally
		{
			Release(request);
		}
	}

	public Task<DaishinOrderResponse> CancelOrderAsync(string originalOrderId,
		DaishinOrderRequest order, CancellationToken cancellationToken)
		=> _dispatcher.InvokeAsync(() => CancelOrder(originalOrderId, order, cancellationToken),
			cancellationToken);

	private DaishinOrderResponse CancelOrder(string originalOrderId, DaishinOrderRequest order,
		CancellationToken cancellationToken)
	{
		EnsureTrading();
		var nativeOrderId = long.Parse(originalOrderId, CultureInfo.InvariantCulture);
		var request = Create(order.SecurityType is SecurityTypes.Stock or SecurityTypes.Etf
			? "CpTrade.CpTd0314"
			: "CpTrade.CpTd6833");
		try
		{
			if (order.SecurityType is SecurityTypes.Stock or SecurityTypes.Etf)
			{
				SetInput(request, 1, nativeOrderId);
				SetInput(request, 2, order.Account);
				SetInput(request, 3, order.Product);
				SetInput(request, 4, order.Code);
				SetInput(request, 5, 0);
			}
			else
			{
				SetInput(request, 2, nativeOrderId);
				SetInput(request, 3, order.Account);
				SetInput(request, 4, order.Code);
				SetInput(request, 5, order.Volume);
				SetInput(request, 6, order.Product);
			}

			Request(request, 0, cancellationToken);
			var header = order.SecurityType is SecurityTypes.Stock or SecurityTypes.Etf ? 6 : 5;
			return new()
			{
				OrderId = Header<long>(request, header).ToString(CultureInfo.InvariantCulture),
				ServerTime = DateTime.UtcNow,
				Message = GetMessage(request),
			};
		}
		finally
		{
			Release(request);
		}
	}

	public Task<IReadOnlyList<DaishinOrderUpdate>> GetOpenOrdersAsync(string account,
		CancellationToken cancellationToken)
		=> _dispatcher.InvokeAsync<IReadOnlyList<DaishinOrderUpdate>>(() =>
			GetOpenOrders(account, cancellationToken), cancellationToken);

	private IReadOnlyList<DaishinOrderUpdate> GetOpenOrders(string account,
		CancellationToken cancellationToken)
	{
		EnsureTrading();
		var accountInfo = ResolveAccount(account);
		var result = new List<DaishinOrderUpdate>();
		if (accountInfo.IsStockEnabled)
			ReadStockOpenOrders(accountInfo, result, cancellationToken);
		if (accountInfo.IsDerivativesEnabled)
			ReadDerivativesOpenOrders(accountInfo, result, cancellationToken);
		return result;
	}

	private void ReadStockOpenOrders(DaishinAccountInfo account,
		ICollection<DaishinOrderUpdate> result, CancellationToken cancellationToken)
	{
		var request = Create("CpTrade.CpTd5339");
		try
		{
			SetInput(request, 0, account.Account);
			SetInput(request, 1, account.StockProduct);
			SetInput(request, 4, "0");
			SetInput(request, 5, "0");
			SetInput(request, 6, "1");
			SetInput(request, 7, 20);
			SetInput(request, 8, "0");
			do
			{
				Request(request, 0, cancellationToken);
				var count = Header<int>(request, 5);
				for (var index = 0; index < count; index++)
				{
					var balance = Data<decimal>(request, 11, index);
					if (balance <= 0)
						continue;
					var quoteType = DataCode(request, 21, index);
					result.Add(new()
					{
						OrderId = Data<long>(request, 1, index).ToString(CultureInfo.InvariantCulture),
						OriginalOrderId = Data<long>(request, 2, index).ToString(CultureInfo.InvariantCulture),
						Account = account.Account,
						Product = account.StockProduct,
						Code = Data<string>(request, 3, index),
						SecurityType = SecurityTypes.Stock,
						Side = DataCode(request, 13, index) == "1" ? Sides.Sell : Sides.Buy,
						OrderType = quoteType is "03" or "3" ? OrderTypes.Market : OrderTypes.Limit,
						TimeInForce = TimeInForce.PutInQueue,
						Event = DaishinOrderEvents.Accepted,
						Price = Data<decimal>(request, 7, index),
						Volume = Data<decimal>(request, 6, index),
						Balance = balance,
						ServerTime = DateTime.UtcNow,
					});
				}
			}
			while (Get<bool>(request, "Continue"));
		}
		finally
		{
			Release(request);
		}
	}

	private void ReadDerivativesOpenOrders(DaishinAccountInfo account,
		ICollection<DaishinOrderUpdate> result, CancellationToken cancellationToken)
	{
		var request = Create("CpTrade.CpTd5371");
		try
		{
			SetInput(request, 0, account.Account);
			SetInput(request, 1, account.DerivativesProduct);
			SetInput(request, 4, "1");
			SetInput(request, 6, "3");
			SetInput(request, 7, 20);
			do
			{
				Request(request, 0, cancellationToken);
				var count = Header<int>(request, 6);
				for (var index = 0; index < count; index++)
				{
					var balance = Data<decimal>(request, 9, index);
					if (balance <= 0)
						continue;
					var code = Data<string>(request, 4, index);
					var orderType = DataCode(request, 13, index);
					var volume = Data<decimal>(request, 16, index);
					result.Add(new()
					{
						OrderId = Data<long>(request, 2, index).ToString(CultureInfo.InvariantCulture),
						OriginalOrderId = Data<long>(request, 3, index).ToString(CultureInfo.InvariantCulture),
						Account = account.Account,
						Product = account.DerivativesProduct,
						Code = code,
						SecurityType = InferDerivativeType(code),
						Side = Data<string>(request, 6, index).Contains("매도", StringComparison.Ordinal)
							? Sides.Sell : Sides.Buy,
						OrderType = orderType == "2" ? OrderTypes.Market : OrderTypes.Limit,
						TimeInForce = TimeInForce.PutInQueue,
						Event = DaishinOrderEvents.Accepted,
						Price = Data<decimal>(request, 8, index),
						Volume = volume > 0 ? volume : balance,
						Balance = balance,
						ServerTime = DateTime.UtcNow,
					});
				}
			}
			while (Get<bool>(request, "Continue"));
		}
		finally
		{
			Release(request);
		}
	}

	public Task<DaishinPortfolioSnapshot> GetPortfolioAsync(string account,
		CancellationToken cancellationToken)
		=> _dispatcher.InvokeAsync(() => GetPortfolio(account, cancellationToken), cancellationToken);

	private DaishinPortfolioSnapshot GetPortfolio(string account,
		CancellationToken cancellationToken)
	{
		EnsureTrading();
		var accountInfo = ResolveAccount(account);
		var positions = new List<DaishinPositionInfo>();
		decimal? currentValue = null;
		decimal? blockedValue = null;
		decimal? unrealized = null;
		if (accountInfo.IsStockEnabled)
		{
			var request = Create("CpTrade.CpTd6033");
			try
			{
				SetInput(request, 0, accountInfo.Account);
				SetInput(request, 1, accountInfo.StockProduct);
				SetInput(request, 2, 50);
				SetInput(request, 3, "2");
				SetInput(request, 4, _stockMarket == DaishinStockMarkets.Nxt ? "2" : "1");
				do
				{
					Request(request, 0, cancellationToken);
					currentValue ??= Header<decimal>(request, 9) + Header<decimal>(request, 3);
					blockedValue ??= Header<decimal>(request, 6);
					unrealized ??= Header<decimal>(request, 4);
					var count = Header<int>(request, 7);
					for (var index = 0; index < count; index++)
					{
						var code = Data<string>(request, 12, index);
						if (code.IsEmpty())
							continue;
						positions.Add(new()
						{
							Account = accountInfo.Account,
							Code = code,
							SecurityType = SecurityTypes.Stock,
							CurrentValue = Data<decimal>(request, 7, index),
							AvailableValue = Data<decimal>(request, 15, index),
							AveragePrice = Positive(Data<decimal>(request, 17, index)),
						});
					}
				}
				while (Get<bool>(request, "Continue"));
			}
			finally
			{
				Release(request);
			}
		}

		if (accountInfo.IsDerivativesEnabled)
		{
			var request = Create("CpTrade.CpTd0723");
			try
			{
				SetInput(request, 0, accountInfo.Account);
				SetInput(request, 1, accountInfo.DerivativesProduct);
				SetInput(request, 4, 50);
				do
				{
					Request(request, 0, cancellationToken);
					var count = Header<int>(request, 2);
					for (var index = 0; index < count; index++)
					{
						var code = Data<string>(request, 0, index);
						var value = Data<decimal>(request, 3, index);
						if (DataCode(request, 2, index) == "1")
							value = -value;
						positions.Add(new()
						{
							Account = accountInfo.Account,
							Code = code,
							SecurityType = InferDerivativeType(code),
							CurrentValue = value,
							AvailableValue = Data<decimal>(request, 9, index),
							AveragePrice = Positive(Data<decimal>(request, 5, index)),
						});
					}
				}
				while (Get<bool>(request, "Continue"));
			}
			finally
			{
				Release(request);
			}
		}

		return new()
		{
			Portfolio = new()
			{
				Account = accountInfo.Account,
				CurrentValue = currentValue,
				BlockedValue = blockedValue,
				UnrealizedPnL = unrealized,
			},
			Positions = positions,
		};
	}

	private IReadOnlyList<DaishinAccountInfo> ReadAccounts()
	{
		var accounts = new List<DaishinAccountInfo>();
		foreach (var value in Items(Get(_tradeUtility, "AccountNumber")))
		{
			var account = value?.ToString();
			if (account.IsEmpty())
				continue;
			var stockProduct = Items(Invoke(_tradeUtility, "GoodsList", account, 1))
				.Select(item => item?.ToString()).FirstOrDefault(item => !item.IsEmpty());
			var derivativesProduct = Items(Invoke(_tradeUtility, "GoodsList", account, 2))
				.Select(item => item?.ToString()).FirstOrDefault(item => !item.IsEmpty());
			if (!stockProduct.IsEmpty() || !derivativesProduct.IsEmpty())
				accounts.Add(new()
				{
					Account = account,
					StockProduct = stockProduct,
					DerivativesProduct = derivativesProduct,
				});
		}
		return accounts;
	}

	private void LoadSecurities(ISet<SecurityTypes> requestedTypes)
	{
		var wantsStocks = requestedTypes.Count == 0 || requestedTypes.Contains(SecurityTypes.Stock) ||
			requestedTypes.Contains(SecurityTypes.Etf);
		if (wantsStocks && !_securities.Values.Any(item =>
			item.SecurityType is SecurityTypes.Stock or SecurityTypes.Etf))
		{
			foreach (var market in new[] { 1, 2 })
			{
				foreach (var item in Items(Invoke(_codeManager, "GetStockListByMarket", market)))
				{
					var code = item?.ToString();
					if (code.IsEmpty() || _securities.ContainsKey(code))
						continue;
					var name = Invoke(_codeManager, "CodeToName", code)?.ToString();
					var section = ConvertInt(Invoke(_codeManager, "GetStockSectionKind", code));
					_securities[code] = new()
					{
						Code = code,
						Name = name,
						Board = "KRX",
						SecurityType = section == 10 || name?.Contains("ETF", StringComparison.OrdinalIgnoreCase) == true
							? SecurityTypes.Etf : SecurityTypes.Stock,
						PriceStep = Positive(ConvertDecimal(Invoke(_codeManager, "GetTickUnit", code))),
					};
				}
			}
		}

		if ((requestedTypes.Count == 0 || requestedTypes.Contains(SecurityTypes.Future)) &&
			!_securities.Values.Any(item => item.SecurityType == SecurityTypes.Future))
			LoadDerivatives(_futureCodes, SecurityTypes.Future);
		if ((requestedTypes.Count == 0 || requestedTypes.Contains(SecurityTypes.Option)) &&
			!_securities.Values.Any(item => item.SecurityType == SecurityTypes.Option))
			LoadDerivatives(_optionCodes, SecurityTypes.Option);
	}

	private void LoadDerivatives(object manager, SecurityTypes securityType)
	{
		var count = ConvertInt(Invoke(manager, "GetCount"));
		for (var index = 0; index < count; index++)
		{
			var code = Invoke(manager, "GetData", (short)0, (short)index)?.ToString();
			if (code.IsEmpty() || _securities.ContainsKey(code))
				continue;
			var name = Invoke(manager, "CodeToName", code)?.ToString();
			_securities[code] = new()
			{
				Code = code,
				Name = name,
				Board = "KRX",
				SecurityType = securityType,
				OptionType = securityType == SecurityTypes.Option
					? code.StartsWith("2", StringComparison.Ordinal) ? OptionTypes.Call : OptionTypes.Put
					: null,
				Strike = securityType == SecurityTypes.Option
					? Positive(ConvertDecimal(Invoke(manager, "GetData", (short)4, (short)index)))
					: null,
				PriceStep = Positive(ConvertDecimal(Invoke(_codeManager, "GetTickUnit", code))),
			};
		}
	}

	private void OnStockConclusion()
	{
		try
		{
			var eventCode = HeaderCode(_stockConclusion, 14);
			var correction = HeaderCode(_stockConclusion, 16);
			var orderId = Header<long>(_stockConclusion, 5).ToString(CultureInfo.InvariantCulture);
			var volume = Header<decimal>(_stockConclusion, 3);
			var price = Header<decimal>(_stockConclusion, 4);
			var orderEvent = eventCode switch
			{
				"1" => DaishinOrderEvents.Filled,
				"3" => DaishinOrderEvents.Rejected,
				_ when correction == "2" => DaishinOrderEvents.Replaced,
				_ when correction == "3" => DaishinOrderEvents.Canceled,
				_ => DaishinOrderEvents.Accepted,
			};
			OrderReceived?.Invoke(new()
			{
				OrderId = orderId,
				OriginalOrderId = Header<long>(_stockConclusion, 6).ToString(CultureInfo.InvariantCulture),
				Account = Header<string>(_stockConclusion, 7),
				Product = Header<string>(_stockConclusion, 8),
				Code = Header<string>(_stockConclusion, 9),
				SecurityType = SecurityTypes.Stock,
				Side = HeaderCode(_stockConclusion, 12) == "1" ? Sides.Sell : Sides.Buy,
				OrderType = HeaderCode(_stockConclusion, 18) is "03" or "3"
					? OrderTypes.Market : OrderTypes.Limit,
				TimeInForce = HeaderCode(_stockConclusion, 19).ToTimeInForce(),
				Event = orderEvent,
				Price = price,
				Volume = volume,
				TradePrice = orderEvent == DaishinOrderEvents.Filled ? price : null,
				TradeVolume = orderEvent == DaishinOrderEvents.Filled ? volume : null,
				TradeId = orderEvent == DaishinOrderEvents.Filled
					? $"{orderId}:{DateTime.UtcNow.Ticks}:{price}:{volume}" : null,
				ServerTime = DateTime.UtcNow,
				Error = orderEvent == DaishinOrderEvents.Rejected
					? "Daishin CYBOS Plus rejected the stock order." : null,
			});
		}
		catch (Exception error)
		{
			Error?.Invoke(error);
		}
	}

	private void OnFuturesConclusion()
	{
		try
		{
			var orderId = Header<long>(_futuresConclusion, 5).ToString(CultureInfo.InvariantCulture);
			var volume = Header<decimal>(_futuresConclusion, 3);
			var price = Header<decimal>(_futuresConclusion, 4);
			var code = Header<string>(_futuresConclusion, 9);
			var orderEvent = HeaderCode(_futuresConclusion, 44) switch
			{
				"2" => DaishinOrderEvents.Replaced,
				"3" => DaishinOrderEvents.Canceled,
				"4" => DaishinOrderEvents.Filled,
				"5" => DaishinOrderEvents.Rejected,
				_ => DaishinOrderEvents.Accepted,
			};
			var orderTypeCode = HeaderCode(_futuresConclusion,
				orderEvent == DaishinOrderEvents.Replaced ? 21 : 20);
			OrderReceived?.Invoke(new()
			{
				OrderId = orderId,
				OriginalOrderId = Header<long>(_futuresConclusion, 6).ToString(CultureInfo.InvariantCulture),
				Account = Header<string>(_futuresConclusion, 7),
				Product = Header<string>(_futuresConclusion, 8),
				Code = code,
				SecurityType = InferDerivativeType(code),
				Side = HeaderCode(_futuresConclusion, 12) == "1" ? Sides.Sell : Sides.Buy,
				OrderType = orderTypeCode == "2"
					? OrderTypes.Market : OrderTypes.Limit,
				TimeInForce = HeaderCode(_futuresConclusion, 43).ToTimeInForce(),
				Event = orderEvent,
				Price = price,
				Volume = volume,
				TradePrice = orderEvent == DaishinOrderEvents.Filled ? price : null,
				TradeVolume = orderEvent == DaishinOrderEvents.Filled ? volume : null,
				TradeId = orderEvent == DaishinOrderEvents.Filled
					? $"{orderId}:{DateTime.UtcNow.Ticks}:{price}:{volume}" : null,
				ServerTime = DateTime.UtcNow,
				Error = orderEvent == DaishinOrderEvents.Rejected
					? "Daishin CYBOS Plus rejected the derivatives order." : null,
			});
		}
		catch (Exception error)
		{
			Error?.Invoke(error);
		}
	}

	private void OnDisconnected()
		=> ConnectionLost?.Invoke(new InvalidOperationException(
			"CYBOS Plus reported that the vendor session was disconnected."));

	private DaishinAccountInfo ResolveAccount(string account)
	{
		account = account.IsEmpty(_requestedAccount);
		var result = account.IsEmpty()
			? _accounts.FirstOrDefault()
			: _accounts.FirstOrDefault(item => item.Account.EqualsIgnoreCase(account));
		return result ?? throw new InvalidOperationException(
			account.IsEmpty() ? LocalizedStrings.AccountNotFound : $"CYBOS Plus account '{account}' is not available.");
	}

	private void Request(object request, int limitType, CancellationToken cancellationToken)
	{
		while (true)
		{
			WaitForLimit(limitType, cancellationToken);
			var result = ConvertInt(Invoke(request, "BlockRequest"));
			if (result == 4)
				continue;
			if (result != 0)
				throw new InvalidOperationException(
					$"CYBOS Plus BlockRequest failed ({result}): {GetMessage(request)}");
			var status = ConvertInt(Invoke(request, "GetDibStatus"));
			if (status != 0)
				throw new InvalidOperationException(
					$"CYBOS Plus request failed ({status}): {GetMessage(request)}");
			return;
		}
	}

	private void WaitForLimit(int limitType, CancellationToken cancellationToken)
	{
		while (ConvertInt(Invoke(_cybos, "GetLimitRemainCount", limitType)) <= 0)
		{
			var milliseconds = Math.Max(50, Get<int>(_cybos, "LimitRequestRemainTime") + 20);
			var until = Environment.TickCount64 + milliseconds;
			while (Environment.TickCount64 < until)
			{
				cancellationToken.ThrowIfCancellationRequested();
				Thread.Sleep((int)Math.Min(100, until - Environment.TickCount64));
			}
		}
	}

	private decimal? GetVolumeDelta(string key, decimal totalVolume)
	{
		if (!_lastVolumes.TryGetValue(key, out var previous))
		{
			_lastVolumes[key] = totalVolume;
			return null;
		}
		_lastVolumes[key] = totalVolume;
		var delta = totalVolume - previous;
		return delta > 0 ? delta : null;
	}

	private static (char period, int interval) GetChartPeriod(TimeSpan timeFrame)
	{
		if (timeFrame < TimeSpan.FromDays(1) && timeFrame.TotalMinutes >= 1 &&
			timeFrame.TotalMinutes == Math.Truncate(timeFrame.TotalMinutes))
			return ('m', checked((int)timeFrame.TotalMinutes));
		if (timeFrame == TimeSpan.FromDays(1))
			return ('D', 1);
		if (timeFrame == TimeSpan.FromDays(7))
			return ('W', 1);
		if (timeFrame == TimeSpan.FromDays(30))
			return ('M', 1);
		throw new NotSupportedException($"CYBOS Plus does not support the {timeFrame} chart period.");
	}

	private SecurityTypes InferDerivativeType(string code)
	{
		if (!code.IsEmpty() && _securities.TryGetValue(code, out var security))
			return security.SecurityType;
		return code?.StartsWith("2", StringComparison.Ordinal) == true ||
			code?.StartsWith("3", StringComparison.Ordinal) == true
			? SecurityTypes.Option : SecurityTypes.Future;
	}

	private static Sides? ParseOriginSide(string value)
		=> value?.Trim() switch
		{
			"1" => Sides.Buy,
			"2" => Sides.Sell,
			_ => null,
		};

	private static decimal? Positive(decimal value) => value > 0 ? value : null;
	private static decimal? NonNegative(decimal value) => value >= 0 ? value : null;

	private static object Create(string programId)
	{
		var type = Type.GetTypeFromProgID(programId, false)
			?? throw new InvalidOperationException(
				$"CYBOS Plus COM object '{programId}' is not registered. Install the current 32-bit CYBOS Plus or CREON Plus package.");
		return Activator.CreateInstance(type)
			?? throw new InvalidOperationException($"Cannot create CYBOS Plus COM object '{programId}'.");
	}

	private static object Invoke(object target, string name, params object[] args)
	{
		try
		{
			return target.GetType().InvokeMember(name,
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.InvokeMethod,
				null, target, args, CultureInfo.InvariantCulture);
		}
		catch (TargetInvocationException error)
		{
			throw error.InnerException ?? error;
		}
	}

	private static object Get(object target, string name)
	{
		try
		{
			return target.GetType().InvokeMember(name,
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.GetProperty,
				null, target, null, CultureInfo.InvariantCulture);
		}
		catch (TargetInvocationException error)
		{
			throw error.InnerException ?? error;
		}
	}

	private static T Get<T>(object target, string name)
		=> ConvertValue<T>(Get(target, name));

	private static void SetInput(object target, int index, object value)
		=> Invoke(target, "SetInputValue", index, value);

	private static T Header<T>(object target, int index)
		=> ConvertValue<T>(Invoke(target, "GetHeaderValue", index));

	private static string HeaderCode(object target, int index)
		=> ToCodeText(Invoke(target, "GetHeaderValue", index));

	private static T Data<T>(object target, int type, int index)
		=> ConvertValue<T>(Invoke(target, "GetDataValue", type, index));

	private static string DataCode(object target, int type, int index)
		=> ToCodeText(Invoke(target, "GetDataValue", type, index));

	private static string ToCodeText(object value)
	{
		if (value is char character)
			return character.ToString();
		if (value is byte or sbyte or short or ushort or int or uint or long or ulong)
		{
			var number = Convert.ToInt32(value, CultureInfo.InvariantCulture);
			if (number is >= 32 and <= 126)
				return ((char)number).ToString();
		}
		return value?.ToString()?.Trim();
	}

	private static T ConvertValue<T>(object value)
	{
		if (value == null || value is DBNull)
			return default;
		var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
		if (targetType == typeof(string))
			return (T)(object)value.ToString();
		if (targetType == typeof(bool))
			return (T)(object)(ConvertInt(value) != 0);
		return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
	}

	private static int ConvertInt(object value)
		=> value == null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);

	private static decimal ConvertDecimal(object value)
		=> value == null ? 0 : Convert.ToDecimal(value, CultureInfo.InvariantCulture);

	private static object[] Items(object value)
		=> value is IEnumerable items ? [.. items.Cast<object>()] : value == null ? [] : [value];

	private static string GetMessage(object request)
		=> Invoke(request, "GetDibMsg1")?.ToString();

	private static void Combine(object source, Guid eventInterface, Action handler)
		=> ComEventsHelper.Combine(source, eventInterface, 1, handler);

	private static void Remove(object source, Guid eventInterface, Action handler)
	{
		if (source == null || handler == null)
			return;
		try
		{
			ComEventsHelper.Remove(source, eventInterface, 1, handler);
		}
		catch
		{
		}
	}

	private static void Release(object value)
	{
		if (value == null || !Marshal.IsComObject(value))
			return;
		try
		{
			_ = Marshal.FinalReleaseComObject(value);
		}
		catch
		{
		}
	}

	private void EnsureConnected()
	{
		if (_cybos == null || Get<int>(_cybos, "IsConnect") == 0)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureTrading()
	{
		EnsureConnected();
		if (!_isTradingEnabled || _tradeUtility == null)
			throw new InvalidOperationException("Daishin CYBOS Plus trading services are disabled.");
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
			return;
		try
		{
			_dispatcher.InvokeAsync(Disconnect, CancellationToken.None).GetAwaiter().GetResult();
		}
		catch
		{
		}
		_dispatcher.Dispose();
	}

	private void Disconnect()
	{
		foreach (var key in _feeds.Keys.ToArray())
			Unsubscribe(key);
		_lastVolumes.Clear();
		_securities.Clear();

		if (_stockConclusion != null)
		{
			try { Invoke(_stockConclusion, "Unsubscribe"); } catch { }
			Remove(_stockConclusion, _dibEvents, _stockConclusionHandler);
		}
		if (_futuresConclusion != null)
		{
			try { Invoke(_futuresConclusion, "Unsubscribe"); } catch { }
			Remove(_futuresConclusion, _dibEvents, _futuresConclusionHandler);
		}
		Remove(_cybos, _cybosEvents, _disconnectHandler);

		Release(_stockConclusion);
		Release(_futuresConclusion);
		Release(_tradeUtility);
		Release(_optionCodes);
		Release(_futureCodes);
		Release(_codeManager);
		Release(_cybos);
		_stockConclusion = null;
		_futuresConclusion = null;
		_tradeUtility = null;
		_optionCodes = null;
		_futureCodes = null;
		_codeManager = null;
		_cybos = null;
		_disconnectHandler = null;
		_stockConclusionHandler = null;
		_futuresConclusionHandler = null;
		_accounts = [];
		Version = null;
	}
}
