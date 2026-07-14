namespace StockSharp.HitBtc.Native;

class HttpClient : BaseLogReceiver
{
	private const string _baseUrl = "https://api.hitbtc.com/api";

	// to get readable name after obfuscation
	public override string Name => nameof(HitBtc) + "_" + nameof(HttpClient);

	public Task<IEnumerable<Trade>> GetTradesAsync(string instrument, string sort, string by, long? from, long? till, long? limit, long? offset, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		if (!sort.IsEmpty())
			request.AddParameter("sort", sort);

		if (from != null)
			request.AddParameter("from", from.Value);

		if (till != null)
			request.AddParameter("till", till.Value);

		if (limit != null)
			request.AddParameter("limit", limit.Value);

		if (offset != null)
			request.AddParameter("offset", offset.Value);

		return MakeRequestAsync<IEnumerable<Trade>>(CreateUrl($"public/trades/{instrument}"), request, cancellationToken);
	}

	public Task<IEnumerable<Ohlc>> GetCandlesAsync(string instrument, string period, int? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request.AddParameter("period", period);

		if (limit != null)
			request.AddParameter("limit", limit.Value);

		return MakeRequestAsync<IEnumerable<Ohlc>>(CreateUrl($"public/candles/{instrument}"), request, cancellationToken);
	}

	private static Uri CreateUrl(string methodName, string version = "2/")
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/{version}{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private async Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (obj is JObject && obj.success == false)
			throw new InvalidOperationException((string)obj.message);

		return ((JToken)obj).DeserializeObject<T>();
	}
}