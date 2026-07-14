namespace StockSharp.CoinCap.Native;

class HttpClient : BaseLogReceiver
{
	private readonly string _baseUrl;
	private readonly SecureString _token;
	private const string _version = "v3";

	public HttpClient(string address, SecureString token)
	{
		if (address.IsEmpty())
			throw new ArgumentNullException(nameof(address));

		if (token.IsEmpty())
			throw new ArgumentNullException(nameof(token));

		_baseUrl = $"https://{address}";
		_token = token;
	}

	// to get readable name after obfuscation
	public override string Name => nameof(CoinCap) + "_" + nameof(HttpClient);

	public Task<IEnumerable<Asset>> GetAssetsAsync(CancellationToken cancellationToken)
		=> MakeRequestAsync<IEnumerable<Asset>>(CreateUrl("assets"), CreateRequest(Method.Get), cancellationToken);

	public Task<IEnumerable<Exchange>> GetExchangesAsync(CancellationToken cancellationToken)
		=> MakeRequestAsync<IEnumerable<Exchange>>(CreateUrl("exchanges"), CreateRequest(Method.Get), cancellationToken);

	public Task<IEnumerable<Market>> GetAssetMarketsAsync(string asset, CancellationToken cancellationToken)
		=> MakeRequestAsync<IEnumerable<Market>>(CreateUrl($"assets/{asset}/markets"), CreateRequest(Method.Get), cancellationToken);

	public Task<IEnumerable<Market>> GetMarketsAsync(CancellationToken cancellationToken)
		=> MakeRequestAsync<IEnumerable<Market>>(CreateUrl("markets"), CreateRequest(Method.Get), cancellationToken);

	public Task<IEnumerable<Ohlc>> GetCandlesAsync(string exchange, string interval, string baseId, string quoteId, long? start, long? end, CancellationToken cancellationToken)
	{
		var url = CreateUrl("candles");
		var qs = url.QueryString;

		qs
			.Append("exchange", exchange)
			.Append("interval", interval)
			.Append("baseId", baseId)
			.Append("quoteId", quoteId);

		if (start != null)
			qs.Append("start", start.Value);

		if (end != null)
			qs.Append("end", end.Value);

		return MakeRequestAsync<IEnumerable<Ohlc>>(url, CreateRequest(Method.Get), cancellationToken);
	}

	private Url CreateUrl(string methodName, string version = _version)
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return new Url($"{_baseUrl}/{version}/{methodName}");
	}

	private RestRequest CreateRequest(Method method)
	{
		var request = new RestRequest((string)null, method);
		request.SetBearer(_token);
		return request;
	}

	private async Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (obj is JObject)
		{
			if (obj.error != null)
				throw new InvalidOperationException((string)obj.error);

			if (obj.data != null)
				obj = obj.data;
		}

		return ((JToken)obj).DeserializeObject<T>();
	}
}