namespace StockSharp.DowJones.Native;

sealed class DowJonesApiException : InvalidOperationException
{
	public DowJonesApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class DowJonesClient : BaseLogReceiver
{
	private const string _contentMediaType =
		"application/vnd.dowjones.dna.content.v_1.0+json";
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly DowJonesAuthenticationModes _authenticationMode;
	private readonly Uri _apiAddress;
	private readonly Uri _oauthAddress;
	private readonly string _token;
	private readonly string _clientId;
	private readonly string _login;
	private readonly string _password;
	private readonly int _maxAttempts;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
	private readonly SemaphoreSlim _authLock = new(1, 1);
	private string _accessToken;
	private string _refreshToken;
	private DateTime _accessTokenExpiresUtc;

	public DowJonesClient(DowJonesAuthenticationModes authenticationMode,
		Uri apiAddress, Uri oauthAddress, string token, string clientId,
		string login, string password, int maxAttempts)
	{
		_authenticationMode = authenticationMode;
		_apiAddress = EnsureTrailingSlash(apiAddress ??
			throw new ArgumentNullException(nameof(apiAddress)));
		_oauthAddress = oauthAddress ?? throw new ArgumentNullException(nameof(oauthAddress));
		_token = token;
		_clientId = clientId;
		_login = login;
		_password = password;
		_maxAttempts = Math.Max(1, maxAttempts);
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"User-Agent", "StockSharp-DowJones/1.0");
	}

	public async Task Authenticate(CancellationToken cancellationToken)
	{
		switch (_authenticationMode)
		{
			case DowJonesAuthenticationModes.BearerToken:
			case DowJonesAuthenticationModes.UserKey:
				_token.ThrowIfEmpty(nameof(_token));
				break;
			case DowJonesAuthenticationModes.ServiceAccount:
				await EnsureAccessToken(true, cancellationToken);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(_authenticationMode),
					_authenticationMode, null);
		}
	}

	public Task<DowJonesSearchResponse> Search(DowJonesSearchRequest request,
		CancellationToken cancellationToken)
	{
		var address = new Uri(_apiAddress, "content/realtime/search");
		var body = JsonConvert.SerializeObject(request ??
			throw new ArgumentNullException(nameof(request)), _jsonSettings);
		return SendApi<DowJonesSearchResponse>(HttpMethod.Post, address, body,
			_contentMediaType, cancellationToken);
	}

	public Task<DowJonesArticleResponse> GetArticle(string drn,
		CancellationToken cancellationToken)
	{
		var path = $"content/{Uri.EscapeDataString(drn.ThrowIfEmpty(nameof(drn)))}";
		return SendApi<DowJonesArticleResponse>(HttpMethod.Get,
			new Uri(_apiAddress, path), null, "application/json", cancellationToken);
	}

	private async Task<TResponse> SendApi<TResponse>(HttpMethod method, Uri address,
		string body, string accept, CancellationToken cancellationToken)
		where TResponse : class
	{
		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(method, address);
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
			await SetAuthorization(request, cancellationToken);
			if (body != null)
				request.Content = new StringContent(body, Encoding.UTF8, _contentMediaType);

			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var content = await response.Content.ReadAsStringAsync(cancellationToken);

			if (response.StatusCode == HttpStatusCode.Unauthorized &&
				_authenticationMode == DowJonesAuthenticationModes.ServiceAccount &&
				attempt < _maxAttempts)
			{
				InvalidateAccessToken();
				continue;
			}
			if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
				(int)response.StatusCode is >= 500 and <= 511) &&
				attempt < _maxAttempts)
			{
				await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateError(response.StatusCode, content, address);
			if (content.IsEmpty())
				throw new InvalidDataException(
					$"Dow Jones returned an empty response for '{address.AbsolutePath}'.");

			try
			{
				return JsonConvert.DeserializeObject<TResponse>(content, _jsonSettings)
					?? throw new InvalidDataException(
						$"Dow Jones returned an empty JSON document for '{address.AbsolutePath}'.");
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					$"Dow Jones returned invalid JSON for '{address.AbsolutePath}'.", error);
			}
		}
	}

	private async Task SetAuthorization(HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		switch (_authenticationMode)
		{
			case DowJonesAuthenticationModes.BearerToken:
				request.Headers.Authorization = new AuthenticationHeaderValue(
					"Bearer", NormalizeBearer(_token));
				break;
			case DowJonesAuthenticationModes.ServiceAccount:
				await EnsureAccessToken(false, cancellationToken);
				request.Headers.Authorization = new AuthenticationHeaderValue(
					"Bearer", _accessToken);
				break;
			case DowJonesAuthenticationModes.UserKey:
				request.Headers.TryAddWithoutValidation("user-key",
					_token.ThrowIfEmpty(nameof(_token)));
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(_authenticationMode),
					_authenticationMode, null);
		}
	}

	private async Task EnsureAccessToken(bool force,
		CancellationToken cancellationToken)
	{
		if (!force && !_accessToken.IsEmpty() &&
			DateTime.UtcNow < _accessTokenExpiresUtc)
		{
			return;
		}

		await _authLock.WaitAsync(cancellationToken);
		try
		{
			if (!force && !_accessToken.IsEmpty() &&
				DateTime.UtcNow < _accessTokenExpiresUtc)
			{
				return;
			}

			if (!_refreshToken.IsEmpty())
			{
				try
				{
					var refreshed = await PostAuthentication<DowJonesRefreshGrantRequest,
						DowJonesIdentityTokenResponse>(new()
						{
							ClientId = _clientId,
							RefreshToken = _refreshToken,
						}, cancellationToken);
					await ExchangeAssertion(refreshed.AccessToken, cancellationToken);
					return;
				}
				catch (DowJonesApiException error) when (error.StatusCode is
					HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
				{
					_refreshToken = null;
				}
			}

			var identity = await PostAuthentication<DowJonesPasswordGrantRequest,
				DowJonesIdentityTokenResponse>(new()
				{
					ClientId = _clientId.ThrowIfEmpty(nameof(_clientId)),
					Username = _login.ThrowIfEmpty(nameof(_login)),
					Password = _password.ThrowIfEmpty(nameof(_password)),
				}, cancellationToken);
			_refreshToken = identity.RefreshToken;
			await ExchangeAssertion(identity.IdToken, cancellationToken);
		}
		finally
		{
			_authLock.Release();
		}
	}

	private async Task ExchangeAssertion(string assertion,
		CancellationToken cancellationToken)
	{
		var token = await PostAuthentication<DowJonesJwtGrantRequest,
			DowJonesAccessTokenResponse>(new()
			{
				Assertion = assertion.ThrowIfEmpty(nameof(assertion)),
				ClientId = _clientId.ThrowIfEmpty(nameof(_clientId)),
			}, cancellationToken);
		_accessToken = token.AccessToken.ThrowIfEmpty(
			nameof(DowJonesAccessTokenResponse.AccessToken));
		var lifetime = TimeSpan.FromSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 3600);
		var safety = TimeSpan.FromSeconds(Math.Min(60, lifetime.TotalSeconds / 2));
		_accessTokenExpiresUtc = DateTime.UtcNow + lifetime - safety;
	}

	private async Task<TResponse> PostAuthentication<TRequest, TResponse>(
		TRequest requestBody, CancellationToken cancellationToken)
		where TRequest : class
		where TResponse : class
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, _oauthAddress)
		{
			Content = new StringContent(JsonConvert.SerializeObject(requestBody,
				_jsonSettings), Encoding.UTF8, "application/json"),
		};
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateError(response.StatusCode, content, _oauthAddress);
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(content, _jsonSettings)
				?? throw new InvalidDataException("Dow Jones returned an empty OAuth response.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Dow Jones returned invalid OAuth JSON.", error);
		}
	}

	private void InvalidateAccessToken()
	{
		_accessToken = null;
		_accessTokenExpiresUtc = default;
	}

	private static DowJonesApiException CreateError(HttpStatusCode statusCode,
		string content, Uri address)
	{
		DowJonesErrorResponse response = null;
		try
		{
			response = JsonConvert.DeserializeObject<DowJonesErrorResponse>(
				content, _jsonSettings);
		}
		catch (JsonException)
		{
		}
		var details = response?.GetMessage().IsEmpty(content)?.Trim();
		if (details?.Length > 1000)
			details = details[..1000];
		return new(statusCode,
			$"Dow Jones request '{address.AbsolutePath}' failed " +
			$"({(int)statusCode} {statusCode})" +
			(details.IsEmpty() ? "." : $": {details}"));
	}

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response.Headers.RetryAfter?.Delta is { } delta)
			return ClampDelay(delta);
		if (response.Headers.RetryAfter?.Date is { } date)
			return ClampDelay(date.UtcDateTime - DateTime.UtcNow);
		return TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
	}

	private static TimeSpan ClampDelay(TimeSpan delay)
		=> delay <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) :
			delay > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : delay;

	private static string NormalizeBearer(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
			? value[7..].Trim() : value;
	}

	private static Uri EnsureTrailingSlash(Uri address)
		=> address.AbsoluteUri.EndsWith('/') ? address : new(address.AbsoluteUri + "/");

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_authLock.Dispose();
		base.DisposeManaged();
	}
}
