namespace StockSharp.Alor.Native;

using System.Dynamic;
using System.Runtime.CompilerServices;

using Newtonsoft.Json.Linq;

using RestSharp;

class HttpClient : BaseLogReceiver
{
	private readonly string _baseUrl;

	private readonly SecureString _token;

	public HttpClient(string domain, SecureString token)
	{
		_baseUrl = $"https://{domain}";
		_token = token;
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Alor) + "_" + nameof(HttpClient);

	public async IAsyncEnumerable<Security> GetSecurities(
		string query = default,
		int? limit = default,
		int? offset = default,
		string sector = default,
		string cficode = default,
		string exchange = default,
		string instrumentGroup = default,
		bool? includeNonBaseBoards = default,
		string format = default,
		bool includeOptions = default,
		[EnumeratorCancellation]CancellationToken cancellationToken = default)
	{
		var url = CreateUrl("md/v2/securities");
		var request = CreateRequest(Method.Get);

		if (!query.IsEmpty()) request.AddParameter(nameof(query), query);
		if (limit.HasValue) request.AddParameter(nameof(limit), limit.Value);
		if (offset.HasValue) request.AddParameter(nameof(offset), offset.Value);
		if (!sector.IsEmpty()) request.AddParameter(nameof(sector), sector);
		if (!cficode.IsEmpty()) request.AddParameter(nameof(cficode), cficode);
		if (!exchange.IsEmpty()) request.AddParameter(nameof(exchange), exchange);
		if (!instrumentGroup.IsEmpty()) request.AddParameter(nameof(instrumentGroup), instrumentGroup);
		if (includeNonBaseBoards.HasValue) request.AddParameter(nameof(includeNonBaseBoards), includeNonBaseBoards.Value.ToString().ToLower());
		if (!format.IsEmpty()) request.AddParameter(nameof(format), format);

		request = ApplySecret(request);

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

	public async IAsyncEnumerable<Ohlc> GetCandles(string symbol, string exchange, string tf, long from, long to, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		var url = CreateUrl($"md/v2/history");

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

	public ValueTask<IEnumerable<Position>> GetPositions(string exchange, string portfolio, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		return MakeRequest<IEnumerable<Position>>(CreateUrl($"md/v2/clients/{exchange}/{portfolio}/positions"), ApplySecret(request), cancellationToken);
	}

	public ValueTask<OrderResponse> RegisterOrder(long transactionId, string symbol, string exchange, string portfolio, string type, string side, decimal price, decimal quantity, string condition, decimal? triggerPrice, long? stopEndUnixTime, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);
		AddAlorHeader(request, portfolio, transactionId);

		dynamic body = new ExpandoObject();

		body.side = side;
		body.type = type;
		body.quantity = quantity;
		body.price = price;
		body.instrument = new { symbol, exchange };
		body.user = new { portfolio };

		if (!condition.IsEmpty())
			body.condition = condition;

		if (triggerPrice is not null)
			body.triggerPrice = triggerPrice.Value;

		if (stopEndUnixTime is not null)
			body.stopEndUnixTime = stopEndUnixTime.Value;

		request.AddJsonBody((object)body);

		return MakeRequest<OrderResponse>(CreateOrderUrl(type), ApplySecret(request), cancellationToken);
	}

	public ValueTask<OrderResponse> ChangeOrder(long transactionId, long id, string symbol, string exchange, string portfolio, string type, string side, decimal price, decimal quantity, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Put);
		AddAlorHeader(request, portfolio, transactionId);

		request.AddJsonBody(new
		{
			side,
			type,
			id,
			quantity,
			price,
			instrument = new { symbol, exchange },
			user = new { portfolio },
		});

		return MakeRequest<OrderResponse>(CreateOrderUrl($"{type}/{id}"), ApplySecret(request), cancellationToken);
	}

	public async ValueTask CancelOrder(long orderId, string portfolio, string exchange, bool stop, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Delete);

		dynamic response = await MakeRequest<object>(CreateOrderUrl($"{orderId}/?portfolio={portfolio}&exchange={exchange}&stop={stop}"), ApplySecret(request), cancellationToken);

		if (response is string str)
		{
			if (str.EqualsIgnoreCase("success"))
				return;

			throw new InvalidOperationException(str);
		}
		else
		{
			throw new InvalidOperationException((string)response.message);
		}
	}

	private Uri CreateOrderUrl(string urlPart)
		=> CreateUrl($"commandapi/warptrans/TRADE/v2/client/orders/actions/{urlPart}");

	private Uri CreateUrl(string urlPart)
	{
		if (urlPart.IsEmpty())
			throw new ArgumentNullException(nameof(urlPart));

		return $"{_baseUrl}/{urlPart}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method) => new() { Method = method };

	private static RestRequest AddAlorHeader(RestRequest request, string portfolio, long transactionId)
	{
		if (request is null)
			throw new ArgumentNullException(nameof(request));

		return request.AddHeader("X-ALOR-REQID", $"{portfolio}:{transactionId}");
	}

	private RestRequest ApplySecret(RestRequest request)
	{
		if (request is null)
			throw new ArgumentNullException(nameof(request));

		return request.SetBearer(_token);
	}

	private async ValueTask<T> MakeRequest<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		return ((JToken)obj).DeserializeObject<T>();
	}
}