namespace StockSharp.Cex.Native;

    class HttpClient : BaseLogReceiver
{
	private const string _baseUrl = "https://cex.io/api";

	// to get readable name after obfuscation
	public override string Name => nameof(Cex) + "_" + nameof(HttpClient);

	public async Task<IEnumerable<Symbol>> GetSymbolsAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		dynamic response = await MakeRequestAsync<object>(CreateUrl("currency_limits"), request, cancellationToken);

		return ((JToken)response.data.pairs).DeserializeObject<IEnumerable<Symbol>>();
	}

	public async Task<IEnumerable<Trade>> GetTradeHistoryAsync(string[] ccy, long? since, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		if (since != null)
			request.AddParameter("since", since.Value);

		var trades = await MakeRequestAsync<IEnumerable<Trade>>(CreateUrl($"trade_history/{ccy[0]}/{ccy[1]}/"), request, cancellationToken);
		return trades.OrderBy(t => t.Id);
	}

	public async Task<IDictionary<string, Ohlc[]>> GetCandlesAsync(string[] ccy, DateTime date, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		var response = await MakeRequestAsync<object>(CreateUrl($"ohlcv/hd/{date:yyyyMMdd}/{ccy[0]}/{ccy[1]}"), request, cancellationToken);

		var dict = new Dictionary<string, Ohlc[]>();

		if (response == null)
			return dict;

		foreach (var prop in ((JObject)response).Properties())
		{
			if (!prop.Name.StartsWithIgnoreCase("data"))
				continue;

			dict.Add(prop.Name, ((string)prop.Value).DeserializeObject<Ohlc[]>());
		}

		return dict;
	}

	private static Uri CreateUrl(string methodName, string version = "")
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

		if (obj == null)
			return default;

		if (((JToken)obj).Type == JTokenType.Object && obj.error != null)
			throw new InvalidOperationException((string)obj.error);

		return ((JToken)obj).DeserializeObject<T>();
	}
}