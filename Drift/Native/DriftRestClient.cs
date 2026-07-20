namespace StockSharp.Drift.Native;

sealed class DriftRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 16 * 1024 * 1024;
	private readonly HttpClient _dataClient;
	private readonly HttpClient _dlobClient;
	private readonly SemaphoreSlim _dataGate = new(1, 1);
	private readonly SemaphoreSlim _dlobGate = new(1, 1);
	private readonly JsonSerializerSettings _serializerSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextDataRequestTime;
	private DateTime _nextDlobRequestTime;
	private bool _isDisposed;

	public DriftRestClient(string dataEndpoint, string dlobEndpoint)
	{
		_dataClient = CreateClient(dataEndpoint.NormalizeHttpEndpoint(
			nameof(dataEndpoint)));
		_dlobClient = CreateClient(dlobEndpoint.NormalizeHttpEndpoint(
			nameof(dlobEndpoint)));
	}

	public override string Name => "Drift_REST";

	public ValueTask<DriftMarketsResponse> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> GetDataAsync<DriftMarketsResponse>("stats/markets",
			cancellationToken);

	public ValueTask<DriftCandlesResponse> GetCandlesAsync(string symbol,
		string resolution, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(limit));
		var path = $"market/{Escape(symbol)}/candles/{Escape(resolution)}" +
			$"?limit={limit.ToString(CultureInfo.InvariantCulture)}";
		if (from is DateTime start)
			path += "&startTs=" + start.ToDriftSeconds().ToString(
				CultureInfo.InvariantCulture);
		if (to is DateTime end)
			path += "&endTs=" + end.ToDriftSeconds().ToString(
				CultureInfo.InvariantCulture);
		return GetDataAsync<DriftCandlesResponse>(path, cancellationToken);
	}

	public async ValueTask<DriftTrade[]> GetTradesAsync(string symbol,
		int limit, CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(limit));
		var result = new List<DriftTrade>(limit);
		string page = null;
		while (result.Count < limit)
		{
			var size = Math.Min(20, limit - result.Count);
			var path = $"market/{Escape(symbol)}/trades?limit=" +
				size.ToString(CultureInfo.InvariantCulture);
			if (!page.IsEmpty())
				path += "&page=" + Escape(page);
			var response = await GetDataAsync<DriftPagedResponse<DriftTrade>>(
				path, cancellationToken);
			var records = response?.Records ?? [];
			result.AddRange(records.Where(static trade => trade is not null));
			page = response?.Meta?.NextPage;
			if (records.Length == 0 || page.IsEmpty())
				break;
		}
		return [.. result.Take(limit)];
	}

	public ValueTask<DriftDlobBook> GetOrderBookAsync(string symbol,
		int depth, CancellationToken cancellationToken)
	{
		if (depth is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(depth));
		return GetDlobAsync<DriftDlobBook>(
			$"l2?marketName={Escape(symbol)}&depth={depth}" +
			"&includeVamm=true&includeIndicative=true", cancellationToken);
	}

	public ValueTask<DriftAuthorityAccountsResponse> GetAccountsAsync(
		string authority, CancellationToken cancellationToken)
		=> GetDataAsync<DriftAuthorityAccountsResponse>(
			$"authority/{Escape(authority.NormalizePublicKey())}/accounts",
			cancellationToken);

	public ValueTask<DriftUserResponse> GetUserAsync(string account,
		CancellationToken cancellationToken)
		=> GetDataAsync<DriftUserResponse>(
			$"user/{Escape(account.NormalizePublicKey())}", cancellationToken);

	public async ValueTask<DriftTrade[]> GetUserTradesAsync(string account,
		int limit, CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(limit));
		var result = new List<DriftTrade>(limit);
		string page = null;
		while (result.Count < limit)
		{
			var path = $"user/{Escape(account.NormalizePublicKey())}/trades";
			if (!page.IsEmpty())
				path += "?page=" + Escape(page);
			var response = await GetDataAsync<DriftPagedResponse<DriftTrade>>(
				path, cancellationToken);
			var records = response?.Records ?? [];
			result.AddRange(records.Where(static trade => trade is not null));
			page = response?.Meta?.NextPage;
			if (records.Length == 0 || page.IsEmpty())
				break;
		}
		return [.. result.Take(limit)];
	}

	public ValueTask<DriftPreparedTransactionResponse> PrepareOrderAsync(
		DriftPlaceOrderRequest request, CancellationToken cancellationToken)
		=> PostDataAsync<DriftPlaceOrderRequest,
			DriftPreparedTransactionResponse>("tx/order/place", request, true,
			cancellationToken);

	public ValueTask<DriftPreparedTransactionResponse> PrepareCancelAsync(
		DriftCancelOrderRequest request, CancellationToken cancellationToken)
		=> PostDataAsync<DriftCancelOrderRequest,
			DriftPreparedTransactionResponse>("tx/order/cancel", request, true,
			cancellationToken);

	public ValueTask<DriftExecutedTransactionResponse> ExecuteAsync(
		DriftExecuteTransactionRequest request,
		CancellationToken cancellationToken)
		=> PostDataAsync<DriftExecuteTransactionRequest,
			DriftExecutedTransactionResponse>("tx/execute", request, false,
			cancellationToken);

	private ValueTask<TResponse> GetDataAsync<TResponse>(string path,
		CancellationToken cancellationToken)
		=> SendAsync<TResponse>(_dataClient, _dataGate, true,
			HttpMethod.Get, path, null, true, cancellationToken);

	private ValueTask<TResponse> GetDlobAsync<TResponse>(string path,
		CancellationToken cancellationToken)
		=> SendAsync<TResponse>(_dlobClient, _dlobGate, false,
			HttpMethod.Get, path, null, true, cancellationToken);

	private ValueTask<TResponse> PostDataAsync<TRequest, TResponse>(string path,
		TRequest request, bool isRetryAllowed,
		CancellationToken cancellationToken)
		=> SendAsync<TResponse>(_dataClient, _dataGate, true,
			HttpMethod.Post, path,
			JsonConvert.SerializeObject(request, _serializerSettings),
			isRetryAllowed, cancellationToken);

	private async ValueTask<TResponse> SendAsync<TResponse>(HttpClient client,
		SemaphoreSlim gate, bool isDataLane, HttpMethod method, string path,
		string json, bool isRetryAllowed, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		for (var attempt = 0; ; attempt++)
		{
			await WaitAsync(gate, isDataLane, cancellationToken);
			using var request = new HttpRequestMessage(method, path);
			if (json is not null)
				request.Content = new StringContent(json, Encoding.UTF8,
					"application/json");
			using var response = await client.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (isRetryAllowed && attempt < 2 &&
				(response.StatusCode == HttpStatusCode.TooManyRequests ||
					(int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)),
					cancellationToken);
				continue;
			}
			var body = await ReadBodyAsync(response.Content, path,
				cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw CreateApiException(response.StatusCode, body);
			try
			{
				return JsonConvert.DeserializeObject<TResponse>(body,
					_serializerSettings) ?? throw new DriftApiException(
						"Drift returned an empty JSON response.");
			}
			catch (JsonException error)
			{
				throw new DriftApiException(
					"Drift returned malformed JSON.", error);
			}
		}
	}

	private async ValueTask WaitAsync(SemaphoreSlim gate, bool isDataLane,
		CancellationToken cancellationToken)
	{
		await gate.WaitAsync(cancellationToken);
		try
		{
			var next = isDataLane ? _nextDataRequestTime : _nextDlobRequestTime;
			var delay = next - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			if (isDataLane)
				_nextDataRequestTime = DateTime.UtcNow +
					TimeSpan.FromMilliseconds(60);
			else
				_nextDlobRequestTime = DateTime.UtcNow +
					TimeSpan.FromMilliseconds(60);
		}
		finally
		{
			gate.Release();
		}
	}

	private static DriftApiException CreateApiException(HttpStatusCode status,
		string body)
	{
		DriftApiError error = null;
		try
		{
			error = JsonConvert.DeserializeObject<DriftApiError>(body);
		}
		catch (JsonException)
		{
		}
		var message = error?.Message ?? error?.Error;
		if (message.IsEmpty())
			message = body.IsEmpty()
				? "empty response"
				: body[..Math.Min(body.Length, 1024)];
		return new DriftApiException(
			$"Drift HTTP {(int)status} ({status}): {message}");
	}

	private static HttpClient CreateClient(string endpoint)
	{
		var client = new HttpClient
		{
			BaseAddress = new Uri(endpoint, UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(45),
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Drift/1.0");
		return client;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		string path, CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new DriftApiException(
				$"Drift response for '{path}' exceeds the safety limit.");
		await using var source = await content.ReadAsStreamAsync(
			cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseLength)
				throw new DriftApiException(
					$"Drift response for '{path}' exceeds the safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_dataClient.Dispose();
		_dlobClient.Dispose();
		_dataGate.Dispose();
		_dlobGate.Dispose();
		base.DisposeManaged();
	}
}
