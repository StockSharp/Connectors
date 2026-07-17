namespace StockSharp.JpmDataQuery.Native;

sealed class JpmDataQueryClient : BaseLogReceiver, IDisposable
{
	private const string _audience = "JPMC:URI:RS-06785-DataQueryExternalApi-PROD";
	private const int _maxPages = 1000;

	private static readonly Uri _apiAddress =
		new("https://api-dataquery.jpmchase.com/research/dataquery-authe/api/v2/");
	private static readonly Uri _authAddress =
		new("https://authe.jpmorgan.com/as/token.oauth2");
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
	};

	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
	private readonly SemaphoreSlim _authLock = new(1, 1);
	private readonly string _clientId;
	private readonly string _clientSecret;
	private string _accessToken;
	private DateTime _tokenExpires;

	public JpmDataQueryClient(string clientId, string clientSecret)
	{
		_clientId = clientId.ThrowIfEmpty(nameof(clientId));
		_clientSecret = clientSecret.ThrowIfEmpty(nameof(clientSecret));
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public Task Authenticate(CancellationToken cancellationToken)
		=> EnsureToken(true, cancellationToken);

	public async IAsyncEnumerable<JpmDataQueryInstrument> LookupInstruments(
		string groupId, string value, bool exactIdentifier,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var endpoint = exactIdentifier || value.IsEmpty()
			? "group/instruments"
			: "group/instruments/search";
		var query = new JpmDataQueryInstrumentsQuery
		{
			GroupId = groupId.ThrowIfEmpty(nameof(groupId)),
			InstrumentId = exactIdentifier ? value : null,
			Keywords = exactIdentifier ? null : value,
		};

		var address = new Uri(_apiAddress, $"{endpoint}?{query.ToQueryString()}");
		await foreach (var page in GetPages<JpmDataQueryInstrumentsResponse>(
			address, cancellationToken).WithEnforcedCancellation(cancellationToken))
		{
			foreach (var instrument in page.Instruments ?? [])
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (instrument?.InstrumentId.IsEmpty() == false)
					yield return instrument;
			}
		}
	}

	public async IAsyncEnumerable<JpmDataQuerySeriesInstrument> GetTimeSeries(
		string instrumentId, string attribute, DateTime from, DateTime to,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var query = new JpmDataQueryTimeSeriesQuery
		{
			InstrumentId = instrumentId.ThrowIfEmpty(nameof(instrumentId)),
			Attribute = attribute.ThrowIfEmpty(nameof(attribute)),
			From = from,
			To = to,
		};
		var address = new Uri(_apiAddress,
			$"instruments/time-series?{query.ToQueryString()}");

		await foreach (var page in GetPages<JpmDataQueryTimeSeriesResponse>(
			address, cancellationToken).WithEnforcedCancellation(cancellationToken))
		{
			foreach (var instrument in page.Instruments ?? [])
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (instrument?.InstrumentId.IsEmpty() == false)
					yield return instrument;
			}
		}
	}

	private async IAsyncEnumerable<TPage> GetPages<TPage>(Uri firstAddress,
		[EnumeratorCancellation] CancellationToken cancellationToken)
		where TPage : JpmDataQueryPage
	{
		var address = firstAddress;
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (var pageIndex = 0; pageIndex < _maxPages; pageIndex++)
		{
			if (!visited.Add(address.AbsoluteUri))
				throw new InvalidOperationException(
					$"J.P. Morgan DataQuery pagination loop detected at '{address}'.");

			var page = await Get<TPage>(address, cancellationToken);
			yield return page;
			var next = page.GetNextLink();
			if (next.IsEmpty())
				yield break;
			address = Uri.TryCreate(next, UriKind.Absolute, out var absolute)
				? absolute
				: new Uri(_apiAddress, next);
		}

		throw new InvalidOperationException(
			$"J.P. Morgan DataQuery pagination exceeded {_maxPages} pages.");
	}

	private async Task<T> Get<T>(Uri address, CancellationToken cancellationToken)
		where T : class
	{
		for (var attempt = 0; attempt < 4; attempt++)
		{
			await EnsureToken(false, cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get, address);
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
			request.Headers.TryAddWithoutValidation("X-Application", "StockSharp");

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
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body, address,
					response.Headers.TryGetValues("x-dataquery-interaction-id", out var ids)
						? ids.FirstOrDefault() : null);

			return JsonConvert.DeserializeObject<T>(body, _jsonSettings)
				?? throw new InvalidOperationException(
					$"J.P. Morgan DataQuery returned an empty response for '{address}'.");
		}

		throw new InvalidOperationException(
			$"J.P. Morgan DataQuery request '{address}' exhausted its retry limit.");
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
				Content = new JpmDataQueryTokenRequest
				{
					ClientId = _clientId,
					ClientSecret = _clientSecret,
					Audience = _audience,
				}.ToContent(),
			};
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseContentRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body, _authAddress, null);

			var token = JsonConvert.DeserializeObject<JpmDataQueryTokenResponse>(body, _jsonSettings)
				?? throw new InvalidOperationException("J.P. Morgan returned an empty OAuth response.");
			_accessToken = token.AccessToken.ThrowIfEmpty(nameof(JpmDataQueryTokenResponse.AccessToken));
			var lifetime = TimeSpan.FromSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 1800);
			var safety = TimeSpan.FromSeconds(Math.Min(300, lifetime.TotalSeconds / 2));
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
		if (delay == null && response.Headers.RetryAfter?.Date is DateTimeOffset date)
			delay = date - DateTimeOffset.UtcNow;
		if (delay != null && delay.Value > TimeSpan.Zero)
			return delay.Value > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay.Value;
		return TimeSpan.FromSeconds(Math.Pow(2, attempt));
	}

	private static Exception CreateApiError(HttpStatusCode statusCode, string body,
		Uri address, string interactionId)
	{
		JpmDataQueryErrorEnvelope envelope = null;
		try
		{
			envelope = JsonConvert.DeserializeObject<JpmDataQueryErrorEnvelope>(body, _jsonSettings);
		}
		catch (JsonException)
		{
		}

		var error = envelope?.GetError();
		var description = error?.Description;
		if (description.IsEmpty())
			description = body?.Length > 1000 ? body[..1000] : body;
		var trace = interactionId.IsEmpty(error?.InteractionId);
		return new InvalidOperationException(
			$"J.P. Morgan DataQuery request '{address}' failed ({(int)statusCode} {statusCode})" +
			$"{(error?.Code.IsEmpty() == false ? $" [{error.Code}]" : string.Empty)}: {description}" +
			$"{(trace.IsEmpty() ? string.Empty : $" (interaction {trace})")}");
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_authLock.Dispose();
		base.DisposeManaged();
	}
}
