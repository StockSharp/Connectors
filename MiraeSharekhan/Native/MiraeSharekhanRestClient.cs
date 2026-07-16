namespace StockSharp.MiraeSharekhan.Native;

internal sealed class MiraeSharekhanRestClient : BaseLogReceiver
{
	private readonly HttpClient _http;
	private readonly int _maxAttempts;
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly SemaphoreSlim _masterGate = new(1, 1);
	private readonly Dictionary<string, MiraeSharekhanInstrument[]> _instrumentCache =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	public MiraeSharekhanRestClient(string apiKey, string accessToken, string vendorKey, int maxAttempts)
	{
		apiKey.ThrowIfEmpty(nameof(apiKey));
		accessToken.ThrowIfEmpty(nameof(accessToken));
		_maxAttempts = Math.Max(1, maxAttempts);
		var handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate |
				DecompressionMethods.Brotli,
		};
		_http = new(handler)
		{
			BaseAddress = new("https://api.sharekhan.com/skapi/services/"),
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.TryAddWithoutValidation("api-key", apiKey);
		_http.DefaultRequestHeaders.TryAddWithoutValidation("access-token", accessToken);
		_http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", accessToken);
		if (!vendorKey.IsEmpty())
			_http.DefaultRequestHeaders.TryAddWithoutValidation("vendor-key", vendorKey);
		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public override string Name => nameof(MiraeSharekhan) + "_" + nameof(MiraeSharekhanRestClient);

	public async Task<MiraeSharekhanInstrument[]> GetInstruments(string exchange,
		CancellationToken cancellationToken)
	{
		exchange = exchange.ThrowIfEmpty(nameof(exchange)).ToUpperInvariant();
		if (_instrumentCache.TryGetValue(exchange, out var cached))
			return cached;

		await _masterGate.WaitAsync(cancellationToken);
		try
		{
			if (_instrumentCache.TryGetValue(exchange, out cached))
				return cached;
			var content = await Get($"master/{Uri.EscapeDataString(exchange)}", cancellationToken);
			var instruments = DeserializeItems<MiraeSharekhanInstrument>(content);
			foreach (var instrument in instruments)
				instrument.Exchange = instrument.Exchange.IsEmpty(exchange).ToUpperInvariant();
			_instrumentCache[exchange] = instruments;
			return instruments;
		}
		finally
		{
			_masterGate.Release();
		}
	}

	public async Task<MiraeSharekhanHistoricalCandle[]> GetCandles(string exchange, long scripCode,
		string interval, CancellationToken cancellationToken)
	{
		var path = $"historical/{Uri.EscapeDataString(exchange)}/" +
			scripCode.ToString(CultureInfo.InvariantCulture) + $"/{Uri.EscapeDataString(interval)}";
		return DeserializeItems<MiraeSharekhanHistoricalCandle>(await Get(path, cancellationToken));
	}

	public async Task<MiraeSharekhanOrderResponse> SubmitOrder(MiraeSharekhanOrderRequest order,
		CancellationToken cancellationToken)
	{
		var response = JsonConvert.DeserializeObject<MiraeSharekhanOrderResponse>(
			await Post("orders", order, cancellationToken), _jsonSettings)
			?? throw new InvalidDataException("Mirae Asset Sharekhan returned an invalid order response.");
		ThrowIfFailed(response);
		return response;
	}

	public async Task<MiraeSharekhanOrder[]> GetOrders(string customerId,
		CancellationToken cancellationToken)
		=> DeserializeItems<MiraeSharekhanOrder>(await Get(
			$"reports/{Uri.EscapeDataString(customerId)}", cancellationToken));

	public async Task<MiraeSharekhanTrade[]> GetTrades(string customerId,
		CancellationToken cancellationToken)
		=> DeserializeItems<MiraeSharekhanTrade>(await Get(
			$"trades/{Uri.EscapeDataString(customerId)}", cancellationToken));

	public async Task<MiraeSharekhanHolding[]> GetHoldings(string customerId,
		CancellationToken cancellationToken)
		=> DeserializeItems<MiraeSharekhanHolding>(await Get(
			$"holdings/{Uri.EscapeDataString(customerId)}", cancellationToken));

	public async Task<MiraeSharekhanFunds> GetFunds(string exchange, string customerId,
		CancellationToken cancellationToken)
		=> DeserializeObject<MiraeSharekhanFunds>(await Get(
			$"limitstmt/{Uri.EscapeDataString(exchange)}/{Uri.EscapeDataString(customerId)}",
			cancellationToken));

	private Task<string> Get(string path, CancellationToken cancellationToken)
		=> Send(HttpMethod.Get, path, null, cancellationToken);

	private Task<string> Post<T>(string path, T body, CancellationToken cancellationToken)
		where T : class
		=> Send(HttpMethod.Post, path, JsonConvert.SerializeObject(body, _jsonSettings), cancellationToken);

	private async Task<string> Send(HttpMethod method, string path, string body,
		CancellationToken cancellationToken)
	{
		await _requestGate.WaitAsync(cancellationToken);
		try
		{
			for (var attempt = 1; ; attempt++)
			{
				using var request = new HttpRequestMessage(method, path);
				if (body != null)
					request.Content = new StringContent(body, Encoding.UTF8, "application/json");
				try
				{
					using var response = await _http.SendAsync(request,
						HttpCompletionOption.ResponseHeadersRead, cancellationToken);
					var content = await response.Content.ReadAsStringAsync(cancellationToken);
					if (!response.IsSuccessStatusCode)
						throw CreateException(response.StatusCode, content);
					if (content.IsEmpty())
						throw new InvalidDataException("Mirae Asset Sharekhan returned an empty response.");
					return content;
				}
				catch (Exception ex) when (method == HttpMethod.Get && attempt < _maxAttempts && IsTransient(ex))
				{
					await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
				}
			}
		}
		finally
		{
			_requestGate.Release();
		}
	}

	private T[] DeserializeItems<T>(string content)
		where T : class
	{
		var trimmed = content.TrimStart();
		if (trimmed.StartsWith("[", StringComparison.Ordinal))
			return JsonConvert.DeserializeObject<T[]>(content, _jsonSettings) ?? [];

		var response = JsonConvert.DeserializeObject<MiraeSharekhanItemsResponse<T>>(content, _jsonSettings)
			?? throw new InvalidDataException("Mirae Asset Sharekhan returned an invalid response.");
		ThrowIfFailed(response);
		return response.GetItems();
	}

	private T DeserializeObject<T>(string content)
		where T : class
	{
		var response = JsonConvert.DeserializeObject<MiraeSharekhanObjectResponse<T>>(content, _jsonSettings)
			?? throw new InvalidDataException("Mirae Asset Sharekhan returned an invalid response.");
		ThrowIfFailed(response);
		return response.GetValue() ?? JsonConvert.DeserializeObject<T>(content, _jsonSettings);
	}

	private static void ThrowIfFailed(MiraeSharekhanResponse response)
	{
		if (!response.IsFailed())
			return;
		var code = response.GetErrorCode();
		var message = response.Message.IsEmpty("Unknown Mirae Asset Sharekhan API error.");
		throw new InvalidOperationException(code.IsEmpty() ? message : $"{code}: {message}");
	}

	private static Exception CreateException(HttpStatusCode statusCode, string content)
	{
		var message = content;
		try
		{
			var response = JsonConvert.DeserializeObject<MiraeSharekhanResponse>(content);
			if (response != null)
			{
				var code = response.GetErrorCode();
				message = response.Message.IsEmpty(content);
				if (!code.IsEmpty())
					message = $"{code}: {message}";
			}
		}
		catch (JsonException)
		{
		}
		return new HttpRequestException(
			$"Mirae Asset Sharekhan request failed ({(int)statusCode}): {message}", null, statusCode);
	}

	private static bool IsTransient(Exception exception)
		=> exception is HttpRequestException http &&
			(http.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
				(int?)http.StatusCode >= 500);

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		_masterGate.Dispose();
		base.DisposeManaged();
	}
}
