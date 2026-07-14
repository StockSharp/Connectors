namespace StockSharp.Okex.Native;

using System.Collections;

using Newtonsoft.Json.Linq;

class HttpClient : BaseLogReceiver
{
	private class OkexEndPoint
	{
		private enum EndPointSections
		{
			Account,
			Asset,
			Market,
			Public,
			Trade,
			Users,
		}

		private EndPointSections Section { get; }
		private Method RequestMethod { get; }
		private string ApiMethod { get; }
		private bool RequireAuth => Section is not (EndPointSections.Market or EndPointSections.Public);
		private string EndPoint => $"/api/v5/{Section.ToString().ToLowerInvariant()}/{ApiMethod}";

		private OkexEndPoint(EndPointSections section, string apiMethod, Method reqMethod = Method.Get)
		{
			Section = section;
			ApiMethod = apiMethod;
			RequestMethod = reqMethod;
		}

		public static readonly OkexEndPoint Withdrawal             = new(EndPointSections.Asset, "withdrawal", Method.Post);

		public static readonly OkexEndPoint AccountConfig          = new(EndPointSections.Account, "config");
		public static readonly OkexEndPoint Positions              = new(EndPointSections.Account, "positions");
		public static readonly OkexEndPoint SetPosMode             = new(EndPointSections.Account, "set-position-mode", Method.Post);

		public static readonly OkexEndPoint Instruments            = new(EndPointSections.Public, "instruments");

		public static readonly OkexEndPoint Candles                = new(EndPointSections.Market, "candles");
		public static readonly OkexEndPoint HistoryCandles         = new(EndPointSections.Market, "history-candles");

		public static readonly OkexEndPoint OpenOrders             = new(EndPointSections.Trade, "orders-pending");
		public static readonly OkexEndPoint OrderHistory7d         = new(EndPointSections.Trade, "orders-history");
		public static readonly OkexEndPoint OrderHistory3M         = new(EndPointSections.Trade, "orders-history-archive");
		public static readonly OkexEndPoint OrderFills             = new(EndPointSections.Trade, "fills");

		public static void CheckResult(JToken obj)
		{
			if(obj is not JObject result)
				throw new InvalidOperationException($"unexpected result type '{obj?.GetType().FullName}', obj='{obj}'");

			var code = (string)result["code"];
			if(code != "0")
				throw new InvalidOperationException((string)result["msg"] ?? "unknown error, result is " + result);

			if(result["data"] is not JArray)
				throw new InvalidOperationException("no data, result is " + result);
		}

		public async Task<T> MakeRequestAsync<T>(HttpClient parent, CancellationToken token, Action<RestRequest> prepare = null, Action<JToken> checkResult = null) where T: class
		{
			static JToken GetResult(JToken obj)
			{
				var arr = (JArray)obj?["data"];
				return typeof(T).Is(typeof(IEnumerable)) ? arr : arr?[0];
			}

			var request = new RestRequest((string)null, RequestMethod);
			prepare?.Invoke(request);

			var url = new Url($"{parent._baseUrl}{EndPoint}");

			if(RequireAuth)
				parent._authenticator.ApplySecret(request, url);

			var obj = await request.InvokeAsync<object>(url, this, parent.AddVerboseLog, token) as JToken;

			checkResult ??= CheckResult;

			checkResult(obj);
			return GetResult(obj)?.DeserializeObject<T>();
		}
	}

	private readonly Authenticator _authenticator;
	private readonly string _baseUrl;

	public HttpClient(string baseUrl, Authenticator authenticator)
	{
		_baseUrl = baseUrl.ThrowIfEmpty(nameof(baseUrl));
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Okex) + "_" + nameof(HttpClient);

	public Task<AccountConfig> GetAccountConfigAsync(CancellationToken token)
		=> OkexEndPoint.AccountConfig.MakeRequestAsync<AccountConfig>(this, token);

	public Task<AccountConfig> SetNetModeAsync(CancellationToken token)
		=> OkexEndPoint.SetPosMode.MakeRequestAsync<AccountConfig>(this, token, req => req.AddParameter("posMode", "net_mode"));

	public async Task<IEnumerable<Instrument>> GetInstrumentsAsync(string instType, bool isOption, string underlying, CancellationToken token)
	{
		if(isOption && underlying.IsEmptyOrWhiteSpace())
			throw new InvalidOperationException("underlying must be set for options");

		return await OkexEndPoint.Instruments.MakeRequestAsync<IEnumerable<Instrument>>(this, token, req =>
		{
			req.AddParameter("instType", instType);

			if(!underlying.IsEmptyOrWhiteSpace())
				req.AddParameter("uly", underlying);
		});
	}

	public async Task<IEnumerable<Ohlc>> GetCandlesAsync(string instrumentId, bool isHistory, string bar, DateTime before, DateTime after, CancellationToken token)
	{
		var endPoint = isHistory ? OkexEndPoint.HistoryCandles : OkexEndPoint.Candles;

		return await endPoint.MakeRequestAsync<IEnumerable<Ohlc>>(this, token, req =>
		{
			req.AddParameter("instId", instrumentId);
			req.AddParameter("bar", bar);
			req.AddParameter("before", before.ToUnix(false).To<long>());
			req.AddParameter("after", after.ToUnix(false).To<long>());
		});
	}

	private static async Task<IEnumerable<T>> GetPaginatedDataAsync<T>(
		int reqMaxLimit,
		Func<T, DateTime?> timeGetter,
		Func<int, T, Task<T[]>> getNextPageAsync,
		Comparison<T> comparer,
		TimeSpan? period = null,
		int? maxCount = null) where T : class
	{
		maxCount ??= int.MaxValue;

		var minTime = DateTime.UtcNow - period ?? DateTime.MinValue;
		var actualLimit = 1.Max(reqMaxLimit.Min(maxCount.Value));

		var list = new List<T>();
		T lastItem = null;

		do
		{
			var spaceLeft = maxCount.Value - list.Count;
			if(!(spaceLeft > 0))
				break;

			var arr = await getNextPageAsync(actualLimit, lastItem);

			list.AddRange(arr.Where(i => !(timeGetter(i) < minTime)).Take(spaceLeft));

			if(arr.Length < actualLimit)
				break;

			lastItem = arr.Last();
		} while(true);

		list.Sort(comparer);

		return list;
	}

	public async Task<OwnTrade[]> GetFillsAsync(int maxCount, CancellationToken token)
	{
		async Task<OwnTrade[]> GetNextPage(int count, OwnTrade prevLast)
		{
			return (await OkexEndPoint.OrderFills.MakeRequestAsync<IEnumerable<OwnTrade>>(this, token, req =>
			{
				req.AddParameter("limit", count);

				if (prevLast != null)
					req.AddParameter("after", prevLast.BillId);
			})).ToArray();
		}

		var actualLimit = 0.Max(maxCount.Min(1000));

		if (actualLimit != maxCount)
			this.AddWarningLog($"invalid value for recent trades limit ({maxCount}). corrected value={actualLimit}");

		if(actualLimit == 0)
		{
			this.AddVerboseLog("recent trades loading is disabled");
			return [];
		}

		return (await GetPaginatedDataAsync<OwnTrade>(100, t => t.Time, GetNextPage, (t1, t2) => t1.TradeId.CompareTo(t2.TradeId), TimeSpan.FromDays(1), actualLimit)).ToArray();
	}

	public async Task<OkexOrder[]> GetOpenOrdersAsync(CancellationToken token)
	{
		async Task<OkexOrder[]> GetNextPage(int count, OkexOrder prevLast)
		{
			return (await OkexEndPoint.OpenOrders.MakeRequestAsync<IEnumerable<OkexOrder>>(this, token, req =>
			{
				req.AddParameter("limit", count);

				if (prevLast != null)
					req.AddParameter("after", prevLast.Id);
			})).ToArray();
		}

		return (await GetPaginatedDataAsync<OkexOrder>(100, _ => null, GetNextPage, (o1, o2) => string.Compare(o1.Id, o2.Id, StringComparison.Ordinal))).ToArray();
	}


	private async Task<IEnumerable<OkexOrder>> GetRecentOrdersByInstTypeAsync(string it, CancellationToken token, TimeSpan? period = null, int? maxCount = null)
	{
		async Task<OkexOrder[]> GetNextPage(int count, OkexOrder prevLast)
		{
			return (await OkexEndPoint.OrderHistory7d.MakeRequestAsync<IEnumerable<OkexOrder>>(this, token, req =>
			{
				req.AddParameter("instType", it);
				req.AddParameter("limit", count);

				if (prevLast != null)
					req.AddParameter("after", prevLast.Id);
			})).ToArray();
		}

		return await GetPaginatedDataAsync<OkexOrder>(100, _ => null, GetNextPage, (o1, o2) => string.Compare(o1.Id, o2.Id, StringComparison.Ordinal), period, maxCount);
	}

	public async Task<OkexOrder[]> GetRecentOrdersAsync(int perTypeLimit, CancellationToken token)
	{
		var per = TimeSpan.FromDays(2);

		var actualPerTypeLimit = 0.Max(perTypeLimit.Min(1000));

		if (actualPerTypeLimit != perTypeLimit)
			this.AddWarningLog($"invalid value for recent orders limit ({perTypeLimit}). corrected value={actualPerTypeLimit}");

		if(actualPerTypeLimit == 0)
		{
			this.AddVerboseLog("recent orders loading is disabled");
			return [];
		}

		var types = new[] { SecurityTypes.CryptoCurrency, Extensions.Margin, SecurityTypes.Swap, SecurityTypes.Future, SecurityTypes.Option };
		var tasks = types.Select(t => GetRecentOrdersByInstTypeAsync(t.ToNative(), token, per, actualPerTypeLimit));

		await Task.WhenAll(tasks);

		var list = new List<OkexOrder>();
		foreach (var t in tasks)
			list.AddRange(t.Result);

		return [.. list];
	}

	public Task<IEnumerable<OkexPosition>> GetPositionsAsync(CancellationToken token) => OkexEndPoint.Positions.MakeRequestAsync<IEnumerable<OkexPosition>>(this, token);

	public async Task<long> WithdrawAsync(string currency, SecureString adminPwd, decimal volume, WithdrawInfo info, CancellationToken token)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var result = await OkexEndPoint.Withdrawal.MakeRequestAsync<JObject>(this, token, req =>
		{
			req
				.AddParameter("ccy", currency)
				.AddParameter("amt", volume)
				.AddParameter("dest", info.Comment)
				.AddParameter("toAddr", info.CryptoAddress)
				.AddParameter("pwd", adminPwd.UnSecure());

			if (info.ChargeFee != null)
				req.AddParameter("fee", info.ChargeFee.Value);
		});

		return result["wdId"]?.Value<long>() ?? throw new InvalidOperationException("invalid result");
	}
}
