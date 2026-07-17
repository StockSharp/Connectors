namespace StockSharp.MatchTrader.Native;

internal sealed class MatchTraderApiException : InvalidOperationException
{
	public MatchTraderApiException(HttpStatusCode statusCode, string message)
		: base(message) => StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}

internal sealed class MatchTraderClient : BaseLogReceiver, IDisposable
{
	private readonly CookieContainer _cookies = new();
	private readonly HttpClient _auth;
	private HttpClient _trading;
	private readonly string _email;
	private readonly string _password;
	private readonly string _brokerId;
	private readonly string _requestedAccountId;
	private readonly int _maxAttempts;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private string _tradingToken;
	private string _systemUuid;
	private DateTimeOffset _expires;

	public MatchTraderClient(string address, string email, string password, string brokerId,
		string accountId, int maxAttempts)
	{
		_email = email.ThrowIfEmpty(nameof(email));
		_password = password.ThrowIfEmpty(nameof(password));
		_brokerId = brokerId.ThrowIfEmpty(nameof(brokerId));
		_requestedAccountId = accountId;
		_maxAttempts = Math.Max(1, maxAttempts);
		_auth = CreateClient(NormalizeAddress(address.ThrowIfEmpty(nameof(address))));
	}

	public MatchTraderAccount Account { get; private set; }

	public async Task<MatchTraderAccount> Login(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await AuthenticateLocked(cancellationToken);
			return Account;
		}
		finally
		{
			_gate.Release();
		}
	}

	public Task<MatchTraderInstrument[]> GetInstruments(CancellationToken cancellationToken)
		=> Send<MatchTraderInstrument[]>(HttpMethod.Get, ApiPath("effective-instruments"), null,
			true, cancellationToken);

	public Task<MatchTraderQuote[]> GetQuotes(IEnumerable<string> symbols,
		CancellationToken cancellationToken)
	{
		var joined = string.Join(',', symbols.Where(s => !s.IsEmpty()).Distinct(StringComparer.OrdinalIgnoreCase));
		return Send<MatchTraderQuote[]>(HttpMethod.Get,
			ApiPath("quotations") + "?symbols=" + Uri.EscapeDataString(joined), null, true,
			cancellationToken);
	}

	public Task<MatchTraderCandlesResponse> GetCandles(string symbol, string interval,
		DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
		=> Send<MatchTraderCandlesResponse>(HttpMethod.Get, ApiPath("candles") +
			"?symbol=" + Uri.EscapeDataString(symbol) +
			"&interval=" + Uri.EscapeDataString(interval) +
			"&from=" + Uri.EscapeDataString(FormatTime(from)) +
			"&to=" + Uri.EscapeDataString(FormatTime(to)), null, true, cancellationToken);

	public Task<MatchTraderBalance> GetBalance(CancellationToken cancellationToken)
		=> Send<MatchTraderBalance>(HttpMethod.Get, ApiPath("balance"), null, true,
			cancellationToken);

	public async Task<MatchTraderPosition[]> GetPositions(CancellationToken cancellationToken)
		=> (await Send<MatchTraderPositionsResponse>(HttpMethod.Get, ApiPath("open-positions"), null,
			true, cancellationToken))?.Positions ?? [];

	public async Task<MatchTraderOrder[]> GetOrders(CancellationToken cancellationToken)
		=> (await Send<MatchTraderOrdersResponse>(HttpMethod.Get, ApiPath("active-orders"), null,
			true, cancellationToken))?.Orders ?? [];

	public async Task<MatchTraderClosedPosition[]> GetClosedPositions(DateTimeOffset from,
		DateTimeOffset to, CancellationToken cancellationToken)
		=> (await Send<MatchTraderClosedResponse>(HttpMethod.Post, ApiPath("closed-positions"),
			new MatchTraderClosedRequest { From = from, To = to }, true, cancellationToken))
			?.Operations ?? [];

	public Task<MatchTraderOperationResponse> OpenPosition(MatchTraderOpenPositionRequest request,
		CancellationToken cancellationToken)
		=> Send<MatchTraderOperationResponse>(HttpMethod.Post, ApiPath("position/open"), request,
			false, cancellationToken);

	public Task<MatchTraderOperationResponse> CreatePendingOrder(MatchTraderPendingOrderRequest request,
		CancellationToken cancellationToken)
		=> Send<MatchTraderOperationResponse>(HttpMethod.Post, ApiPath("pending-order/create"), request,
			false, cancellationToken);

	public Task<MatchTraderOperationResponse> ClosePosition(MatchTraderClosePositionRequest request,
		bool partial, CancellationToken cancellationToken)
		=> Send<MatchTraderOperationResponse>(HttpMethod.Post,
			ApiPath(partial ? "position/close-partially" : "position/close"), request, false,
			cancellationToken);

	public Task<MatchTraderOperationResponse> CancelOrder(MatchTraderCancelOrderRequest request,
		CancellationToken cancellationToken)
		=> Send<MatchTraderOperationResponse>(HttpMethod.Post, ApiPath("pending-order/cancel"), request,
			false, cancellationToken);

	public Task Ping(CancellationToken cancellationToken)
		=> Send<MatchTraderBalance>(HttpMethod.Get, ApiPath("balance"), null, true,
			cancellationToken);

	private async Task<T> Send<T>(HttpMethod method, string path, object body, bool safe,
		CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (_expires <= DateTimeOffset.UtcNow.AddMinutes(2))
				await AuthenticateLocked(cancellationToken);

			for (var attempt = 1; ; attempt++)
			{
				using var request = new HttpRequestMessage(method, path);
				request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
				request.Headers.TryAddWithoutValidation("Auth-trading-api", _tradingToken);
				if (body != null)
					request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8,
						"application/json");

				var response = await _trading.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
				var payload = await response.Content.ReadAsStringAsync(cancellationToken);
				if (response.IsSuccessStatusCode)
				{
					response.Dispose();
					if (payload.IsEmpty())
						throw new InvalidOperationException(
							$"Match-Trader {method} {path} returned an empty response.");
					return JsonConvert.DeserializeObject<T>(payload);
				}

				if (safe && response.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
				{
					response.Dispose();
					await AuthenticateLocked(cancellationToken);
					continue;
				}
				if (safe && (response.StatusCode == HttpStatusCode.TooManyRequests ||
					(int)response.StatusCode >= 500) && attempt < _maxAttempts)
				{
					var delay = response.Headers.RetryAfter?.Delta ??
						TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
					response.Dispose();
					await Task.Delay(delay, cancellationToken);
					continue;
				}

				var status = response.StatusCode;
				var reason = payload.IsEmpty(response.ReasonPhrase);
				response.Dispose();
				throw new MatchTraderApiException(status,
					$"Match-Trader {method} {path} failed ({(int)status}): {reason}");
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task AuthenticateLocked(CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "manager/mtr-login")
		{
			Content = new StringContent(JsonConvert.SerializeObject(new MatchTraderLoginRequest
			{
				Email = _email,
				Password = _password,
				BrokerId = _brokerId,
			}), Encoding.UTF8, "application/json"),
		};
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		var response = await _auth.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
			cancellationToken);
		var payload = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new MatchTraderApiException(response.StatusCode,
				$"Match-Trader login failed ({(int)response.StatusCode}): {payload}");

		var login = JsonConvert.DeserializeObject<MatchTraderLoginResponse>(payload)
			?? throw new InvalidOperationException("Match-Trader returned no login response.");
		var accounts = login.TradingAccounts ?? login.Accounts ?? [];
		if (login.SelectedTradingAccount != null &&
			!accounts.Any(a => a.Uuid.EqualsIgnoreCase(login.SelectedTradingAccount.Uuid)))
			accounts = [.. accounts, login.SelectedTradingAccount];

		MatchTraderAccount selected;
		if (_requestedAccountId.IsEmpty())
		{
			selected = login.SelectedTradingAccount;
			if (selected == null && accounts.Length == 1)
				selected = accounts[0];
			if (selected == null)
				throw new InvalidOperationException(accounts.Length == 0
					? "Match-Trader returned no trading accounts."
					: "Match-Trader returned multiple accounts. Configure AccountId explicitly.");
		}
		else
		{
			selected = accounts.FirstOrDefault(a =>
				a.TradingAccountId.EqualsIgnoreCase(_requestedAccountId) ||
				a.Uuid.EqualsIgnoreCase(_requestedAccountId))
				?? throw new InvalidOperationException(
					$"Match-Trader account '{_requestedAccountId}' was not found.");
		}

		_tradingToken = selected.TradingApiToken.ThrowIfEmpty(nameof(selected.TradingApiToken));
		_systemUuid = selected.Offer?.System?.Uuid.ThrowIfEmpty(nameof(_systemUuid));
		var domain = selected.Offer?.System?.TradingApiDomain;
		if (domain.IsEmpty())
			domain = _auth.BaseAddress.ToString();
		var address = NormalizeAddress(domain);
		if (_trading?.BaseAddress != address)
		{
			_trading?.Dispose();
			_trading = CreateClient(address);
		}
		Account = selected;
		_expires = DateTimeOffset.UtcNow.AddMinutes(50);
	}

	private string ApiPath(string suffix)
		=> $"mtr-api/{Uri.EscapeDataString(_systemUuid)}/{suffix}";

	private HttpClient CreateClient(Uri address)
		=> new(new HttpClientHandler { CookieContainer = _cookies, UseCookies = true })
		{
			BaseAddress = address,
			Timeout = TimeSpan.FromSeconds(30),
		};

	private static Uri NormalizeAddress(string address)
	{
		if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
			!address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			address = "https://" + address;
		return new(address.TrimEnd('/') + "/");
	}

	private static string FormatTime(DateTimeOffset value)
		=> value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

	protected override void DisposeManaged()
	{
		_gate.Dispose();
		_auth.Dispose();
		_trading?.Dispose();
		base.DisposeManaged();
	}
}
