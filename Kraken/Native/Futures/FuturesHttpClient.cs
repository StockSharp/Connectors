namespace StockSharp.Kraken.Native.Futures;

using System.Security.Cryptography;

using StockSharp.Kraken.Native.Futures.Model;

class FuturesHttpClient : BaseLogReceiver
{
	private readonly SecureString _key;
	private readonly HashAlgorithm _hasher;
	private readonly SHA256 _sha256 = SHA256.Create();
	private readonly string _baseUrl;

	private long _nonce;

	public FuturesHttpClient(SecureString key, SecureString secret, string restEndpoint)
	{
		_key = key;
		_hasher = secret.IsEmpty() ? null : new HMACSHA512(secret.UnSecure().Base64());
		_baseUrl = restEndpoint.ThrowIfEmpty(nameof(restEndpoint)).TrimEnd('/');

		_nonce = DateTime.UtcNow.Ticks;
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Kraken) + "_" + nameof(FuturesHttpClient);

	public Task<Dictionary<string, AssetPair>> GetAssetPairsAsync(string info = null, string pairs = null, CancellationToken cancellationToken = default)
	{
		var request = CreateRequest(Method.Get);

		if (info != null)
			request.AddParameter("info", info);

		if (pairs != null)
			request.AddParameter("pairs", pairs);

		return MakeRequestAsync<Dictionary<string, AssetPair>>(CreateUrl("public/AssetPairs"), request, cancellationToken);
	}

	public async Task<(Dictionary<string, Ohlc[]> Data, long Last)> GetOhlcAsync(string pair, int interval, long? since = null, CancellationToken cancellationToken = default)
	{
		if (pair.IsEmpty())
			throw new ArgumentNullException(nameof(pair));

		var request = CreateRequest(Method.Get);

		request.AddParameter("pair", pair);
		request.AddParameter("interval", interval);

		if (since != null)
			request.AddParameter("since", since.Value);

		var jo = await MakeRequestAsync<JObject>(CreateUrl("public/OHLC"), request, cancellationToken);

		var data = new Dictionary<string, Ohlc[]>();
		long last = 0;

		if (jo != null)
		{
			if (jo.TryGetValue("last", out var lastToken))
			{
				last = lastToken.Value<long>();
				jo.Remove("last");
			}

			foreach (var prop in jo.Properties())
			{
				data[prop.Name] = prop.Value.ToObject<Ohlc[]>();
			}
		}

		return (data, last);
	}

	public async Task<(Dictionary<string, Trade[]> Data, long Last)> GetRecentTradesAsync(string pair, long? since = null, CancellationToken cancellationToken = default)
	{
		var request = CreateRequest(Method.Get);

		request.AddParameter("pair", pair);

		if (since != null)
			request.AddParameter("since", since.Value);

		var jo = await MakeRequestAsync<JObject>(CreateUrl("public/Trades"), request, cancellationToken);

		var data = new Dictionary<string, Trade[]>();
		long last = 0;

		if (jo != null)
		{
			if (jo.TryGetValue("last", out var lastToken))
			{
				last = lastToken.Value<long>();
				jo.Remove("last");
			}

			foreach (var prop in jo.Properties())
			{
				data[prop.Name] = prop.Value.ToObject<Trade[]>();
			}
		}

		return (data, last);
	}

	public Task<Dictionary<string, decimal>> GetAccountBalanceAsync(CancellationToken cancellationToken = default)
	{
		var uri = CreateUrl("private/Balance");
		var request = CreateRequest(Method.Post);
		return MakeRequestAsync<Dictionary<string, decimal>>(uri, ApplySecret(request, uri), cancellationToken);
	}

	public Task<OpenOrders> GetOpenOrdersAsync(bool includeTrades = false, string userRef = null, CancellationToken cancellationToken = default)
	{
		var uri = CreateUrl("private/OpenOrders");

		var request = CreateRequest(Method.Post);

		if (includeTrades)
			request.AddParameter("trades", "true");

		if (!userRef.IsEmpty())
			request.AddParameter("userref", userRef);

		return MakeRequestAsync<OpenOrders>(uri, ApplySecret(request, uri), cancellationToken);
	}

	public Task<ClosedOrders> GetClosedOrdersAsync(bool includeTrades = false, long? userRef = null, long? start = null, long? end = null, int? offset = null, string closeTime = null, CancellationToken cancellationToken = default)
	{
		var uri = CreateUrl("private/ClosedOrders");

		var request = CreateRequest(Method.Post);

		if (includeTrades)
			request.AddParameter("trades", "true");

		if (userRef != null)
			request.AddParameter("userref", userRef.Value);

		if (start != null)
			request.AddParameter("start", start.Value);

		if (end != null)
			request.AddParameter("end", end.Value);

		if (offset != null)
			request.AddParameter("ofs", offset.Value);

		if (!closeTime.IsEmpty())
			request.AddParameter("closetime", closeTime);

		return MakeRequestAsync<ClosedOrders>(uri, ApplySecret(request, uri), cancellationToken);
	}

	public Task<Dictionary<string, OrderInfo>> GetOrdersInfoAsync(IEnumerable<string> transactionIds, bool includeTrades = false, long? userRef = null, CancellationToken cancellationToken = default)
	{
		if (transactionIds == null)
			throw new ArgumentNullException(nameof(transactionIds));

		var uri = CreateUrl("private/QueryOrders");

		var request = CreateRequest(Method.Post);

		if (includeTrades)
			request.AddParameter("trades", "true");

		if (userRef != null)
			request.AddParameter("userref", userRef.Value);

		request.AddParameter("txid", transactionIds.JoinComma());

		return MakeRequestAsync<Dictionary<string, OrderInfo>>(uri, ApplySecret(request, uri), cancellationToken);
	}

	public Task<Dictionary<string, PositionInfo>> GetOpenPositionsAsync(IEnumerable<string> transactionIds, bool doCalculations = false, CancellationToken cancellationToken = default)
	{
		if (transactionIds == null)
			throw new ArgumentNullException(nameof(transactionIds));

		var uri = CreateUrl("private/OpenPositions");

		var request =
			CreateRequest(Method.Post)
				.AddParameter("txid", transactionIds.JoinComma());

		if (doCalculations)
			request.AddParameter("docalcs", "true");

		return MakeRequestAsync<Dictionary<string, PositionInfo>>(uri, ApplySecret(request, uri), cancellationToken);
	}

	public async Task<string> AddOrderAsync(string pair, string type, string orderType, decimal volume, decimal? price = null, decimal? price2 = null, string leverage = null, string orderFlags = null, string startTime = null, double? expireTime = null, long? userRef = null, bool validate = false, CancellationToken cancellationToken = default)
	{
		var uri = CreateUrl("private/AddOrder");

		var request =
			CreateRequest(Method.Post)
				.AddParameter("pair", pair)
				.AddParameter("type", type)
				.AddParameter("ordertype", orderType);

		if (price != null)
			request.AddParameter("price", price.Value);

		if (price2 != null)
			request.AddParameter("price2", price2.Value);

		request.AddParameter("volume", volume);

		if (!leverage.IsEmpty())
			request.AddParameter("leverage", leverage);

		if (!orderFlags.IsEmpty())
			request.AddParameter("oflags", orderFlags);

		if (!startTime.IsEmpty())
			request.AddParameter("starttm", startTime);

		if (expireTime != null)
			request.AddParameter("expiretm", expireTime.Value);

		if (userRef != null)
			request.AddParameter("userref", userRef.Value);

		if (validate)
			request.AddParameter("validate", "true");

		dynamic response = await MakeRequestAsync<object>(uri, ApplySecret(request, uri), cancellationToken, 0);

		return (string)response.txid[0];
	}

	public async Task CancelOrderAsync(string transactionId, CancellationToken cancellationToken = default)
	{
		var uri = CreateUrl("private/CancelOrder");

		var request =
			CreateRequest(Method.Post)
				.AddParameter("txid", transactionId);

		await MakeRequestAsync<object>(uri, ApplySecret(request, uri), cancellationToken, 0);
	}

	public async Task<string> WithdrawAsync(string currency, decimal volume, WithdrawInfo info, CancellationToken cancellationToken = default)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var uri = CreateUrl("private/Withdraw");

		var request =
			CreateRequest(Method.Post)
				.AddParameter("asset", currency)
				.AddParameter("key", _key.UnSecure())
				.AddParameter("amount", volume);

		dynamic response = await MakeRequestAsync<object>(uri, ApplySecret(request, uri), cancellationToken, 0);

		return (string)response.refid;
	}

	private Url CreateUrl(string methodName, string version = "0/")
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return new Url($"{_baseUrl}/{version}{methodName}");
	}

	private RestRequest CreateRequest(Method method)
	{
		var request = new RestRequest((string)null, method);

		if (method == Method.Post)
			request.AddParameter("nonce", ++_nonce);

		return request;
	}

	private RestRequest ApplySecret(RestRequest request, Url url)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		var nonce = (long)request.Parameters.First(p => p.Name == "nonce").Value;

		var encodedArgs = request
			.Parameters
			.Where(p => p.Type == ParameterType.GetOrPost && p.Value != null)
			.Select(p => $"{p.Name}={p.Value.ToString().EncodeUrl().UrlEncodeToUpperCase()}")
			.JoinAnd();

		var urlBytes = url.ToString().Remove(_baseUrl).UTF8();
		var dataBytes = _sha256.ComputeHash((nonce + encodedArgs).UTF8());

		var buffer = new byte[urlBytes.Length + dataBytes.Length];
		Buffer.BlockCopy(urlBytes, 0, buffer, 0, urlBytes.Length);
		Buffer.BlockCopy(dataBytes, 0, buffer, urlBytes.Length, dataBytes.Length);

		var signature = _hasher.ComputeHash(buffer).Base64();

		request
			.AddHeader("API-Key", _key.UnSecure())
			.AddHeader("API-Sign", signature);

		return request;
	}

	private async Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, CancellationToken cancellationToken, int apiCallCost = 1)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		var error = (JArray)obj.error;

		if (error == null || error.Count == 0)
			return ((JToken)obj.result).DeserializeObject<T>();

		throw new InvalidOperationException((string)error[0]);
	}
}
