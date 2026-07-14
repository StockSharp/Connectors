namespace StockSharp.AlorHistory.Native;

using Newtonsoft.Json.Linq;

class HttpClient : BaseLogReceiver
{
	private readonly string _baseUrl;

	public HttpClient(string domain)
	{
		_baseUrl = $"https://{domain}";
	}

	// to get readable name after obfuscation
	public override string Name => nameof(AlorHistory) + "_" + nameof(HttpClient);

	public async IAsyncEnumerable<Security> GetSecurities(
		string query = default,
		int? limit = default,
		int? offset = default,
		string cficode = default,
		string exchange = default,
		string format = default,
		bool includeOptions = default,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var url = CreateUrl("md/v2/securities");
		var request = CreateRequest(Method.Get);

		if (!query.IsEmpty()) request.AddParameter(nameof(query), query);
		if (limit.HasValue) request.AddParameter(nameof(limit), limit.Value);
		if (offset.HasValue) request.AddParameter(nameof(offset), offset.Value);
		if (!cficode.IsEmpty()) request.AddParameter(nameof(cficode), cficode);
		if (!exchange.IsEmpty()) request.AddParameter(nameof(exchange), exchange);
		if (!format.IsEmpty()) request.AddParameter(nameof(format), format);

		while (true)
		{
			var response = await MakeRequest<JArray>(url, request, cancellationToken);

			var needBreak = true;

			foreach (var item in response)
			{
				var sec = item.DeserializeObject<Security>();

				if (includeOptions || sec.Type.ToSecurityType() != SecurityTypes.Option)
				{
					needBreak = false;
					yield return sec;
				}
			}

			if (needBreak || response.Count < (limit ?? 25) || !limit.HasValue)
				break;

			offset = (offset ?? 0) + response.Count;
			request.Parameters.RemoveParameter(nameof(offset));
			request.AddParameter(nameof(offset), offset.Value);
		}
	}

	public async IAsyncEnumerable<Ohlc> GetCandles(string symbol, string exchange, string tf, long from, long to, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var url = CreateUrl("md/v2/history");

		var request =
				CreateRequest(Method.Get)
			.AddParameter(nameof(symbol), symbol)
			.AddParameter(nameof(exchange), exchange)
			.AddParameter(nameof(tf), tf)
			.AddParameter(nameof(from), from)
			.AddParameter(nameof(to), to)
			.AddParameter("format", "Slim")
		;

		while (true)
		{
			dynamic response = await MakeRequest<object>(url, request, cancellationToken);

			var candles = ((JToken)response.h).DeserializeObject<IEnumerable<Ohlc>>().OrderBy(c => c.Time).ToArray();

			if (candles.Length == 0)
				break;

			foreach (var item in candles)
				yield return item;

			if ((long?)response.next is not long next || next > to)
				break;

			request.Parameters.RemoveParameter(nameof(from));
			request.AddParameter(nameof(from), next);
		}
	}

	private Uri CreateUrl(string urlPart)
	{
		if (urlPart.IsEmpty())
			throw new ArgumentNullException(nameof(urlPart));

		return $"{_baseUrl}/{urlPart}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method) => new() { Method = method };

	private async ValueTask<T> MakeRequest<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		return ((JToken)obj).DeserializeObject<T>();
	}
}
