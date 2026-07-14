namespace StockSharp.Binance.Native;

using System.Security.Cryptography;

class HttpClient : BaseLogReceiver
{
	private readonly SecureString _key;
	private readonly HashAlgorithm _hasher;
	private readonly bool _isDemo;

	private TimeSpan _serverTimeDiff;

	public HttpClient(SecureString key, SecureString secret, bool isDemo)
	{
		_key = key;
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().ASCII());
		_isDemo = isDemo;
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Binance) + "_" + nameof(HttpClient);

	public async Task RefreshTimeDiff(CancellationToken cancellationToken)
	{
		async Task<(TimeSpan diff, TimeSpan delay)> GetDiff()
		{
			var before = DateTime.UtcNow;
			var serverTime = await GetServerTime(cancellationToken);
			var after = DateTime.UtcNow;

			var receiveTimeAsLocal = before + TimeSpan.FromTicks((after - before).Ticks / 2);
			var diff = TimeSpan.FromMilliseconds((serverTime - receiveTimeAsLocal).TotalMilliseconds);

			return (diff, after - before);
		}

		this.AddInfoLog("synchronizing time...");

		var t1 = await GetDiff();
		var t2 = await GetDiff();

		if(t1.delay > t2.delay)
			t1 = t2;

		_serverTimeDiff = t1.diff;

		this.AddInfoLog($"server time diff: {_serverTimeDiff.TotalMilliseconds:0.###}ms, delay={t1.delay.TotalMilliseconds:0.###}ms");
	}

	public async Task<DateTime> GetServerTime(CancellationToken cancellationToken)
	{
		dynamic res = await MakeRequest<object>(CreateUrl(BinanceSections.Futures, "time"), CreateRequest(Method.Get), cancellationToken);

		return ((long)res.serverTime).FromUnix(false);
	}

	public async Task<IEnumerable<Symbol>> GetSymbols(BinanceSections section, CancellationToken cancellationToken)
	{
		dynamic res = await MakeRequest<object>(CreateUrl(section, "exchangeInfo"), CreateRequest(Method.Get), cancellationToken);

		return ((JToken)res.symbols).DeserializeObject<IEnumerable<Symbol>>();
	}

	private static readonly TimeSpan _maxFirstTradeLookupPeriod = TimeSpan.FromDays(2);

	public async Task<long> GetFirstTradeIdFromTime(BinanceSections section, string symbol, DateTime fromTime, CancellationToken cancellationToken)
	{
		var step = TimeSpan.FromHours(1);

		for (var shift = TimeSpan.Zero; shift < _maxFirstTradeLookupPeriod; shift += step)
		{
			var request = CreateRequest(Method.Get);

			var start = (fromTime + shift).ToUnix(false).To<long>();
			var end = (fromTime + shift + step).ToUnix(false).To<long>();

			request
				.AddParameter("symbol", symbol)
				.AddParameter("startTime", start)
				.AddParameter("endTime", end)
				.AddParameter("limit", 1);

			dynamic response = await MakeRequest<object>(CreateUrl(section, "aggTrades"), request, cancellationToken);

			if ((int)response.Count > 0)
				return (long)response[0].f;

			this.AddWarningLog("server returned no trades for '{0}' one hour from {1}", symbol, fromTime + shift);
		}

		return 0;
	}

	public Task<IEnumerable<HttpTrade>> GetHistoricalTrades(BinanceSections section, string symbol, long? fromId, int? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request.AddParameter("symbol", symbol);

		if (fromId != null)
			request.AddParameter("fromId", fromId.Value);

		if (limit != null)
			request.AddParameter("limit", limit.Value);

		ApplyKey(request);

		return MakeRequest<IEnumerable<HttpTrade>>(CreateUrl(section, "historicalTrades"), request, cancellationToken);
	}

	public Task<IEnumerable<HttpTrade>> GetTrades(BinanceSections section, string symbol, int? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request.AddParameter("symbol", symbol);

		if (limit != null)
			request.AddParameter("limit", limit.Value);

		return MakeRequest<IEnumerable<HttpTrade>>(CreateUrl(section, "trades"), request, cancellationToken);
	}

	public Task<IEnumerable<HttpOhlc>> GetCandles(BinanceSections section, string symbol, string interval, long? start, long? end, long? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("interval", interval);

		if (start != null)
			request.AddParameter("startTime", start.Value);

		if (end != null)
			request.AddParameter("endTime", end.Value);

		if (limit != null)
			request.AddParameter("limit", limit.Value);

		return MakeRequest<IEnumerable<HttpOhlc>>(CreateUrl(section, "klines"), request, cancellationToken);
	}

	public Task<HttpOrderBook> GetDepth(BinanceSections section, string symbol, int? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request.AddParameter("symbol", symbol);

		if (limit != null)
			request.AddParameter("limit", limit.Value);

		return MakeRequest<HttpOrderBook>(CreateUrl(section, "depth"), request, cancellationToken);
	}

	public Task<Account> GetAccount(BinanceSections section, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request = ApplTimestamp(request);

		return MakeRequest<Account>(CreateUrl(section, "account", section == BinanceSections.Futures ? "v2" : null), ApplySecret(request, true), cancellationToken);
	}

	private IsolatedAccounts _isolatedAccounts;

	public async Task<IsolatedAccounts> EnsureGetIsolatedAccounts(CancellationToken cancellationToken)
	{
		if(_isolatedAccounts != null)
			return _isolatedAccounts;

		var request = CreateRequest(Method.Get);

		request = ApplTimestamp(request);

		_isolatedAccounts = await MakeRequest<IsolatedAccounts>(CreateUrl(BinanceSections.Margin, "isolated/account"), ApplySecret(request, true), cancellationToken) ?? new();

		return _isolatedAccounts;
	}

	public Task<IEnumerable<BalanceFuturePosition>> GetPositions(BinanceSections section, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request = ApplTimestamp(request);

		return MakeRequest<IEnumerable<BalanceFuturePosition>>(CreateUrl(section, "positionRisk", section == BinanceSections.Futures ? "v2" : null), ApplySecret(request, true), cancellationToken);
	}

	public async Task<IEnumerable<Order>> GetRecentOrders(BinanceSections section, string isolatedSymbol, string[] orderSymbols, CancellationToken cancellationToken)
	{
		BinanceMessageAdapter.CheckSectionSymbol(section, isolatedSymbol);

		var result = new Dictionary<long, Order>();

		var request = CreateRequest(Method.Get);

		if (!isolatedSymbol.IsEmpty())
		{
			request.AddParameter("symbol", isolatedSymbol);
			request.AddParameter("isIsolated", true);
		}

		request = ApplTimestamp(request);

		foreach(var o in await MakeRequest<IEnumerable<Order>>(CreateUrl(section, "openOrders"), ApplySecret(request, true), cancellationToken))
			result[o.Id] = o;

		if(orderSymbols?.Length > 0)
			foreach (var symbol in orderSymbols)
			{
				request = CreateRequest(Method.Get);
				request.AddParameter("symbol", symbol);
				request = ApplTimestamp(request);

				foreach(var o in await MakeRequest<IEnumerable<Order>>(CreateUrl(section, "allOrders"), ApplySecret(request, true), cancellationToken))
					result[o.Id] = o;
			}

		return [.. result.Values.OrderBy(o => o.Id)];
	}

	public async Task<IEnumerable<MyTrade>> GetRecentTrades(BinanceSections section, string[] orderSymbols, CancellationToken cancellationToken)
	{
		if(!(orderSymbols?.Length > 0))
			return [];

		var result = new List<MyTrade>();

		foreach (var symbol in orderSymbols)
		{
			var request = CreateRequest(Method.Get);
			request.AddParameter("symbol", symbol);
			request = ApplTimestamp(request);

			result.AddRange(await MakeRequest<IEnumerable<MyTrade>>(CreateUrl(section, section.IsCommonFutures() ? "userTrades" : "myTrades"), ApplySecret(request, true), cancellationToken));
		}

		return result;
	}

	public Task RegisterOCO(
		string symbol, string side, decimal price, decimal volume,
		decimal stopActivationPrice, decimal? stopLimitPrice,
		string limitClientId, string stopClientId,
		decimal? icebergQuantity, bool? isIsolated, string tif,
		CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("side", side)
			.AddParameter("quantity", volume)
			.AddParameter("price", price)
			.AddParameter("stopPrice", stopActivationPrice)
			.AddParameter("newOrderRespType", "ACK")
			.AddParameter("limitClientOrderId", limitClientId)
			.AddParameter("stopClientOrderId", stopClientId);

		if(icebergQuantity != null)
			request
				.AddParameter("limitIcebergQty", icebergQuantity.Value)
				.AddParameter("stopIcebergQty", icebergQuantity.Value);

		if (isIsolated == true)
			request.AddParameter("isIsolated", true.To<string>().ToLowerInvariant());

		if (stopLimitPrice != null)
		{
			request.AddParameter("stopLimitPrice", stopLimitPrice.Value);
			if(tif.IsEmpty())
				tif = "GTC";

			request.AddParameter("stopLimitTimeInForce", tif);
		}
		else if (!tif.IsEmpty())
		{
			request.AddParameter("stopLimitTimeInForce", tif);
		}

		request = ApplTimestamp(request);

		return MakeRequest<object>(CreateUrl(isIsolated == null ? BinanceSections.Spot : BinanceSections.Margin, "order/oco"), ApplySecret(request, true), cancellationToken);
	}

	public Task<Order> RegisterOrder(BinanceSections section, string symbol, string side, string type, string tif,
		decimal? price, decimal volume, string clientId, decimal? stopPrice, decimal? icebergQuantity,
		bool? reduceOnly, decimal? activationPrice, string workingType, bool? closePosition, bool isIsolated,
		CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("side", side)
			.AddParameter("newOrderRespType", "ACK")
			.AddParameter("type", type);

		if (!tif.IsEmpty())
			request.AddParameter("timeInForce", tif);

		request.AddParameter("quantity", volume);

		if (price != null)
			request.AddParameter("price", price.Value);

		if (reduceOnly != null)
			request.AddParameter("reduceOnly", reduceOnly.Value.To<string>().ToLowerInvariant());

		request.AddParameter("newClientOrderId", clientId);

		if (icebergQuantity != null)
			request.AddParameter("icebergQty", icebergQuantity.Value);

		if (stopPrice != null)
			request.AddParameter("stopPrice", stopPrice.Value);

		if (closePosition != null)
			request.AddParameter("closePosition", closePosition.Value.To<string>().ToLowerInvariant());

		if (activationPrice != null)
			request.AddParameter("activationPrice", activationPrice.Value);

		if (!workingType.IsEmpty())
			request.AddParameter("workingType", workingType);

		if (isIsolated)
			request.AddParameter("isIsolated", true.To<string>().ToLowerInvariant());

		request = ApplTimestamp(request);

		return MakeRequest<Order>(CreateUrl(section, "order"), ApplySecret(request, true), cancellationToken);
	}

	public Task CancelOrder(BinanceSections section, string symbol, long? orderId, string originClientId, string clientId, bool isIsolated, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Delete);

		request
			.AddParameter("symbol", symbol);

		if (orderId != null)
			request.AddParameter("orderId", orderId.Value);

		if (!originClientId.IsEmpty())
			request.AddParameter("origClientOrderId", originClientId);

		if (!clientId.IsEmpty())
			request.AddParameter("newClientOrderId", clientId);

		if (isIsolated)
			request.AddParameter("isIsolated", true.To<string>().ToLowerInvariant());

		request = ApplTimestamp(request);

		return MakeRequest<object>(CreateUrl(section, "order"), ApplySecret(request, true), cancellationToken);
	}

	public async Task<string> CreateListenKey(BinanceSections section, string isolatedSymbol, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		if (isolatedSymbol.IsEmptyOrWhiteSpace())
		{
			dynamic response = await MakeRequest<object>(CreateUrl(section, "userDataStream"), ApplySecret(request, false), cancellationToken);
			return (string)response.listenKey;
		}

		request.AddParameter("symbol", isolatedSymbol);
		dynamic response2 = await MakeRequest<object>(CreateUrl(section, "userDataStream/isolated"), ApplySecret(request, false), cancellationToken);
		return (string)response2.listenKey;
	}

	public Task PingListenKey(BinanceSections section, string listenKey, string isolatedSymbol, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Put);

		if (isolatedSymbol.IsEmptyOrWhiteSpace())
		{
			request.AddParameter("listenKey", listenKey);
			return MakeRequest<object>(CreateUrl(section, "userDataStream"), ApplySecret(request, false), cancellationToken);
		}

		request.AddParameter("symbol", isolatedSymbol);
		return MakeRequest<object>(CreateUrl(section, "userDataStream/isolated"), ApplySecret(request, false), cancellationToken);
	}

	public Task DeleteListenKey(BinanceSections section, string listenKey, string isolatedSymbol, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Delete);

		request.AddParameter("listenKey", listenKey);

		if (isolatedSymbol.IsEmptyOrWhiteSpace())
		{
			return MakeRequest<object>(CreateUrl(section, "userDataStream"), ApplySecret(request, false), cancellationToken);
		}
		else
		{
			request.AddParameter("symbol", isolatedSymbol);
			return MakeRequest<object>(CreateUrl(section, "userDataStream/isolated"), ApplySecret(request, false), cancellationToken);
		}
	}

	public async Task<string> Withdraw(string asset, decimal volume, WithdrawInfo info, string comment, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var request = CreateRequest(Method.Post);

		request
			.AddQueryParameter("coin", asset)
			.AddQueryParameter("address", info.CryptoAddress);

		if (!info.PaymentId.IsEmpty())
			request.AddQueryParameter("addressTag", info.PaymentId);

		request
			.AddQueryParameter("amount", volume.To<string>());

		if (comment.IsEmpty())
			comment = asset;

		request.AddQueryParameter("name", comment.DataEscape());

		request = ApplTimestamp(request, ParameterType.QueryString);

		var adapter = (BinanceMessageAdapter)Parent;
		var hostSpot = adapter.HostRestSpot;
		hostSpot.CheckHostName(nameof(adapter.HostRestSpot));

		dynamic response = await MakeRequest<object>(new Url($"https://{hostSpot}/sapi/v1/capital/withdraw/apply"), ApplySecret(request, true, true), cancellationToken);

		return (string)response.id;
	}

	private Uri CreateUrl(BinanceSections section, string methodName, string version = null)
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		string baseUrl;

		var adapter = (BinanceMessageAdapter)Parent;
		var hostSpot = adapter.HostRestSpot;
		var hostFuture = adapter.HostRestFuture;
		var hostFutureCoin = adapter.HostRestFutureCoin;

		switch (section)
		{
			case BinanceSections.Spot:
				hostSpot.CheckHostName(nameof(adapter.HostRestSpot));
				baseUrl = $"https://{hostSpot}/api/v3";
				break;
			case BinanceSections.Margin:
				if (_isDemo)
					throw new NotSupportedException();

				hostSpot.CheckHostName(nameof(adapter.HostRestSpot));
				baseUrl = $"https://{hostSpot}/sapi/v1";

				if (methodName != "userDataStream")
					baseUrl += "/margin";

				break;
			case BinanceSections.Futures:
				if (methodName == "userDataStream")
					methodName = "listenKey";

				if (version.IsEmpty())
					version = "v1";

				if (_isDemo)
				{
					baseUrl = $"https://testnet.binancefuture.com/fapi/{version}";
				}
				else
				{
					hostFuture.CheckHostName(nameof(adapter.HostRestFuture));
					baseUrl = $"https://{hostFuture}/fapi/{version}";
				}

				break;
			case BinanceSections.FuturesCoin:
				if (methodName == "userDataStream")
					methodName = "listenKey";

				if (version.IsEmpty())
					version = "v1";

				if (_isDemo)
				{
					baseUrl = $"https://testnet.binancefuture.com/dapi/{version}";
				}
				else
				{
					hostFutureCoin.CheckHostName(nameof(adapter.HostRestFutureCoin));
					baseUrl = $"https://{hostFutureCoin}/dapi/{version}";
				}

				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(section), section, null);
		}

		return $"{baseUrl}/{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method) => new() { Method = method };

	private RestRequest ApplTimestamp(RestRequest request, ParameterType type = ParameterType.GetOrPost)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		return request.AddParameter("timestamp", (long)(DateTime.UtcNow + _serverTimeDiff).ToUnix(false), type);
	}

	private void ApplyKey(RestRequest request)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		if (_key.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);

		request.AddHeader("X-MBX-APIKEY", _key.UnSecure());
	}

	private RestRequest ApplySecret(RestRequest request, bool applySignature, bool isQuery = false)
	{
		ApplyKey(request);

		if (applySignature)
		{
			var encodedArgs = request
			                  .Parameters
			                  .Where(p => isQuery ? p.Type == ParameterType.QueryString : p.Type == ParameterType.GetOrPost && p.Value != null)
			                  .ToQueryString();

			var signature = _hasher
			                .ComputeHash(encodedArgs.UTF8())
			                .Digest()
			                .ToLowerInvariant();

			if (isQuery)
				request.AddQueryParameter("signature", signature);
			else
				request.AddParameter("signature", signature);
		}

		return request;
	}

	private async Task<T> MakeRequest<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj;

		try
		{
			var resp = await request.InvokeAsync2<object>(url, this, this.AddVerboseLog, cancellationToken);
			var weight = resp.Headers?.FirstOrDefault(h => h.Name == "X-MBX-USED-WEIGHT-1M")?.Value;
			this.AddVerboseLog("Response (code={0}, curweight={1}): '{2}'", resp.StatusCode, weight, resp.Content);

			obj = resp.Data;
		}
		catch (RestSharpException)
		{
			throw;
		}

		if (((JToken)obj).Type == JTokenType.Object && ((obj.success != null && obj.success == false) || (obj.code != null && (int)obj.code != 0)))
			throw new InvalidOperationException((string)obj.msg);

		return ((JToken)obj).DeserializeObject<T>();
	}
}
