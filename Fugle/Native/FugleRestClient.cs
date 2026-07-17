namespace StockSharp.Fugle.Native;

sealed class FugleRestClient : BaseLogReceiver
{
	private const string _baseUrl = "https://api.fugle.tw/marketdata/v1.0/";
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly HttpClient _httpClient = new() { BaseAddress = new(_baseUrl) };
	private readonly SemaphoreSlim _securitiesLock = new(1, 1);
	private FugleSecurityInfo[] _securities;

	public FugleRestClient(SecureString apiKey)
	{
		_httpClient.DefaultRequestHeaders.Add("X-API-KEY", apiKey.ThrowIfEmpty(nameof(apiKey)).UnSecure());
		_httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Fugle/1.0");
	}

	public override string Name => nameof(Fugle) + "_" + nameof(FugleRestClient);

	protected override void DisposeManaged()
	{
		_httpClient.Dispose();
		_securitiesLock.Dispose();
		base.DisposeManaged();
	}

	public Task Validate(CancellationToken cancellationToken)
		=> Send<FugleStockTicker>("stock/intraday/ticker/0050", cancellationToken);

	public async Task<FugleSecurityInfo[]> GetSecurities(CancellationToken cancellationToken)
	{
		if (_securities != null)
			return _securities;

		await _securitiesLock.WaitAsync(cancellationToken);
		try
		{
			if (_securities != null)
				return _securities;

			var stockPages = new List<FugleTickerListResponse>();
			foreach (var type in new[] { "EQUITY", "INDEX", "WARRANT" })
			{
				foreach (var exchange in new[] { "TWSE", "TPEx" })
					stockPages.Add(await GetTickerList("stock", type, exchange, null, cancellationToken));
			}

			var futuresPages = new List<FugleTickerListResponse>();
			foreach (var type in new[] { "FUTURE", "OPTION" })
			{
				foreach (var session in new[] { "REGULAR", "AFTERHOURS" })
					futuresPages.Add(await GetTickerList("futopt", type, "TAIFEX", session, cancellationToken));
			}

			var securities = new List<FugleSecurityInfo>();

			foreach (var page in stockPages)
			{
				foreach (var ticker in page.Data ?? [])
				{
					if (ticker.Symbol.IsEmpty())
						continue;
					securities.Add(new()
					{
						Kind = FugleAssetKinds.Stock,
						TickerType = ticker.Type.IsEmpty(page.Type),
						Exchange = page.Exchange,
						Market = page.Market,
						Symbol = ticker.Symbol,
						Name = ticker.Name,
					});
				}
			}

			foreach (var page in futuresPages)
			{
				foreach (var ticker in page.Data ?? [])
				{
					if (ticker.Symbol.IsEmpty())
						continue;
					securities.Add(new()
					{
						Kind = FugleAssetKinds.FuturesOptions,
						TickerType = ticker.Type.IsEmpty(page.Type),
						Exchange = page.Exchange,
						Session = page.Session,
						Symbol = ticker.Symbol,
						Name = ticker.Name,
						ContractType = ticker.ContractType,
						ReferencePrice = ticker.ReferencePrice,
						StartDate = ticker.StartDate,
						EndDate = ticker.EndDate,
						SettlementDate = ticker.SettlementDate,
					});
				}
			}

			_securities = [.. securities
				.GroupBy(security => security.ToNativeKey(), StringComparer.OrdinalIgnoreCase)
				.Select(group => group.First())];
			return _securities;
		}
		finally
		{
			_securitiesLock.Release();
		}
	}

	public async Task<FugleCandle[]> GetCandles(FugleSecurityInfo security, TimeSpan timeFrame,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var interval = timeFrame.ToFugleTimeFrame(security.Kind);
		string path;

		if (security.Kind == FugleAssetKinds.Stock)
		{
			path = $"stock/historical/candles/{Uri.EscapeDataString(security.Symbol)}" +
				$"?timeframe={interval}&fields=open%2Chigh%2Clow%2Cclose%2Cvolume&sort=asc";
			if (interval is "D" or "W" or "M")
			{
				if (from != null)
					path += $"&from={NormalizeUtc(from.Value):yyyy-MM-dd}";
				if (to != null)
					path += $"&to={NormalizeUtc(to.Value):yyyy-MM-dd}";
			}
		}
		else
		{
			path = $"futopt/intraday/candles/{Uri.EscapeDataString(security.Symbol)}?timeframe={interval}";
			if (security.IsAfterHours)
				path += "&session=afterhours";
		}

		var response = await Send<FugleCandleResponse>(path, cancellationToken);
		return response.Data ?? [];
	}

	private Task<FugleTickerListResponse> GetTickerList(string market, string type, string exchange,
		string session, CancellationToken cancellationToken)
	{
		var path = $"{market}/intraday/tickers?type={Uri.EscapeDataString(type)}&exchange={Uri.EscapeDataString(exchange)}";
		if (!session.IsEmpty())
			path += $"&session={Uri.EscapeDataString(session)}";
		return Send<FugleTickerListResponse>(path, cancellationToken);
	}

	private async Task<T> Send<T>(string path, CancellationToken cancellationToken)
		where T : class
	{
		this.AddVerboseLog("Fugle GET {0}.", path);
		using var request = new HttpRequestMessage(HttpMethod.Get, path);
		using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			var error = content.IsEmpty() ? null : JsonConvert.DeserializeObject<FugleErrorResponse>(content, _jsonSettings);
			throw new HttpRequestException($"Fugle {path} returned HTTP {(int)response.StatusCode}: " +
				error?.Message.IsEmpty(response.ReasonPhrase));
		}
		if (content.IsEmpty())
			throw new InvalidOperationException($"Fugle returned an empty response for {path}.");

		return JsonConvert.DeserializeObject<T>(content, _jsonSettings)
			?? throw new InvalidDataException($"Fugle returned an invalid response for {path}.");
	}

	private static DateTime NormalizeUtc(DateTime value)
		=> value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: value.ToUniversalTime();
}
