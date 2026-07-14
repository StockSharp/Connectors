namespace StockSharp.Schwab.Native;

sealed class SchwabClient : Disposable
{
	private readonly HttpClient _httpClient;

	public SchwabClient(Uri address, SecureString accessToken)
	{
		ArgumentNullException.ThrowIfNull(address);
		if (accessToken.IsEmpty())
			throw new ArgumentNullException(nameof(accessToken));

		_httpClient = new HttpClient { BaseAddress = address };
		_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.UnSecure());
		_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
	}

	protected override void DisposeManaged()
	{
		_httpClient.Dispose();
		base.DisposeManaged();
	}

	private async Task<string> SendRawAsync(HttpMethod method, string path, object body, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, path);
		if (body is not null)
			request.Content = new StringContent(JsonConvert.SerializeObject(body, Formatting.None), Encoding.UTF8, "application/json");

		using var response = await _httpClient.SendAsync(request, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new HttpRequestException(content.IsEmpty() ? response.ReasonPhrase : content, null, response.StatusCode);

		return content;
	}

	private async Task<T> SendAsync<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken)
	{
		var content = await SendRawAsync(method, path, body, cancellationToken);
		return content.IsEmpty() ? default : JsonConvert.DeserializeObject<T>(content);
	}

	public Task<AccountResponse[]> GetAccounts(CancellationToken cancellationToken)
		=> SendAsync<AccountResponse[]>(HttpMethod.Get, "trader/v1/accounts?fields=positions", null, cancellationToken);

	public Task<UserPreferences> GetUserPreferences(CancellationToken cancellationToken)
		=> SendAsync<UserPreferences>(HttpMethod.Get, "trader/v1/userPreference", null, cancellationToken);

	public Task<InstrumentLookupResponse> Lookup(string symbol, CancellationToken cancellationToken)
		=> SendAsync<InstrumentLookupResponse>(HttpMethod.Get, $"marketdata/v1/instruments?symbol={symbol.DataEscape()}&projection=symbol-search", null, cancellationToken);

	public Task<CandleResponse> GetCandles(string symbol, TimeSpan timeFrame, DateTime from, DateTime to, CancellationToken cancellationToken)
	{
		var frequency = timeFrame.ToSchwabFrequency();
		var periodType = frequency.Type == "daily" ? "year" : "day";
		var path = $"marketdata/v1/pricehistory?symbol={symbol.DataEscape()}&periodType={periodType}&frequencyType={frequency.Type}&frequency={frequency.Value}&startDate={(long)from.ToUnix(false)}&endDate={(long)to.ToUnix(false)}&needExtendedHoursData=true";
		return SendAsync<CandleResponse>(HttpMethod.Get, path, null, cancellationToken);
	}

	public Task<SchwabOrder[]> GetOrders(string account, DateTime from, DateTime to, CancellationToken cancellationToken)
		=> SendAsync<SchwabOrder[]>(HttpMethod.Get, $"trader/v1/accounts/{account.DataEscape()}/orders?fromEnteredTime={from.ToString("O").DataEscape()}&toEnteredTime={to.ToString("O").DataEscape()}", null, cancellationToken);

	public async Task<string> PlaceOrder(string account, SchwabOrderRequest order, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, $"trader/v1/accounts/{account.DataEscape()}/orders")
		{
			Content = new StringContent(JsonConvert.SerializeObject(order, Formatting.None), Encoding.UTF8, "application/json"),
		};
		using var response = await _httpClient.SendAsync(request, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new HttpRequestException(content.IsEmpty() ? response.ReasonPhrase : content, null, response.StatusCode);

		return response.Headers.Location?.Segments.LastOrDefault()?.Trim('/');
	}

	public async Task CancelOrder(string account, string orderId, CancellationToken cancellationToken)
		=> await SendRawAsync(HttpMethod.Delete, $"trader/v1/accounts/{account.DataEscape()}/orders/{orderId.DataEscape()}", null, cancellationToken);
}
