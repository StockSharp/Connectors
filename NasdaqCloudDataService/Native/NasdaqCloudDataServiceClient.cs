namespace StockSharp.NasdaqCloudDataService.Native;

sealed class NasdaqCloudApiException : InvalidOperationException
{
	public NasdaqCloudApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class NasdaqCloudDataServiceClient : BaseLogReceiver, IDisposable
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
	};

	private readonly Uri _address;
	private readonly string _clientId;
	private readonly string _clientSecret;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
	private readonly SemaphoreSlim _authLock = new(1, 1);
	private string _accessToken;
	private DateTime _tokenExpires;

	public NasdaqCloudDataServiceClient(Uri address, string clientId, string clientSecret)
	{
		_address = EnsureTrailingSlash(address ?? throw new ArgumentNullException(nameof(address)));
		_clientId = clientId.ThrowIfEmpty(nameof(clientId));
		_clientSecret = clientSecret.ThrowIfEmpty(nameof(clientSecret));
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"User-Agent", "StockSharp-NasdaqCloudDataService/1.0");
	}

	public Task Authenticate(CancellationToken cancellationToken)
		=> EnsureToken(true, cancellationToken);

	public async Task<NasdaqCloudEquity[]> GetSymbols(CancellationToken cancellationToken)
		=> await Get<NasdaqCloudEquity[]>("v1/reference/symbols", cancellationToken) ?? [];

	public Task<NasdaqCloudEquity> GetSymbol(string symbol, CancellationToken cancellationToken)
		=> Get<NasdaqCloudEquity>($"v1/reference/symbol/{Escape(symbol)}", cancellationToken);

	public async Task<NasdaqCloudIndex[]> GetIndexes(CancellationToken cancellationToken)
		=> await Get<NasdaqCloudIndex[]>("v1/reference/indexes", cancellationToken) ?? [];

	public Task<NasdaqCloudIndex> GetIndex(string instrument,
		CancellationToken cancellationToken)
		=> Get<NasdaqCloudIndex>($"v1/reference/index/{Escape(instrument)}", cancellationToken);

	public async Task<NasdaqCloudEtp[]> GetEtps(CancellationToken cancellationToken)
		=> await Get<NasdaqCloudEtp[]>("v1/reference/etps", cancellationToken) ?? [];

	public Task<NasdaqCloudEtp> GetEtp(string symbol, CancellationToken cancellationToken)
		=> Get<NasdaqCloudEtp>($"v1/reference/etp/{Escape(symbol)}", cancellationToken);

	public async Task<NasdaqCloudOptionContract[]> GetOptionContracts(string underlying,
		CancellationToken cancellationToken)
	{
		var response = await Get<NasdaqCloudOptionContractsResponse>(
			$"v1/reference/contracts/{Escape(underlying)}", cancellationToken);
		return response?.Contracts ?? [];
	}

	public async Task<NasdaqCloudLastSale[]> GetLastSales(NasdaqCloudSources source,
		NasdaqCloudOffsets offset, string symbols, CancellationToken cancellationToken)
		=> await Get<NasdaqCloudLastSale[]>(MarketPath(source, offset,
			$"equities/lastsale/{EscapeSymbols(symbols)}"), cancellationToken) ?? [];

	public async Task<NasdaqCloudLastQuote[]> GetLastQuotes(NasdaqCloudSources source,
		NasdaqCloudOffsets offset, string symbols, CancellationToken cancellationToken)
		=> await Get<NasdaqCloudLastQuote[]>(MarketPath(source, offset,
			$"equities/lastquote/{EscapeSymbols(symbols)}"), cancellationToken) ?? [];

	public async Task<NasdaqCloudEquitySnapshot[]> GetEquitySnapshots(
		NasdaqCloudSources source, NasdaqCloudOffsets offset, string symbols,
		CancellationToken cancellationToken)
		=> await Get<NasdaqCloudEquitySnapshot[]>(MarketPath(source, offset,
			$"equities/snapshot/{EscapeSymbols(symbols)}"), cancellationToken) ?? [];

	public async Task<NasdaqCloudBar[]> GetBars(NasdaqCloudSources source,
		NasdaqCloudOffsets offset, string symbol, TimeSpan timeFrame,
		NasdaqCloudBarRanges range, DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		var precision = timeFrame.ToNasdaqCloudPrecision();
		if (source is NasdaqCloudSources.Nasdaq or NasdaqCloudSources.Cqt)
		{
			var response = await Get<NasdaqCloudBarsResponse>(
				$"v2/{source.ToApi()}/{offset.ToApi()}/equities/bars/{Escape(symbol)}/{precision}/false/{range.ToApi()}",
				cancellationToken);
			return response?.Series?.FirstOrDefault(item =>
				item.Symbol.EqualsIgnoreCase(symbol))?.Bars ?? [];
		}

		if (timeFrame is not { TotalMinutes: 1 or 5 })
			throw new NotSupportedException(
				$"Nasdaq Cloud {source} bars support only 1-minute and 5-minute precision.");
		return await Get<NasdaqCloudBar[]>(
			$"v1/{source.ToApi()}/{offset.ToApi()}/equities/bars/{Escape(symbol)}/{precision}/{from.ToEasternRoute()}/{to.ToEasternRoute()}",
			cancellationToken) ?? [];
	}

	public async Task<NasdaqCloudIndexValue[]> GetIndexValues(NasdaqCloudOffsets offset,
		string instruments, CancellationToken cancellationToken)
		=> await Get<NasdaqCloudIndexValue[]>(
			$"v1/nasdaq/{offset.ToApi()}/indexes/value/{EscapeSymbols(instruments)}",
			cancellationToken) ?? [];

	public async Task<NasdaqCloudIndexSnapshot[]> GetIndexSnapshots(NasdaqCloudOffsets offset,
		string instruments, CancellationToken cancellationToken)
		=> await Get<NasdaqCloudIndexSnapshot[]>(
			$"v1/nasdaq/{offset.ToApi()}/indexes/snapshot/{EscapeSymbols(instruments)}",
			cancellationToken) ?? [];

	public async Task<NasdaqCloudEtpValue[]> GetEtpValues(NasdaqCloudOffsets offset,
		string symbols, CancellationToken cancellationToken)
		=> await Get<NasdaqCloudEtpValue[]>(
			$"v1/nasdaq/{offset.ToApi()}/etps/value/{EscapeSymbols(symbols)}",
			cancellationToken) ?? [];

	public async Task<NasdaqCloudEtpSnapshot[]> GetEtpSnapshots(NasdaqCloudOffsets offset,
		string symbols, CancellationToken cancellationToken)
		=> await Get<NasdaqCloudEtpSnapshot[]>(
			$"v1/nasdaq/{offset.ToApi()}/etps/snapshot/{EscapeSymbols(symbols)}",
			cancellationToken) ?? [];

	public async Task<NasdaqCloudOptionChainItem[]> GetOptionChain(NasdaqCloudOffsets offset,
		string underlying, CancellationToken cancellationToken)
		=> await Get<NasdaqCloudOptionChainItem[]>(
			$"v1/nasdaq/{offset.ToApi()}/options/chain/{Escape(underlying)}",
			cancellationToken) ?? [];

	public async Task<NasdaqCloudOptionPrice[]> GetOptionPrices(NasdaqCloudOffsets offset,
		string identifier, CancellationToken cancellationToken)
		=> await Get<NasdaqCloudOptionPrice[]>(
			$"v1/nasdaq/{offset.ToApi()}/options/prices/{Escape(identifier)}",
			cancellationToken) ?? [];

	public async Task<NasdaqCloudOptionGreeks[]> GetOptionGreeks(NasdaqCloudOffsets offset,
		string identifier, CancellationToken cancellationToken)
		=> await Get<NasdaqCloudOptionGreeks[]>(
			$"v1/nasdaq/{offset.ToApi()}/greeksandvolsus/{Escape(identifier)}",
			cancellationToken) ?? [];

	private static string MarketPath(NasdaqCloudSources source, NasdaqCloudOffsets offset,
		string relative)
		=> $"v1/{source.ToApi()}/{offset.ToApi()}/{relative}";

	private async Task<T> Get<T>(string relative, CancellationToken cancellationToken)
		where T : class
	{
		var address = new Uri(_address, relative);
		for (var attempt = 0; attempt < 4; attempt++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, address);
			await SetAuthorization(request, cancellationToken);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseContentRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);

			if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
			{
				InvalidateToken();
				continue;
			}
			if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
				(int)response.StatusCode >= 500) && attempt < 3)
			{
				await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
				continue;
			}
			if (response.StatusCode == HttpStatusCode.NoContent)
				return null;
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body, address);
			if (body.IsEmpty())
				return null;

			return JsonConvert.DeserializeObject<T>(body, _jsonSettings)
				?? throw new InvalidOperationException(
					$"Nasdaq Cloud returned an empty response for '{address}'.");
		}

		throw new InvalidOperationException(
			$"Nasdaq Cloud request '{address}' exhausted its retry limit.");
	}

	private async Task SetAuthorization(HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		await EnsureToken(false, cancellationToken);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
	}

	private async Task EnsureToken(bool force, CancellationToken cancellationToken)
	{
		if (!force && !_accessToken.IsEmpty() && DateTime.UtcNow < _tokenExpires)
			return;

		await _authLock.WaitAsync(cancellationToken);
		try
		{
			if (!force && !_accessToken.IsEmpty() && DateTime.UtcNow < _tokenExpires)
				return;

			var payload = JsonConvert.SerializeObject(new NasdaqCloudAuthenticationRequest
			{
				ClientId = _clientId,
				ClientSecret = _clientSecret,
			}, _jsonSettings);
			using var request = new HttpRequestMessage(HttpMethod.Post,
				new Uri(_address, "v1/auth/token"))
			{
				Content = new StringContent(payload, Encoding.UTF8, "application/json"),
			};
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseContentRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body, request.RequestUri);

			var token = JsonConvert.DeserializeObject<NasdaqCloudAuthenticationResponse>(
				body, _jsonSettings) ?? throw new InvalidOperationException(
					"Nasdaq Cloud returned an empty authentication response.");
			_accessToken = token.AccessToken.ThrowIfEmpty(
				nameof(NasdaqCloudAuthenticationResponse.AccessToken));
			var lifetime = TimeSpan.FromSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 3600);
			var safety = TimeSpan.FromSeconds(Math.Min(60, lifetime.TotalSeconds / 2));
			_tokenExpires = DateTime.UtcNow + lifetime - safety;
		}
		finally
		{
			_authLock.Release();
		}
	}

	private void InvalidateToken()
	{
		_accessToken = null;
		_tokenExpires = default;
	}

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		var delay = response.Headers.RetryAfter?.Delta;
		if (delay == null && response.Headers.RetryAfter?.Date != null)
			delay = response.Headers.RetryAfter.Date.Value.UtcDateTime - DateTime.UtcNow;
		if (delay != null && delay.Value > TimeSpan.Zero)
			return delay.Value > TimeSpan.FromSeconds(30)
				? TimeSpan.FromSeconds(30)
				: delay.Value;
		return TimeSpan.FromSeconds(Math.Pow(2, attempt));
	}

	private static NasdaqCloudApiException CreateApiError(HttpStatusCode statusCode,
		string body, Uri address)
	{
		NasdaqCloudErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<NasdaqCloudErrorResponse>(body, _jsonSettings);
		}
		catch (JsonException)
		{
		}
		var details = error?.GetMessage();
		if (details.IsEmpty())
			details = body?.Length > 1000 ? body[..1000] : body;
		return new NasdaqCloudApiException(statusCode,
			$"Nasdaq Cloud request '{address}' failed ({(int)statusCode} {statusCode}): {details}");
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private static string EscapeSymbols(string value)
		=> value.ThrowIfEmpty(nameof(value)).Split(',')
			.Select(symbol => Escape(symbol.Trim())).Join(",");

	private static Uri EnsureTrailingSlash(Uri address)
	{
		var value = address.AbsoluteUri;
		return value.EndsWith('/') ? address : new Uri(value + "/");
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_authLock.Dispose();
		base.DisposeManaged();
	}
}
