namespace StockSharp.KabuStation.Native;

internal sealed class KabuStationRestClient : BaseLogReceiver
{
	private sealed class RequestPacer : IDisposable
	{
		private readonly TimeSpan _interval;
		private readonly SemaphoreSlim _sync = new(1, 1);
		private DateTime _nextRequest;

		public RequestPacer(int requestsPerSecond)
		{
			_interval = TimeSpan.FromSeconds(1d / requestsPerSecond);
		}

		public async Task Wait(CancellationToken cancellationToken)
		{
			TimeSpan delay;
			await _sync.WaitAsync(cancellationToken);
			try
			{
				var now = DateTime.UtcNow;
				var slot = _nextRequest > now ? _nextRequest : now;
				delay = slot - now;
				_nextRequest = slot + _interval;
			}
			finally
			{
				_sync.Release();
			}
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
		}

		public void Dispose() => _sync.Dispose();
	}

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly HttpClient _http = new();
	private readonly string _password;
	private readonly Uri _root;
	private readonly int _maxAttempts;
	private readonly SemaphoreSlim _authenticationLock = new(1, 1);
	private readonly RequestPacer _orderPacer = new(5);
	private readonly RequestPacer _informationPacer = new(10);
	private string _token;

	public KabuStationRestClient(SecureString password, bool isDemo, int maxAttempts)
	{
		_password = password.ThrowIfEmpty(nameof(password)).UnSecure();
		_root = new($"http://localhost:{(isDemo ? 18081 : 18080)}/kabusapi/");
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public override string Name => nameof(KabuStation) + "_REST";

	public Task Connect(CancellationToken cancellationToken)
		=> GetToken(null, cancellationToken);

	public Task<KabuStationSymbol> GetSymbol(KabuStationSecurityInfo security, CancellationToken cancellationToken)
		=> Send<KabuStationSymbol>(HttpMethod.Get, $"symbol/{FormatSecurity(security)}?addinfo=true", null,
			false, true, cancellationToken);

	public Task<KabuStationBoard> GetBoard(KabuStationSecurityInfo security, CancellationToken cancellationToken)
		=> Send<KabuStationBoard>(HttpMethod.Get, $"board/{FormatSecurity(security)}", null,
			false, true, cancellationToken);

	public async Task<KabuStationRegisteredSymbol[]> Register(KabuStationSecurityInfo security,
		CancellationToken cancellationToken)
		=> (await Send<KabuStationRegistrationResponse>(HttpMethod.Put, "register", CreateRegistration(security),
			false, true, cancellationToken)).RegisteredSymbols ?? [];

	public async Task<KabuStationRegisteredSymbol[]> Unregister(KabuStationSecurityInfo security,
		CancellationToken cancellationToken)
		=> (await Send<KabuStationRegistrationResponse>(HttpMethod.Put, "unregister", CreateRegistration(security),
			false, true, cancellationToken)).RegisteredSymbols ?? [];

	public async Task<KabuStationOrder[]> GetOrders(DateTime? updatedFrom, CancellationToken cancellationToken)
	{
		var updated = updatedFrom is { } time
			? "&updtime=" + time.ToUniversalTime().AddHours(9).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
			: string.Empty;
		var result = new List<KabuStationOrder>();
		foreach (var (product, securityType) in new[]
		{
			(1, SecurityTypes.Stock),
			(2, SecurityTypes.Stock),
			(3, SecurityTypes.Future),
			(4, SecurityTypes.Option),
		})
		{
			var orders = await Send<KabuStationOrder[]>(HttpMethod.Get,
				$"orders?product={product}&details=true{updated}", null, false, true, cancellationToken);
			foreach (var order in orders ?? [])
			{
				order.SecurityType = securityType;
				result.Add(order);
			}
		}
		return [.. result.GroupBy(order => order.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.Last())];
	}

	public Task<KabuStationPosition[]> GetPositions(CancellationToken cancellationToken)
		=> Send<KabuStationPosition[]>(HttpMethod.Get, "positions?product=0&addinfo=true", null,
			false, true, cancellationToken);

	public Task<KabuStationCashWallet> GetCashWallet(CancellationToken cancellationToken)
		=> Send<KabuStationCashWallet>(HttpMethod.Get, "wallet/cash", null, false, true, cancellationToken);

	public Task<KabuStationMarginWallet> GetMarginWallet(CancellationToken cancellationToken)
		=> Send<KabuStationMarginWallet>(HttpMethod.Get, "wallet/margin", null, false, true, cancellationToken);

	public Task<KabuStationFutureWallet> GetFutureWallet(CancellationToken cancellationToken)
		=> Send<KabuStationFutureWallet>(HttpMethod.Get, "wallet/future", null, false, true, cancellationToken);

	public Task<KabuStationOptionWallet> GetOptionWallet(CancellationToken cancellationToken)
		=> Send<KabuStationOptionWallet>(HttpMethod.Get, "wallet/option", null, false, true, cancellationToken);

	public Task<KabuStationOrderResult> PlaceStockOrder(KabuStationStockOrderRequest request,
		CancellationToken cancellationToken)
		=> Send<KabuStationOrderResult>(HttpMethod.Post, "sendorder", request, true, false, cancellationToken);

	public Task<KabuStationOrderResult> PlaceFutureOrder(KabuStationDerivativeOrderRequest request,
		CancellationToken cancellationToken)
		=> Send<KabuStationOrderResult>(HttpMethod.Post, "sendorder/future", request, true, false, cancellationToken);

	public Task<KabuStationOrderResult> PlaceOptionOrder(KabuStationDerivativeOrderRequest request,
		CancellationToken cancellationToken)
		=> Send<KabuStationOrderResult>(HttpMethod.Post, "sendorder/option", request, true, false, cancellationToken);

	public Task<KabuStationOrderResult> CancelOrder(string orderId, CancellationToken cancellationToken)
		=> Send<KabuStationOrderResult>(HttpMethod.Put, "cancelorder",
			new KabuStationCancelOrderRequest { OrderId = orderId.ThrowIfEmpty(nameof(orderId)) },
			true, false, cancellationToken);

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_authenticationLock.Dispose();
		_orderPacer.Dispose();
		_informationPacer.Dispose();
		base.DisposeManaged();
	}

	private async Task<T> Send<T>(HttpMethod method, string path, object body, bool isOrder,
		bool isIdempotent, CancellationToken cancellationToken)
	{
		var token = await GetToken(null, cancellationToken);
		var authenticationRetried = false;
		var attempts = isIdempotent ? _maxAttempts : 1;

		for (var attempt = 1; ; attempt++)
		{
			await (isOrder ? _orderPacer : _informationPacer).Wait(cancellationToken);
			try
			{
				using var response = await SendRequest(method, path, body, token, cancellationToken);
				var content = await response.Content.ReadAsStringAsync(cancellationToken);
				if (response.StatusCode == HttpStatusCode.Unauthorized && !authenticationRetried)
				{
					token = await GetToken(token, cancellationToken);
					authenticationRetried = true;
					attempt--;
					continue;
				}
				if (!response.IsSuccessStatusCode)
					throw CreateException(response.StatusCode, content);

				return JsonConvert.DeserializeObject<T>(content, _jsonSettings)
					?? throw new InvalidDataException("kabu Station returned an empty response.");
			}
			catch (KabuStationApiException ex) when (attempt < attempts && IsTransient(ex.StatusCode))
			{
				await Task.Delay(GetRetryDelay(attempt), cancellationToken);
			}
			catch (HttpRequestException) when (attempt < attempts)
			{
				await Task.Delay(GetRetryDelay(attempt), cancellationToken);
			}
		}
	}

	private async Task<string> GetToken(string failedToken, CancellationToken cancellationToken)
	{
		if (failedToken == null && !_token.IsEmpty())
			return _token;

		await _authenticationLock.WaitAsync(cancellationToken);
		try
		{
			if (!_token.IsEmpty() && (failedToken == null || !_token.Equals(failedToken, StringComparison.Ordinal)))
				return _token;

			await _informationPacer.Wait(cancellationToken);
			using var response = await SendRequest(HttpMethod.Post, "token",
				new KabuStationTokenRequest { ApiPassword = _password }, null, cancellationToken);
			var content = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw CreateException(response.StatusCode, content);
			var token = JsonConvert.DeserializeObject<KabuStationTokenResponse>(content, _jsonSettings)
				?? throw new InvalidDataException("kabu Station returned an empty token response.");
			if (token.ResultCode != 0)
				throw new InvalidOperationException($"kabu Station token request failed with result {token.ResultCode}.");
			return _token = token.Token.ThrowIfEmpty(nameof(token.Token));
		}
		finally
		{
			_authenticationLock.Release();
		}
	}

	private async Task<HttpResponseMessage> SendRequest(HttpMethod method, string path, object body,
		string token, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, new Uri(_root, path));
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		if (!token.IsEmpty())
			request.Headers.TryAddWithoutValidation("X-API-KEY", token);
		if (body != null)
			request.Content = new StringContent(JsonConvert.SerializeObject(body, _jsonSettings), Encoding.UTF8, "application/json");
		return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
	}

	private static KabuStationApiException CreateException(HttpStatusCode statusCode, string content)
	{
		KabuStationErrorResponse error = null;
		try { error = JsonConvert.DeserializeObject<KabuStationErrorResponse>(content, _jsonSettings); }
		catch (JsonException) { }
		var message = error?.Message.IsEmpty(content).IsEmpty(statusCode.ToString());
		return new(statusCode, error?.Code, $"kabu Station API error{(error?.Code is { } code ? $" {code}" : string.Empty)}: {message}");
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or
			HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or
			HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

	private static TimeSpan GetRetryDelay(int attempt)
		=> TimeSpan.FromMilliseconds(Math.Min(5000, 250 * Math.Pow(2, attempt - 1)));

	private static string FormatSecurity(KabuStationSecurityInfo security)
		=> Uri.EscapeDataString($"{security.Symbol.ThrowIfEmpty(nameof(security.Symbol))}@{security.Exchange}");

	private static KabuStationRegistrationRequest CreateRegistration(KabuStationSecurityInfo security)
		=> new()
		{
			Symbols =
			[
				new() { Symbol = security.Symbol, Exchange = security.Exchange },
			],
		};
}
