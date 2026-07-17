namespace StockSharp.FactSet.Native;

sealed class FactSetClient : BaseLogReceiver, IDisposable
{
	private static readonly Uri _apiAddress = new("https://api.factset.com/content/");
	private static readonly Uri _wellKnownAddress =
		new("https://auth.factset.com/.well-known/openid-configuration");
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
	};

	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
	private readonly SemaphoreSlim _authLock = new(1, 1);
	private readonly FactSetAuthenticationModes _authenticationMode;
	private readonly string _login;
	private readonly string _password;
	private readonly string _oauthConfigFile;
	private FactSetOAuthConfiguration _oauthConfig;
	private string _issuer;
	private Uri _tokenAddress;
	private string _accessToken;
	private DateTime _tokenExpires;

	public FactSetClient(FactSetAuthenticationModes authenticationMode,
		string login, string password, string oauthConfigFile)
	{
		_authenticationMode = authenticationMode;
		_login = login;
		_password = password;
		_oauthConfigFile = oauthConfigFile;
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "StockSharp-FactSet/1.0");
	}

	public async Task Authenticate(CancellationToken cancellationToken)
	{
		if (_authenticationMode != FactSetAuthenticationModes.OAuth)
			return;

		var json = await File.ReadAllTextAsync(
			_oauthConfigFile.ThrowIfEmpty(nameof(_oauthConfigFile)), cancellationToken);
		_oauthConfig = JsonConvert.DeserializeObject<FactSetOAuthConfiguration>(json, _jsonSettings)
			?? throw new InvalidOperationException("FactSet OAuth configuration is empty.");
		ValidateOAuthConfiguration(_oauthConfig);

		var discovery = await GetUnauthenticated<FactSetOpenIdConfiguration>(
			_wellKnownAddress, cancellationToken);
		_issuer = discovery.Issuer.ThrowIfEmpty(nameof(discovery.Issuer));
		_tokenAddress = new Uri(discovery.TokenEndpoint.ThrowIfEmpty(nameof(discovery.TokenEndpoint)));
		await EnsureToken(true, cancellationToken);
	}

	public async Task<FactSetReference[]> GetReferences(string id,
		CancellationToken cancellationToken)
	{
		var query = new FactSetReferenceQuery { Id = id.ThrowIfEmpty(nameof(id)) };
		var response = await Get<FactSetReferencesResponse>(new Uri(_apiAddress,
			$"factset-prices/v1/references?{query.ToQueryString()}"), cancellationToken);
		return response.Data ?? [];
	}

	public async Task<FactSetPrice[]> GetPrices(string id, DateTime? from, DateTime? to,
		string currency, FactSetPriceAdjustments adjustment,
		CancellationToken cancellationToken)
	{
		var query = new FactSetPricesQuery
		{
			Id = id.ThrowIfEmpty(nameof(id)),
			From = from,
			To = to,
			Currency = currency,
			Adjustment = adjustment.ToNative(),
		};
		var response = await Get<FactSetPricesResponse>(new Uri(_apiAddress,
			$"factset-prices/v1/prices?{query.ToQueryString()}"), cancellationToken);
		return response.Data ?? [];
	}

	public async Task<FactSetFixedIncomePrice[]> GetFixedIncomePrices(string id,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var query = new FactSetFixedIncomePricesQuery
		{
			Id = id.ThrowIfEmpty(nameof(id)),
			From = from,
			To = to,
		};
		var response = await Get<FactSetFixedIncomePricesResponse>(new Uri(_apiAddress,
			$"factset-prices/v1/fixed-income?{query.ToQueryString()}"), cancellationToken);
		return response.Data ?? [];
	}

	private async Task<T> Get<T>(Uri address, CancellationToken cancellationToken)
		where T : class
	{
		for (var attempt = 0; attempt < 4; attempt++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, address);
			await SetAuthorization(request, cancellationToken);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseContentRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.StatusCode == HttpStatusCode.Unauthorized &&
				_authenticationMode == FactSetAuthenticationModes.OAuth && attempt == 0)
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
				throw CreateApiError(response.StatusCode, body, address);

			return JsonConvert.DeserializeObject<T>(body, _jsonSettings)
				?? throw new InvalidOperationException(
					$"FactSet returned an empty response for '{address}'.");
		}

		throw new InvalidOperationException(
			$"FactSet request '{address}' exhausted its retry limit.");
	}

	private async Task<T> GetUnauthenticated<T>(Uri address,
		CancellationToken cancellationToken) where T : class
	{
		using var response = await _http.GetAsync(address, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateApiError(response.StatusCode, body, address);
		return JsonConvert.DeserializeObject<T>(body, _jsonSettings)
			?? throw new InvalidOperationException($"FactSet returned an empty response for '{address}'.");
	}

	private async Task SetAuthorization(HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		if (_authenticationMode == FactSetAuthenticationModes.ApiKey)
		{
			var value = Convert.ToBase64String(Encoding.UTF8.GetBytes(
				$"{_login.ThrowIfEmpty(nameof(_login))}:{_password.ThrowIfEmpty(nameof(_password))}"));
			request.Headers.Authorization = new AuthenticationHeaderValue("Basic", value);
			return;
		}

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

			var assertion = CreateClientAssertion(_oauthConfig, _issuer);
			using var request = new HttpRequestMessage(HttpMethod.Post, _tokenAddress)
			{
				Content = new FactSetTokenRequest
				{
					ClientId = _oauthConfig.ClientId,
					ClientAssertion = assertion,
				}.ToContent(),
			};
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseContentRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body, _tokenAddress);

			var token = JsonConvert.DeserializeObject<FactSetTokenResponse>(body, _jsonSettings)
				?? throw new InvalidOperationException("FactSet returned an empty OAuth response.");
			_accessToken = token.AccessToken.ThrowIfEmpty(nameof(FactSetTokenResponse.AccessToken));
			var lifetime = TimeSpan.FromSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 300);
			var safety = TimeSpan.FromSeconds(Math.Min(60, lifetime.TotalSeconds / 2));
			_tokenExpires = DateTime.UtcNow + lifetime - safety;
		}
		finally
		{
			_authLock.Release();
		}
	}

	private static string CreateClientAssertion(FactSetOAuthConfiguration configuration,
		string issuer)
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		var header = new FactSetJwtHeader { KeyId = configuration.Jwk.KeyId };
		var payload = new FactSetJwtPayload
		{
			Issuer = configuration.ClientId,
			Subject = configuration.ClientId,
			Audience = issuer,
			JwtId = configuration.Jwk.KeyId,
			NotBefore = now - 5,
			Expires = now + 300,
			IssuedAt = now,
		};
		var encodedHeader = EncodeBase64Url(Encoding.UTF8.GetBytes(
			JsonConvert.SerializeObject(header, _jsonSettings)));
		var encodedPayload = EncodeBase64Url(Encoding.UTF8.GetBytes(
			JsonConvert.SerializeObject(payload, _jsonSettings)));
		var signingInput = $"{encodedHeader}.{encodedPayload}";

		using var rsa = RSA.Create();
		rsa.ImportParameters(configuration.Jwk.ToParameters());
		var signature = rsa.SignData(Encoding.ASCII.GetBytes(signingInput),
			HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		return $"{signingInput}.{EncodeBase64Url(signature)}";
	}

	private static string EncodeBase64Url(byte[] value)
		=> Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	private static void ValidateOAuthConfiguration(FactSetOAuthConfiguration configuration)
	{
		configuration.ClientId.ThrowIfEmpty(nameof(configuration.ClientId));
		var jwk = configuration.Jwk ?? throw new InvalidOperationException(
			"FactSet OAuth configuration does not contain a JWK.");
		if (!jwk.KeyType.EqualsIgnoreCase("RSA") || !jwk.Algorithm.EqualsIgnoreCase("RS256"))
			throw new InvalidOperationException(
				$"FactSet OAuth JWK '{jwk.KeyId}' must use RSA and RS256.");
		jwk.KeyId.ThrowIfEmpty(nameof(jwk.KeyId));
		_ = jwk.ToParameters();
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

	private static Exception CreateApiError(HttpStatusCode statusCode, string body, Uri address)
	{
		FactSetErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<FactSetErrorResponse>(body, _jsonSettings);
		}
		catch (JsonException)
		{
		}
		var message = error?.GetMessage();
		if (message.IsEmpty())
			message = body?.Length > 1000 ? body[..1000] : body;
		return new InvalidOperationException(
			$"FactSet request '{address}' failed ({(int)statusCode} {statusCode}): {message}");
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_authLock.Dispose();
		base.DisposeManaged();
	}
}
