namespace StockSharp.Bitmart.Native.Futures;

using Newtonsoft.Json.Serialization;

using StockSharp.Bitmart.Native.Futures.Model;

class HttpClient : BaseLogReceiver
{
	private class BitmartEndPoint
	{
		private readonly struct LimitHeaders
		{
			public LimitHeaders(int limitCounter, int limitMax, int limitReset)
			{
				LimitCounter = limitCounter;
				LimitMax = limitMax;
				LimitReset = limitReset;
			}

			public override string ToString() => $"LimitHeaders(counter={LimitCounter}, max={LimitMax}, reset={LimitReset})";

			public int LimitCounter { get; }
			public int LimitMax { get; }
			public int LimitReset { get; }
		}

		private readonly List<DateTime> _callTimes = [];
		private LimitHeaders? _limits;

		private Method RequestMethod { get; }
		private string EndPoint { get; }
		private Authenticator.AuthKind Auth { get; }

		private BitmartEndPoint(string endPoint, Authenticator.AuthKind auth = Authenticator.AuthKind.None, Method reqMethod = Method.Get)
		{
			EndPoint = endPoint;
			Auth = auth;
			RequestMethod = reqMethod;
		}

		public static readonly BitmartEndPoint Assets = new("contract/private/assets-detail", Authenticator.AuthKind.Keyed);
		public static readonly BitmartEndPoint Positions = new("contract/private/position-v2", Authenticator.AuthKind.Keyed);

		public static readonly BitmartEndPoint GetContracts = new("contract/public/details");
		public static readonly BitmartEndPoint GetKLine = new("contract/public/kline");

		public static readonly BitmartEndPoint SubmitOrder = new("contract/private/submit-order", Authenticator.AuthKind.Signed, Method.Post);
		public static readonly BitmartEndPoint CancelOrder = new("contract/private/cancel-order", Authenticator.AuthKind.Signed, Method.Post);
		public static readonly BitmartEndPoint CancelOrders = new("contract/private/cancel-orders", Authenticator.AuthKind.Signed, Method.Post);
		public static readonly BitmartEndPoint GetOpenOrders = new("contract/private/get-open-orders", Authenticator.AuthKind.Keyed);

		private static void CheckResult(JToken obj)
		{
			if (obj is not JObject result)
				throw new InvalidOperationException($"unexpected result type '{obj?.GetType().FullName}', obj='{obj}'");

			var code = (string)result["code"];
			if (code != "1000")
				throw new InvalidOperationException((string)result["message"] ?? "unknown error, result is " + result);
		}

		private async Task<object> ExecuteRequest(RestRequest request, Uri url, Action<string, object[]> logVerbose, CancellationToken cancellationToken)
		{
			var asm = GetType().Assembly;

			var client = new RestClient(url);

			logVerbose?.Invoke("Request '{0}' Args '{1}'.", [url, request.Parameters.ToQueryString(false)]);
			var response = await client.ExecuteAsync(request, cancellationToken);

			var content = response.Content;
			logVerbose?.Invoke("Response '{0}' (code {1}).", [content, response.StatusCode]);

			if (response.StatusCode != HttpStatusCode.OK)
				throw new InvalidOperationException(content.IsEmpty() ? (response.ErrorMessage.IsEmpty() ? response.StatusDescription : response.ErrorMessage) : content);

			if (content.IsEmpty())
				throw new InvalidOperationException();

			int limRemaining, limMax, limReset;
			limRemaining = limMax = limReset = 0;

			foreach (var h in response.Headers)
			{
				if (h.Name.EqualsIgnoreCase("X-BM-RateLimit-Remaining"))
					int.TryParse(h.Value, out limRemaining);
				else if (h.Name.EqualsIgnoreCase("X-BM-RateLimit-Limit"))
					int.TryParse(h.Value, out limMax);
				else if (h.Name.EqualsIgnoreCase("X-BM-RateLimit-Reset"))
					int.TryParse(h.Value, out limReset);
			}

			_limits = limRemaining > 0 && limMax > 0 && limReset > 0 ? new LimitHeaders(limRemaining, limMax, limReset) : null;

			return content.DeserializeObject<object>();
		}

		private void AddCallTime(DateTime callTime)
		{
			if (_limits == null)
			{
				_callTimes.Clear();
				return;
			}

			_callTimes.Add(callTime);

			if (_callTimes.Count > _limits.Value.LimitCounter)
				_callTimes.RemoveRange(0, _callTimes.Count - _limits.Value.LimitCounter);
		}

		private async ValueTask CheckWaitBeforeRequest(ILogReceiver log, CancellationToken cancellationToken)
		{
			if (_limits == null || _callTimes.Count < _limits.Value.LimitMax)
				return;

			var resetTime = _callTimes[0] + TimeSpan.FromSeconds(_limits.Value.LimitReset);
			var timeLeft = resetTime - DateTime.UtcNow;

			if (timeLeft > TimeSpan.Zero)
			{
				log.AddWarningLog("rate limit exceeded. limits={0}, waiting for {1}", _limits.Value.ToString(), timeLeft);
				await timeLeft.Delay(cancellationToken);
			}
		}

		private static readonly JsonSerializerSettings _jsonSerializerSettings = new()
		{
			FloatParseHandling = FloatParseHandling.Decimal,
			NullValueHandling = NullValueHandling.Ignore,
			ContractResolver = new DefaultContractResolver { NamingStrategy = new DefaultNamingStrategy() },
			Formatting = Formatting.None
		};

		public async Task<T> MakeRequest<T>(HttpClient parent, Action<RestRequest> prepare, Action<JToken> checkResult, Func<JToken, JToken> getData, CancellationToken cancellationToken)
		{
			if (parent is null) throw new ArgumentNullException(nameof(parent));
			if (prepare is null) throw new ArgumentNullException(nameof(prepare));
			if (getData is null) throw new ArgumentNullException(nameof(getData));

			var request = new RestRequest((string)null, RequestMethod);
			prepare(request);

			var url = new Url($"{parent._baseUrl}/{EndPoint}");

			await CheckWaitBeforeRequest(parent, cancellationToken);

			if (Auth != Authenticator.AuthKind.None)
			{
				var requestData = request
					.Parameters
					.Where(p => p.Type == ParameterType.GetOrPost)
					.ToDictionary(p => p.Name, p => p.Value);

				request.RemoveWhere(p => p.Type == ParameterType.GetOrPost);

				string dataToSign;

				if (request.Method == Method.Get)
				{
					foreach (var pair in requestData)
						url.QueryString[pair.Key] = pair.Value;

					dataToSign = url.PathAndQuery;
				}
				else
				{
					var body = JsonConvert.SerializeObject(requestData, _jsonSerializerSettings);
					request.AddBodyAsStr(body);
					dataToSign = body;
				}

				var auth = parent._authenticator;
				var (key, ts, sign) = auth.Sign(dataToSign);

				request
					.AddHeader("X-BM-KEY", key)
					.AddHeader("X-BM-SIGN", sign)
					.AddHeader("X-BM-TIMESTAMP", ts);
			}

			var obj = await ExecuteRequest(request, url, parent.AddVerboseLog, cancellationToken) as JToken;

			AddCallTime(DateTime.UtcNow);

			checkResult ??= CheckResult;
			checkResult(obj);

			// ReSharper disable once PossibleNullReferenceException
			var data = getData(obj["data"]) ?? throw new InvalidOperationException("no data returned");

			return data.DeserializeObject<T>();
		}
	}

	private readonly Authenticator _authenticator;
	private readonly string _baseUrl;

	public HttpClient(string address, Authenticator authenticator)
	{
		_baseUrl = $"https://{address.ThrowIfEmpty(nameof(address))}";
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	}

	// to get readable name after obfuscation
	public override string Name => $"{nameof(Bitmart)}_{nameof(Futures)}_{nameof(HttpClient)}";

	public Task<IEnumerable<RestOhlc>> GetCandles(string symbol, int step, DateTime start, DateTime end, CancellationToken cancellationToken)
		=> BitmartEndPoint.GetKLine.MakeRequest<IEnumerable<RestOhlc>>(this, req =>
		{
			req.AddParameter("symbol", symbol);
			req.AddParameter("step", step);
			req.AddParameter("start_time", (long)start.ToUnix());
			req.AddParameter("end_time", (long)end.ToUnix());

		}, null, data => data, cancellationToken);

	public Task<IEnumerable<Contract>> GetContracts(CancellationToken cancellationToken)
		=> BitmartEndPoint.GetContracts.MakeRequest<IEnumerable<Contract>>(this, r => { }, null, data => data["symbols"], cancellationToken);

	public Task<long> SubmitOrder(string symbol, int side, string type, int leverage, string openType, int? mode, decimal? price, decimal size, decimal? activationPrice, decimal? callbackRate, long clientOrderId, CancellationToken cancellationToken)
		=> BitmartEndPoint.SubmitOrder.MakeRequest<long>(this, req =>
		{
			req.AddParameter("symbol", symbol);
			req.AddParameter("side", side);
			req.AddParameter("type", type);
			req.AddParameter("leverage", leverage);
			req.AddParameter("open_type", openType);

			if (mode is not null)
				req.AddParameter("mode", mode.Value);

			if (price is not null)
				req.AddParameter("price", price.Value);

			req.AddParameter("size", size);

			if (activationPrice is not null)
				req.AddParameter("activation_price", activationPrice.Value);

			if (callbackRate is not null)
				req.AddParameter("callback_rate", callbackRate.Value);

			req.AddParameter("client_order_id", clientOrderId);

		}, null, data => data["order_id"], cancellationToken);

	public Task CancelOrder(string symbol, long orderId, CancellationToken cancellationToken)
		=> BitmartEndPoint.CancelOrder.MakeRequest<object>(this, req =>
		{
			req.AddParameter("symbol", symbol);
			req.AddParameter("order_id", orderId);

		}, null, data => data, cancellationToken);

	public Task CancelOrders(string symbol, CancellationToken cancellationToken)
		=> BitmartEndPoint.CancelOrders.MakeRequest<object>(this, req =>
		{
			req.AddParameter("symbol", symbol);

		}, null, data => data, cancellationToken);

	public Task<IEnumerable<Asset>> GetAssets(CancellationToken cancellationToken)
		=> BitmartEndPoint.Assets.MakeRequest<IEnumerable<Asset>>(this, req => { }, null, data => data, cancellationToken);

	public Task<IEnumerable<Position>> GetPositions(string symbol, CancellationToken cancellationToken)
		=> BitmartEndPoint.Positions.MakeRequest<IEnumerable<Position>>(this, req =>
		{
			if (!symbol.IsEmpty())
				req.AddParameter("symbol", symbol);

		}, null, data => data, cancellationToken);

	public Task<IEnumerable<Order>> GetOpenOrders(string symbol, CancellationToken cancellationToken)
		=> BitmartEndPoint.GetOpenOrders.MakeRequest<IEnumerable<Order>>(this, req =>
		{
			if (!symbol.IsEmpty())
				req.AddParameter("symbol", symbol);

		}, null, data => data, cancellationToken);
}
