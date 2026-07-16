namespace StockSharp.IG.Native;

internal sealed class IgRestClient : BaseLogReceiver, IDisposable
{
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly string _identifier;
	private readonly string _password;
	private readonly bool _encryptPassword;
	private readonly int _maxAttempts;
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private string _cst;
	private string _securityToken;

	public IgRestClient(IgEnvironments environment, string apiKey, string identifier, string password,
		bool encryptPassword, int maxAttempts)
	{
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_identifier = identifier.ThrowIfEmpty(nameof(identifier));
		_password = password.ThrowIfEmpty(nameof(password));
		_encryptPassword = encryptPassword;
		_maxAttempts = Math.Max(1, maxAttempts);
		_http = new()
		{
			BaseAddress = new(environment == IgEnvironments.Demo
				? "https://demo-api.ig.com/gateway/deal/"
				: "https://api.ig.com/gateway/deal/"),
			Timeout = TimeSpan.FromSeconds(30),
		};
	}

	public IgSession Session { get; private set; }

	public async Task<IgSession> Login(string accountId, CancellationToken cancellationToken)
	{
		var password = _password;
		if (_encryptPassword)
		{
			var key = await Send<IgEncryptionKey>(HttpMethod.Get, "session/encryptionKey", 1, null, false, cancellationToken);
			password = EncryptPassword(password, key);
		}

		var response = await SendWithHeaders<IgLoginResponse>(HttpMethod.Post, "session", 2,
			new IgLoginRequest
			{
				Identifier = _identifier,
				Password = password,
				EncryptedPassword = _encryptPassword,
			}, false, cancellationToken);
		using var loginResponse = response.Response;

		_cst = GetRequiredHeader(loginResponse, "CST");
		_securityToken = GetRequiredHeader(loginResponse, "X-SECURITY-TOKEN");
		var login = response.Value ?? throw new InvalidOperationException("IG login returned no session details.");
		var selected = accountId.IsEmpty()
			? login.Accounts?.FirstOrDefault(a => a.Id.EqualsIgnoreCase(login.CurrentAccountId))
				?? login.Accounts?.FirstOrDefault(a => a.Preferred)
				?? login.Accounts?.FirstOrDefault()
			: login.Accounts?.FirstOrDefault(a => a.Id.EqualsIgnoreCase(accountId));
		if (selected == null)
			throw new InvalidOperationException(accountId.IsEmpty()
				? LocalizedStrings.AccountNotFound
				: $"IG account '{accountId}' was not found.");

		if (!selected.Id.EqualsIgnoreCase(login.CurrentAccountId))
			await Send<IgAccountSwitchResponse>(HttpMethod.Put, "session", 1,
				new IgAccountSwitchRequest { AccountId = selected.Id, DefaultAccount = false }, true, cancellationToken);

		Session = new()
		{
			AccountId = selected.Id,
			Currency = selected.Currency.IsEmpty(login.CurrencyIsoCode),
			LightstreamerEndpoint = login.LightstreamerEndpoint.ThrowIfEmpty(nameof(login.LightstreamerEndpoint)),
			Cst = _cst,
			SecurityToken = _securityToken,
			Accounts = login.Accounts ?? [],
		};
		return Session;
	}

	public Task<IgAccountList> GetAccounts(CancellationToken cancellationToken)
		=> Send<IgAccountList>(HttpMethod.Get, "accounts", 1, null, true, cancellationToken);

	public Task<IgSearchResponse> SearchMarkets(string query, CancellationToken cancellationToken)
		=> Send<IgSearchResponse>(HttpMethod.Get,
			$"markets?searchTerm={Uri.EscapeDataString(query ?? string.Empty)}", 1, null, true, cancellationToken);

	public Task<IgMarketDetails> GetMarket(string epic, CancellationToken cancellationToken)
		=> Send<IgMarketDetails>(HttpMethod.Get, $"markets/{Uri.EscapeDataString(epic)}", 3, null, true, cancellationToken);

	public Task<IgPriceList> GetPrices(string epic, string resolution, DateTimeOffset from,
		DateTimeOffset to, int pageNumber, int pageSize, CancellationToken cancellationToken)
		=> Send<IgPriceList>(HttpMethod.Get,
			$"prices/{Uri.EscapeDataString(epic)}?resolution={resolution}&from={Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture))}" +
			$"&to={Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture))}" +
			$"&pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}&pageNumber={pageNumber.ToString(CultureInfo.InvariantCulture)}", 3, null, true, cancellationToken);

	public Task<IgActivitiesResponse> GetActivities(DateTime? from, DateTime? to, int pageSize, string next,
		CancellationToken cancellationToken)
	{
		var path = next;
		if (path.IsEmpty())
		{
			path = $"history/activity?detailed=true&pageSize={Math.Clamp(pageSize, 10, 500).ToString(CultureInfo.InvariantCulture)}";
			if (from != null)
				path += $"&from={Uri.EscapeDataString(from.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture))}";
			if (to != null)
				path += $"&to={Uri.EscapeDataString(to.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture))}";
		}
		return Send<IgActivitiesResponse>(HttpMethod.Get, path, 3, null, true, cancellationToken);
	}

	public Task<IgPositionsResponse> GetPositions(CancellationToken cancellationToken)
		=> Send<IgPositionsResponse>(HttpMethod.Get, "positions", 2, null, true, cancellationToken);

	public Task<IgWorkingOrdersResponse> GetWorkingOrders(CancellationToken cancellationToken)
		=> Send<IgWorkingOrdersResponse>(HttpMethod.Get, "working-orders", 2, null, true, cancellationToken);

	public Task<IgDealReference> CreatePosition(IgCreatePositionRequest request, CancellationToken cancellationToken)
		=> Send<IgDealReference>(HttpMethod.Post, "positions/otc", 2, request, true, cancellationToken);

	public Task<IgDealReference> ClosePosition(IgClosePositionRequest request, CancellationToken cancellationToken)
		=> Send<IgDealReference>(HttpMethod.Delete, "positions/otc", 1, request, true, cancellationToken);

	public Task<IgDealReference> EditPosition(string dealId, IgEditPositionRequest request, CancellationToken cancellationToken)
		=> Send<IgDealReference>(HttpMethod.Put, $"positions/otc/{Uri.EscapeDataString(dealId)}", 2, request, true, cancellationToken);

	public Task<IgDealReference> CreateWorkingOrder(IgCreateWorkingOrderRequest request, CancellationToken cancellationToken)
		=> Send<IgDealReference>(HttpMethod.Post, "working-orders/otc", 2, request, true, cancellationToken);

	public Task<IgDealReference> EditWorkingOrder(string dealId, IgEditWorkingOrderRequest request, CancellationToken cancellationToken)
		=> Send<IgDealReference>(HttpMethod.Put, $"working-orders/otc/{Uri.EscapeDataString(dealId)}", 2, request, true, cancellationToken);

	public Task<IgDealReference> DeleteWorkingOrder(string dealId, CancellationToken cancellationToken)
		=> Send<IgDealReference>(HttpMethod.Delete, $"working-orders/otc/{Uri.EscapeDataString(dealId)}", 2, null, true, cancellationToken);

	public Task<IgConfirmation> GetConfirmation(string dealReference, CancellationToken cancellationToken)
		=> Send<IgConfirmation>(HttpMethod.Get, $"confirms/{Uri.EscapeDataString(dealReference)}", 1, null, true, cancellationToken);

	public async Task Logout(CancellationToken cancellationToken)
	{
		if (_cst.IsEmpty())
			return;
		try
		{
			await Send<IgApiError>(HttpMethod.Delete, "session", 1, null, true, cancellationToken, true);
		}
		finally
		{
			_cst = null;
			_securityToken = null;
			Session = null;
		}
	}

	private async Task<T> Send<T>(HttpMethod method, string path, int version, object body, bool authenticated,
		CancellationToken cancellationToken, bool allowEmpty = false)
	{
		var result = await SendWithHeaders<T>(method, path, version, body, authenticated, cancellationToken, allowEmpty);
		result.Response.Dispose();
		return result.Value;
	}

	private async Task<(T Value, HttpResponseMessage Response)> SendWithHeaders<T>(HttpMethod method, string path,
		int version, object body, bool authenticated, CancellationToken cancellationToken, bool allowEmpty = false)
	{
		await _requestGate.WaitAsync(cancellationToken);
		try
		{
			for (var attempt = 1; ; attempt++)
			{
				using var request = new HttpRequestMessage(method, path);
				request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
				request.Headers.TryAddWithoutValidation("X-IG-API-KEY", _apiKey);
				request.Headers.TryAddWithoutValidation("VERSION", version.ToString(CultureInfo.InvariantCulture));
				if (authenticated)
				{
					request.Headers.TryAddWithoutValidation("CST", _cst.ThrowIfEmpty(nameof(_cst)));
					request.Headers.TryAddWithoutValidation("X-SECURITY-TOKEN", _securityToken.ThrowIfEmpty(nameof(_securityToken)));
				}
				if (body != null)
					request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

				var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
				var payload = await response.Content.ReadAsStringAsync(cancellationToken);
				if (response.IsSuccessStatusCode)
				{
					UpdateTokens(response);
					if (payload.IsEmpty())
					{
						if (allowEmpty)
							return (default, response);
						throw new InvalidOperationException($"IG {method} {path} returned an empty response.");
					}
					return (JsonConvert.DeserializeObject<T>(payload), response);
				}

				var error = payload.IsEmpty() ? null : JsonConvert.DeserializeObject<IgApiError>(payload);
				if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
					error?.Code?.Contains("allowance", StringComparison.OrdinalIgnoreCase) == true) && attempt < _maxAttempts)
				{
					var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
					response.Dispose();
					await Task.Delay(delay, cancellationToken);
					continue;
				}

				var statusCode = (int)response.StatusCode;
				var reason = error?.Code.IsEmpty(payload).IsEmpty(response.ReasonPhrase);
				response.Dispose();
				throw new InvalidOperationException($"IG {method} {path} failed ({statusCode}): {reason}");
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
			throw new InvalidOperationException($"IG login did not return the required {name} header.");
		return values.First();
	}

	private static string EncryptPassword(string password, IgEncryptionKey encryptionKey)
	{
		if (encryptionKey?.Key.IsEmpty() != false || encryptionKey.Timestamp <= 0)
			throw new InvalidOperationException("IG returned an invalid password-encryption key.");
		var publicKey = Convert.FromBase64String(encryptionKey.Key);
		using var rsa = RSA.Create();
		try
		{
			rsa.ImportSubjectPublicKeyInfo(publicKey, out _);
		}
		catch (CryptographicException)
		{
			rsa.ImportRSAPublicKey(publicKey, out _);
		}
		var clear = Encoding.UTF8.GetBytes($"{password}|{encryptionKey.Timestamp.ToString(CultureInfo.InvariantCulture)}");
		return Convert.ToBase64String(rsa.Encrypt(clear, RSAEncryptionPadding.Pkcs1));
	}

	protected override void DisposeManaged()
	{
		_requestGate.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}
}
