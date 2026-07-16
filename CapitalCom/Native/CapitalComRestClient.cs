namespace StockSharp.CapitalCom.Native;

internal sealed class CapitalComApiException : InvalidOperationException
{
	public CapitalComApiException(HttpStatusCode statusCode, string errorCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
		ErrorCode = errorCode;
	}

	public HttpStatusCode StatusCode { get; }
	public string ErrorCode { get; }
}

internal sealed class CapitalComRestClient : BaseLogReceiver, IDisposable
{
	private static readonly TimeSpan _minimumRequestInterval = TimeSpan.FromMilliseconds(105);

	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly string _identifier;
	private readonly string _password;
	private readonly bool _isPasswordEncryptionEnabled;
	private readonly int _maxAttempts;
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private string _cst;
	private string _securityToken;
	private DateTimeOffset _lastRequest;

	public CapitalComRestClient(bool isDemo, string apiKey, string identifier, string password,
		bool isPasswordEncryptionEnabled, int maxAttempts)
	{
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_identifier = identifier.ThrowIfEmpty(nameof(identifier));
		_password = password.ThrowIfEmpty(nameof(password));
		_isPasswordEncryptionEnabled = isPasswordEncryptionEnabled;
		_maxAttempts = Math.Max(1, maxAttempts);
		_http = new()
		{
			BaseAddress = new(isDemo
				? "https://demo-api-capital.backend-capital.com/"
				: "https://api-capital.backend-capital.com/"),
			Timeout = TimeSpan.FromSeconds(30),
		};
	}

	public CapitalComSession Session { get; private set; }

	public async Task<CapitalComSession> Login(string requestedAccountId, CancellationToken cancellationToken)
	{
		var password = _password;
		if (_isPasswordEncryptionEnabled)
		{
			var key = await Send<CapitalComEncryptionKey>(HttpMethod.Get,
				"api/v1/session/encryptionKey", null, false, cancellationToken);
			password = EncryptPassword(password, key);
		}

		var response = await SendWithHeaders<CapitalComLoginResponse>(HttpMethod.Post, "api/v1/session",
			new CapitalComLoginRequest
			{
				Identifier = _identifier,
				Password = password,
				IsEncryptedPassword = _isPasswordEncryptionEnabled,
			}, false, cancellationToken);
		using var loginResponse = response.Response;

		_cst = GetRequiredHeader(loginResponse, "CST");
		_securityToken = GetRequiredHeader(loginResponse, "X-SECURITY-TOKEN");
		var login = response.Value ?? throw new InvalidOperationException(
			"Capital.com login returned no session details.");

		var selectedId = requestedAccountId;
		if (selectedId.IsEmpty())
		{
			selectedId = login.CurrentAccountId.IsEmpty(
				login.Accounts?.FirstOrDefault(a => a.IsPreferred)?.AccountId
					.IsEmpty(login.Accounts?.FirstOrDefault()?.AccountId));
		}

		if (selectedId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.AccountNotFound);

		if (!requestedAccountId.IsEmpty() && login.Accounts is { Length: > 0 } &&
			!login.Accounts.Any(a => a.AccountId.EqualsIgnoreCase(requestedAccountId)))
			throw new InvalidOperationException($"Capital.com account '{requestedAccountId}' was not found.");

		if (!selectedId.EqualsIgnoreCase(login.CurrentAccountId))
		{
			await Send<CapitalComApiError>(HttpMethod.Put, "api/v1/session",
				new CapitalComAccountSwitchRequest { AccountId = selectedId }, true, cancellationToken, true);
		}

		var accounts = await GetAccounts(cancellationToken);
		var selected = accounts.Accounts?.FirstOrDefault(a => a.AccountId.EqualsIgnoreCase(selectedId));
		var streamHost = login.StreamingHost.IsEmpty(login.StreamEndpoint);

		Session = new()
		{
			AccountId = selectedId,
			Currency = selected?.Currency.IsEmpty(login.CurrencyIsoCode),
			StreamingUrl = BuildStreamingUrl(streamHost),
			Cst = _cst,
			SecurityToken = _securityToken,
		};
		return Session;
	}

	public Task<CapitalComAccountsResponse> GetAccounts(CancellationToken cancellationToken)
		=> Send<CapitalComAccountsResponse>(HttpMethod.Get, "api/v1/accounts", null, true, cancellationToken);

	public Task<CapitalComMarketsResponse> GetMarkets(string searchTerm, CancellationToken cancellationToken)
		=> Send<CapitalComMarketsResponse>(HttpMethod.Get,
			searchTerm.IsEmpty()
				? "api/v1/markets"
				: $"api/v1/markets?searchTerm={Uri.EscapeDataString(searchTerm)}",
			null, true, cancellationToken);

	public Task<CapitalComMarketDetails> GetMarket(string epic, CancellationToken cancellationToken)
		=> Send<CapitalComMarketDetails>(HttpMethod.Get,
			$"api/v1/markets/{Uri.EscapeDataString(epic)}", null, true, cancellationToken);

	public Task<CapitalComPricesResponse> GetPrices(string epic, string resolution, DateTimeOffset from,
		DateTimeOffset to, int max, CancellationToken cancellationToken)
		=> Send<CapitalComPricesResponse>(HttpMethod.Get,
			$"api/v1/prices/{Uri.EscapeDataString(epic)}?resolution={Uri.EscapeDataString(resolution)}" +
			$"&max={Math.Clamp(max, 1, 1000).ToString(CultureInfo.InvariantCulture)}" +
			$"&from={Uri.EscapeDataString(FormatTime(from))}&to={Uri.EscapeDataString(FormatTime(to))}",
			null, true, cancellationToken);

	public Task<CapitalComActivitiesResponse> GetActivities(DateTimeOffset? from, DateTimeOffset? to,
		CancellationToken cancellationToken)
	{
		var path = "api/v1/history/activity?detailed=true";
		if (from is { } fromValue)
			path += $"&from={Uri.EscapeDataString(FormatTime(fromValue))}";
		if (to is { } toValue)
			path += $"&to={Uri.EscapeDataString(FormatTime(toValue))}";
		if (from == null && to == null)
			path += "&lastPeriod=86400";
		return Send<CapitalComActivitiesResponse>(HttpMethod.Get, path, null, true, cancellationToken);
	}

	public Task<CapitalComPositionsResponse> GetPositions(CancellationToken cancellationToken)
		=> Send<CapitalComPositionsResponse>(HttpMethod.Get, "api/v1/positions", null, true, cancellationToken);

	public Task<CapitalComWorkingOrdersResponse> GetWorkingOrders(CancellationToken cancellationToken)
		=> Send<CapitalComWorkingOrdersResponse>(HttpMethod.Get, "api/v1/workingorders", null, true, cancellationToken);

	public Task<CapitalComDealReference> CreatePosition(CapitalComCreatePositionRequest request,
		CancellationToken cancellationToken)
		=> Send<CapitalComDealReference>(HttpMethod.Post, "api/v1/positions", request, true, cancellationToken);

	public Task<CapitalComDealReference> EditPosition(string dealId, CapitalComEditPositionRequest request,
		CancellationToken cancellationToken)
		=> Send<CapitalComDealReference>(HttpMethod.Put,
			$"api/v1/positions/{Uri.EscapeDataString(dealId)}", request, true, cancellationToken);

	public Task<CapitalComDealReference> ClosePosition(string dealId, CancellationToken cancellationToken)
		=> Send<CapitalComDealReference>(HttpMethod.Delete,
			$"api/v1/positions/{Uri.EscapeDataString(dealId)}", null, true, cancellationToken);

	public Task<CapitalComDealReference> CreateWorkingOrder(CapitalComCreateWorkingOrderRequest request,
		CancellationToken cancellationToken)
		=> Send<CapitalComDealReference>(HttpMethod.Post, "api/v1/workingorders", request, true, cancellationToken);

	public Task<CapitalComDealReference> EditWorkingOrder(string dealId, CapitalComEditWorkingOrderRequest request,
		CancellationToken cancellationToken)
		=> Send<CapitalComDealReference>(HttpMethod.Put,
			$"api/v1/workingorders/{Uri.EscapeDataString(dealId)}", request, true, cancellationToken);

	public Task<CapitalComDealReference> DeleteWorkingOrder(string dealId, CancellationToken cancellationToken)
		=> Send<CapitalComDealReference>(HttpMethod.Delete,
			$"api/v1/workingorders/{Uri.EscapeDataString(dealId)}", null, true, cancellationToken);

	public Task<CapitalComConfirmation> GetConfirmation(string dealReference, CancellationToken cancellationToken)
		=> Send<CapitalComConfirmation>(HttpMethod.Get,
			$"api/v1/confirms/{Uri.EscapeDataString(dealReference)}", null, true, cancellationToken);

	public Task Ping(CancellationToken cancellationToken)
		=> Send<CapitalComApiError>(HttpMethod.Get, "api/v1/ping", null, true, cancellationToken, true);

	public async Task Logout(CancellationToken cancellationToken)
	{
		if (_cst.IsEmpty())
			return;

		try
		{
			await Send<CapitalComApiError>(HttpMethod.Delete, "api/v1/session", null, true,
				cancellationToken, true);
		}
		finally
		{
			_cst = null;
			_securityToken = null;
			Session = null;
		}
	}

	private async Task<T> Send<T>(HttpMethod method, string path, object body, bool isAuthenticated,
		CancellationToken cancellationToken, bool isEmptyAllowed = false)
	{
		var result = await SendWithHeaders<T>(method, path, body, isAuthenticated, cancellationToken,
			isEmptyAllowed);
		result.Response.Dispose();
		return result.Value;
	}

	private async Task<(T Value, HttpResponseMessage Response)> SendWithHeaders<T>(HttpMethod method,
		string path, object body, bool isAuthenticated, CancellationToken cancellationToken,
		bool isEmptyAllowed = false)
	{
		await _requestGate.WaitAsync(cancellationToken);
		try
		{
			for (var attempt = 1; ; attempt++)
			{
				var remaining = _minimumRequestInterval - (DateTimeOffset.UtcNow - _lastRequest);
				if (remaining > TimeSpan.Zero)
					await Task.Delay(remaining, cancellationToken);

				using var request = new HttpRequestMessage(method, path);
				request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
				request.Headers.TryAddWithoutValidation("X-CAP-API-KEY", _apiKey);
				if (isAuthenticated)
				{
					request.Headers.TryAddWithoutValidation("CST", _cst.ThrowIfEmpty(nameof(_cst)));
					request.Headers.TryAddWithoutValidation("X-SECURITY-TOKEN",
						_securityToken.ThrowIfEmpty(nameof(_securityToken)));
				}
				if (body != null)
				{
					request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8,
						"application/json");
				}

				_lastRequest = DateTimeOffset.UtcNow;
				var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
				var payload = await response.Content.ReadAsStringAsync(cancellationToken);

				if (response.IsSuccessStatusCode)
				{
					UpdateTokens(response);
					if (payload.IsEmpty())
					{
						if (isEmptyAllowed)
							return (default, response);
						response.Dispose();
						throw new InvalidOperationException(
							$"Capital.com {method} {path} returned an empty response.");
					}

					return (JsonConvert.DeserializeObject<T>(payload), response);
				}

				CapitalComApiError error = null;
				if (!payload.IsEmpty())
				{
					try
					{
						error = JsonConvert.DeserializeObject<CapitalComApiError>(payload);
					}
					catch (JsonException)
					{
					}
				}

				if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < _maxAttempts)
				{
					var delay = response.Headers.RetryAfter?.Delta ??
						TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
					response.Dispose();
					await Task.Delay(delay, cancellationToken);
					continue;
				}

				var reason = error?.Code.IsEmpty(payload).IsEmpty(response.ReasonPhrase);
				var exception = new CapitalComApiException(response.StatusCode, error?.Code,
					$"Capital.com {method} {path} failed ({(int)response.StatusCode}): {reason}");
				response.Dispose();
				throw exception;
			}
		}
		finally
		{
			_requestGate.Release();
		}
	}

	private void UpdateTokens(HttpResponseMessage response)
	{
		if (response.Headers.TryGetValues("CST", out var cst))
			_cst = cst.FirstOrDefault().IsEmpty(_cst);
		if (response.Headers.TryGetValues("X-SECURITY-TOKEN", out var token))
			_securityToken = token.FirstOrDefault().IsEmpty(_securityToken);
	}

	private static string GetRequiredHeader(HttpResponseMessage response, string name)
	{
		if (!response.Headers.TryGetValues(name, out var values) || values.FirstOrDefault().IsEmpty())
			throw new InvalidOperationException(
				$"Capital.com login did not return the required {name} header.");
		return values.First();
	}

	private static string BuildStreamingUrl(string host)
	{
		host = host.IsEmpty("wss://api-streaming-capital.backend-capital.com/");
		if (host.TrimEnd('/').EndsWith("/connect", StringComparison.OrdinalIgnoreCase))
			return host;
		return $"{host.TrimEnd('/')}/connect";
	}

	private static string FormatTime(DateTimeOffset time)
		=> time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

	private static string EncryptPassword(string password, CapitalComEncryptionKey encryptionKey)
	{
		if (encryptionKey?.EncryptionKey.IsEmpty() != false || encryptionKey.TimeStamp <= 0)
			throw new InvalidOperationException("Capital.com returned an invalid password-encryption key.");

		var publicKey = Convert.FromBase64String(encryptionKey.EncryptionKey);
		using var rsa = RSA.Create();
		try
		{
			rsa.ImportSubjectPublicKeyInfo(publicKey, out _);
		}
		catch (CryptographicException)
		{
			rsa.ImportRSAPublicKey(publicKey, out _);
		}

		var clear = Encoding.UTF8.GetBytes(
			$"{password}|{encryptionKey.TimeStamp.ToString(CultureInfo.InvariantCulture)}");
		return Convert.ToBase64String(rsa.Encrypt(clear, RSAEncryptionPadding.Pkcs1));
	}

	protected override void DisposeManaged()
	{
		_requestGate.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}
}
