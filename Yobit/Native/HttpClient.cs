namespace StockSharp.Yobit.Native;

using System.Globalization;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

class HttpClient(SecureString key, SecureString secret) : BaseLogReceiver
{
	private readonly SecureString _key = key;

	private readonly HashAlgorithm _hasher = secret.IsEmpty() ? null : new HMACSHA512(secret.UnSecure().ASCII());

	private const string _publicUrl = "https://yobit.net/api";
	private const string _tradeUrl = "https://yobit.net/tapi";
	private static readonly Regex _pairIdRegex = new(@"var\s+pair_id\s*=\s*'(?<id>\d+)'", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	private readonly UTCIncrementalIdGenerator _nonceGen = new();
	private readonly Lock _sync = new();
	private readonly Dictionary<string, long?> _pairIds = new(StringComparer.InvariantCultureIgnoreCase);

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Yobit) + "_" + nameof(HttpClient);

	public async Task<IDictionary<string, Symbol>> GetSymbolsAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		dynamic response = await MakeRequestAsync<object>(CreateUrl(_publicUrl, "info"), request, cancellationToken);

		return ((JToken)response.pairs).DeserializeObject<IDictionary<string, Symbol>>();
	}

	public async Task<long?> GetPairIdAsync(string symbol, CancellationToken cancellationToken)
	{
		if (symbol.IsEmpty())
			throw new ArgumentNullException(nameof(symbol));

		using (_sync.EnterScope())
		{
			if (_pairIds.TryGetValue(symbol, out var pairId))
				return pairId;
		}

		var parts = symbol.Split('_');

		if (parts.Length != 2)
			throw new ArgumentOutOfRangeException(nameof(symbol), symbol, LocalizedStrings.InvalidValue);

		var tradeUrl = $"https://yobit.net/en/trade/{parts[0].ToUpperInvariant()}/{parts[1].ToUpperInvariant()}";
		string html;

		using (var client = new System.Net.Http.HttpClient())
		{
			html = await client.GetStringAsync(tradeUrl, cancellationToken);
		}

		long? resolved = null;
		var match = _pairIdRegex.Match(html);

		if (match.Success && long.TryParse(match.Groups["id"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPairId))
			resolved = parsedPairId;

		using (_sync.EnterScope())
			_pairIds[symbol] = resolved;

		return resolved;
	}

	public async Task<IEnumerable<KeyValuePair<string, Ticker>>> GetTickersAsync(IEnumerable<string> symbols, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		return await MakeRequestAsync<IDictionary<string, Ticker>>(CreateUrl(_publicUrl, "ticker/" + symbols.Join("-")), request, cancellationToken)
			   ?? Enumerable.Empty<KeyValuePair<string, Ticker>>();
	}

	public async Task<IDictionary<string, OrderBook>> GetDepthsAsync(IEnumerable<string> symbols, int? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		if (limit != null)
			request.AddParameter("limit", limit.Value, ParameterType.QueryString);

		return await MakeRequestAsync<IDictionary<string, OrderBook>>(CreateUrl(_publicUrl, "depth/" + symbols.Join("-")), request, cancellationToken)
			?? new Dictionary<string, OrderBook>();
	}

	public async Task<IDictionary<string, Trade[]>> GetTicksAsync(IEnumerable<string> symbols, int? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		if (limit != null)
			request.AddParameter("limit", limit.Value, ParameterType.QueryString);

		return await MakeRequestAsync<IDictionary<string, Trade[]>>(CreateUrl(_publicUrl, "trades/" + symbols.Join("-")), request, cancellationToken)
			   ?? new Dictionary<string, Trade[]>();
	}

	public async Task<IDictionary<string, RefPair<decimal, decimal>>> GetFundsAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post)
			.AddParameter("method", "getInfo");

		dynamic response = await MakeRequestAsync<object>(CreateUrl(_tradeUrl, string.Empty, string.Empty), ApplySecret(request), cancellationToken);

		var dict = new Dictionary<string, RefPair<decimal, decimal>>(StringComparer.InvariantCultureIgnoreCase);

		if (response.funds != null)
		{
			foreach (var property in ((JObject)response.funds).Properties())
			{
				dict.Add(property.Name, RefTuple.Create((decimal)property.Value, 0M));
			}
		}

		if (response.funds_incl_orders != null)
		{
			foreach (var property in ((JObject)response.funds_incl_orders).Properties())
			{
				var tuple = dict.SafeAdd(property.Name, key => RefTuple.Create(0M, 0M));
				tuple.Second = (decimal)property.Value;
			}
		}

		return dict;
	}

	public async Task<IEnumerable<KeyValuePair<long, Order>>> GetActiveOrdersAsync(string symbol, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("method", "ActiveOrders")
			.AddParameter("pair", symbol);

		return await MakeRequestAsync<IDictionary<long, Order>>(CreateUrl(_tradeUrl, string.Empty, string.Empty), ApplySecret(request), cancellationToken)
			   ?? Enumerable.Empty<KeyValuePair<long, Order>>();
	}

	public async Task<IEnumerable<KeyValuePair<long, Trade>>> GetTradeHistoryAsync(IEnumerable<string> symbols, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("method", "TradeHistory")
			.AddParameter("pair", symbols.Join("-"));

		return await MakeRequestAsync<IDictionary<long, Trade>>(CreateUrl(_tradeUrl, string.Empty, string.Empty), ApplySecret(request), cancellationToken)
			   ?? Enumerable.Empty<KeyValuePair<long, Trade>>();
	}

	public async Task<Order> GetOrderInfoAsync(long orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("method", "OrderInfo")
			.AddParameter("order_id", orderId);

		var dict = await MakeRequestAsync<IDictionary<long, Order>>(CreateUrl(_tradeUrl, string.Empty, string.Empty), ApplySecret(request), cancellationToken);

		return dict.TryGetValue(orderId);
	}

	public async Task<long> RegisterOrderAsync(string symbol, string side, decimal price, decimal volume, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("method", "Trade")
			.AddParameter("pair", symbol)
			.AddParameter("type", side)
			.AddParameter("rate", price)
			.AddParameter("amount", volume);

		dynamic response = await MakeRequestAsync<object>(CreateUrl(_tradeUrl, string.Empty, string.Empty), ApplySecret(request), cancellationToken);

		return (long)response.order_id;
	}

	public Task CancelOrderAsync(long orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("method", "CancelOrder")
			.AddParameter("order_id", orderId);

		return MakeRequestAsync<object>(CreateUrl(_tradeUrl, string.Empty, string.Empty), ApplySecret(request), cancellationToken);
	}

	public async Task WithdrawAsync(string symbol, decimal volume, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var request = CreateRequest(Method.Post);

		request
			.AddParameter("method", "WithdrawCoinsToAddress")
			.AddParameter("coinName", symbol)
			.AddParameter("address", info.CryptoAddress)
			.AddParameter("amount", volume);

		await MakeRequestAsync<object>(CreateUrl(_tradeUrl, string.Empty, string.Empty), ApplySecret(request), cancellationToken);
	}

	private static Uri CreateUrl(string baseUrl, string methodName, string version = "3")
	{
		if (baseUrl.IsEmpty())
			throw new ArgumentNullException(nameof(baseUrl));

		if (!version.IsEmpty())
			version = "/" + version;

		if (!methodName.IsEmpty())
			methodName = "/" + methodName;

		return $"{baseUrl}{version}{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private RestRequest ApplySecret(RestRequest request)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		request.AddParameter("nonce", _nonceGen.GetNextId());

		var encodedArgs = request
			.Parameters
			.Where(p => p.Type == ParameterType.GetOrPost && p.Value != null)
			.ToQueryString();

		var signature = _hasher
			.ComputeHash(encodedArgs.UTF8())
			.Digest()
			.ToLowerInvariant();

		request
			.AddHeader("Key", _key.UnSecure())
			.AddHeader("Sign", signature);

		return request;
	}

	private async Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (obj.success != null)
		{
			if ((int)obj.success == 0)
				throw new InvalidOperationException((string)obj.error);

			if (obj.@return == null)
				return default;

			return ((JToken)obj.@return).DeserializeObject<T>();
		}

		return ((JToken)obj).DeserializeObject<T>();
	}
}