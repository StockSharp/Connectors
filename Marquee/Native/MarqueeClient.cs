namespace StockSharp.Marquee.Native;

sealed class MarqueeClient : BaseLogReceiver, IDisposable
{
	private const string _defaultScopes =
		"read_content read_product_data read_financial_data read_user_profile";

	private static readonly string[] _assetFields =
	[
		"id", "assetClass", "type", "name", "shortName", "active", "currency",
		"exchange", "listed", "liveDate", "rank", "xref",
	];

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
	};

	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
	private readonly SemaphoreSlim _authLock = new(1, 1);
	private readonly string _clientId;
	private readonly string _clientSecret;
	private readonly Uri _apiAddress;
	private readonly Uri _authAddress;
	private string _accessToken;
	private DateTime _tokenExpires;

	public MarqueeClient(string clientId, string clientSecret, bool isDemo)
	{
		_clientId = clientId.ThrowIfEmpty(nameof(clientId));
		_clientSecret = clientSecret.ThrowIfEmpty(nameof(clientSecret));
		_apiAddress = new(isDemo ? "https://api.marquee-qa.gs.com/v1/" : "https://api.gs.com/v1/");
		_authAddress = new(isDemo
			? "https://idfs-qa.gs.com/as/token.oauth2"
			: "https://idfs.gs.com/as/token.oauth2");
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public Task Authenticate(CancellationToken cancellationToken)
		=> EnsureToken(true, cancellationToken);

	public async IAsyncEnumerable<MarqueeAsset> LookupAssets(string ticker,
		string[] assetClasses, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		string scrollId = null;
		do
		{
			var response = await Post<MarqueeAssetResponse, MarqueeAssetQuery>("assets/query", new()
			{
				Where = new()
				{
					Ticker = ticker,
					AssetClass = assetClasses,
					Active = true,
				},
				Scroll = "1m",
				ScrollId = scrollId,
				Fields = _assetFields,
				Limit = 1000,
			}, cancellationToken);

			var results = response?.Results ?? [];
			foreach (var asset in results)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (asset != null && !asset.Id.IsEmpty())
					yield return asset;
			}

			var next = response?.ScrollId;
			if (results.Length == 0 || next.IsEmpty() || next == scrollId)
				yield break;
			scrollId = next;
		}
		while (true);
	}

	public async Task<MarqueeAvailabilityResponse> GetAvailability(string assetId,
		CancellationToken cancellationToken)
	{
		var response = await Get<MarqueeAvailabilityResponse>(
			$"data/measures/{Uri.EscapeDataString(assetId.ThrowIfEmpty(nameof(assetId)))}/availability",
			cancellationToken);
		if (response?.ErrorMessages?.Length > 0)
			throw CreateApiError(response.RequestId, response.ErrorMessages);
		return response ?? new();
	}

	public async Task<MarqueeDataRow[]> GetLastData(string datasetId, string assetId,
		string[] fields, bool realTime, CancellationToken cancellationToken)
	{
		var now = DateTime.UtcNow;
		var response = await Post<MarqueeDataResponse, MarqueeDataQuery>(
			$"data/{Uri.EscapeDataString(datasetId.ThrowIfEmpty(nameof(datasetId)))}/last/query", new()
			{
				Where = new() { AssetId = [assetId.ThrowIfEmpty(nameof(assetId))] },
				EndDate = realTime ? null : now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				EndTime = realTime ? now.ToString("O", CultureInfo.InvariantCulture) : null,
				Fields = fields,
			}, cancellationToken);
		if (response?.ErrorMessages?.Length > 0)
			throw CreateApiError(response.RequestId, response.ErrorMessages);
		return response?.Data ?? [];
	}

	public async IAsyncEnumerable<MarqueeDataRow> GetData(string datasetId, string assetId,
		string[] fields, DateTime from, DateTime to,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var page = 0;
		var totalPages = 1;
		while (page < totalPages)
		{
			var response = await Post<MarqueeDataResponse, MarqueeDataQuery>(
				$"data/{Uri.EscapeDataString(datasetId.ThrowIfEmpty(nameof(datasetId)))}/query", new()
				{
					Where = new() { AssetId = [assetId.ThrowIfEmpty(nameof(assetId))] },
					StartDate = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
					EndDate = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
					Page = page,
					PageSize = 10000,
					Fields = fields,
				}, cancellationToken);

			if (response?.ErrorMessages?.Length > 0)
				throw CreateApiError(response.RequestId, response.ErrorMessages);
			foreach (var row in response?.Data ?? [])
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (row != null)
					yield return row;
			}

			totalPages = Math.Max(1, response?.TotalPages ?? 1);
			page++;
		}
	}

	private async Task<TResponse> Get<TResponse>(string path,
		CancellationToken cancellationToken)
		where TResponse : class
		=> await Send<TResponse>(HttpMethod.Get, path, null, cancellationToken);

	private async Task<TResponse> Post<TResponse, TRequest>(string path, TRequest payload,
		CancellationToken cancellationToken)
		where TResponse : class
		where TRequest : class
		=> await Send<TResponse>(HttpMethod.Post, path,
			() => new StringContent(JsonConvert.SerializeObject(payload, _jsonSettings),
				Encoding.UTF8, "application/json"), cancellationToken);

	private async Task<TResponse> Send<TResponse>(HttpMethod method, string path,
		Func<HttpContent> contentFactory, CancellationToken cancellationToken)
		where TResponse : class
	{
		for (var attempt = 0; attempt < 2; attempt++)
		{
			await EnsureToken(false, cancellationToken);
			using var request = new HttpRequestMessage(method, new Uri(_apiAddress, path));
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
			request.Headers.TryAddWithoutValidation("X-Application", "StockSharp");
			request.Content = contentFactory?.Invoke();

			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseContentRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
			{
				InvalidateToken();
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body, request.RequestUri);

			var result = JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings);
			return result ?? throw new InvalidOperationException(
				$"Goldman Sachs Marquee returned an empty response for '{path}'.");
		}

		throw new InvalidOperationException("Goldman Sachs Marquee authentication failed after token refresh.");
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

			using var request = new HttpRequestMessage(HttpMethod.Post, _authAddress)
			{
				Content = new MarqueeTokenRequest
				{
					ClientId = _clientId,
					ClientSecret = _clientSecret,
					Scope = _defaultScopes,
				}.ToContent(),
			};
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseContentRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body, _authAddress);

			var token = JsonConvert.DeserializeObject<MarqueeTokenResponse>(body, _jsonSettings)
				?? throw new InvalidOperationException("Goldman Sachs returned an empty OAuth response.");
			_accessToken = token.AccessToken.ThrowIfEmpty(nameof(MarqueeTokenResponse.AccessToken));
			var lifetime = TimeSpan.FromSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 1800);
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

	private static Exception CreateApiError(HttpStatusCode statusCode, string body, Uri address)
	{
		MarqueeErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<MarqueeErrorResponse>(body, _jsonSettings);
		}
		catch (JsonException)
		{
		}

		var message = error?.GetMessage();
		if (message.IsEmpty())
			message = body?.Length > 1000 ? body[..1000] : body;
		return new InvalidOperationException(
			$"Goldman Sachs Marquee request '{address}' failed ({(int)statusCode} {statusCode}): {message}");
	}

	private static Exception CreateApiError(string requestId, IEnumerable<string> errors)
		=> new InvalidOperationException(
			$"Goldman Sachs Marquee request '{requestId}' failed: {errors.JoinComma()}");

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_authLock.Dispose();
		base.DisposeManaged();
	}
}
