namespace StockSharp.Fxcm.Native;

internal sealed class FxcmRestClient : BaseLogReceiver, IDisposable
{
	private readonly HttpClient _http;
	private readonly int _maxAttempts;
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.DateTime,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		NullValueHandling = NullValueHandling.Ignore,
	};

	public FxcmRestClient(bool isDemo, string bearer, int maxAttempts)
	{
		bearer.ThrowIfEmpty(nameof(bearer));
		_maxAttempts = Math.Max(1, maxAttempts);
		_http = new()
		{
			BaseAddress = new(isDemo ? "https://api-demo.fxcm.com/" : "https://api.fxcm.com/"),
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.Authorization = new("Bearer", bearer);
	}

	public override string Name => nameof(Fxcm) + "_" + nameof(FxcmRestClient);

	public async Task<FxcmInstrument[]> GetInstruments(CancellationToken cancellationToken)
		=> (await Get<FxcmInstrumentsData, FxcmEmptyData>("trading/get_instruments/", new(),
			cancellationToken)).Instruments ?? [];

	public Task SubscribePair(string symbol, CancellationToken cancellationToken)
		=> PostEmpty("subscribe/", new FxcmPairsRequest { Pairs = symbol }, cancellationToken);

	public Task UnsubscribePair(string symbol, CancellationToken cancellationToken)
		=> PostEmpty("unsubscribe/", new FxcmPairsRequest { Pairs = symbol }, cancellationToken);

	public Task SubscribeModel(string model, CancellationToken cancellationToken)
		=> PostEmpty("trading/subscribe/", new FxcmModelRequest { Models = model }, cancellationToken);

	public Task UnsubscribeModel(string model, CancellationToken cancellationToken)
		=> PostEmpty("trading/unsubscribe/", new FxcmModelRequest { Models = model }, cancellationToken);

	public async Task<FxcmOffer[]> GetOffers(CancellationToken cancellationToken)
		=> ((await GetModels(FxcmModelNames.Offer, cancellationToken)).Offers ?? [])
			.Where(IsActual).ToArray();

	public async Task<FxcmOrder[]> GetOrders(CancellationToken cancellationToken)
		=> ((await GetModels(FxcmModelNames.Order, cancellationToken)).Orders ?? [])
			.Where(IsActual).ToArray();

	public async Task<FxcmPosition[]> GetOpenPositions(CancellationToken cancellationToken)
		=> ((await GetModels(FxcmModelNames.OpenPosition, cancellationToken)).OpenPositions ?? [])
			.Where(IsActual).ToArray();

	public async Task<FxcmPosition[]> GetClosedPositions(CancellationToken cancellationToken)
		=> ((await GetModels(FxcmModelNames.ClosedPosition, cancellationToken)).ClosedPositions ?? [])
			.Where(IsActual).ToArray();

	public async Task<FxcmAccount[]> GetAccounts(CancellationToken cancellationToken)
		=> ((await GetModels(FxcmModelNames.Account, cancellationToken)).Accounts ?? [])
			.Where(IsActual).ToArray();

	public async Task<FxcmCandle[]> GetCandles(long offerId, string period, int count,
		DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
	{
		var request = new FxcmCandlesRequest
		{
			Count = Math.Clamp(count, 1, 10000),
			From = from?.ToUniversalTime().ToUnixTimeSeconds(),
			To = to?.ToUniversalTime().ToUnixTimeSeconds(),
		};
		var path = $"candles/{offerId.ToString(CultureInfo.InvariantCulture)}/{Uri.EscapeDataString(period)}/";
		var response = await Send<FxcmCandlesResponse, FxcmCandlesRequest>(HttpMethod.Get, path, request,
			true, cancellationToken);
		EnsureExecuted(response?.Response);
		return response?.Candles ?? [];
	}

	public async Task<FxcmOrderResult> OpenTrade(FxcmOpenTradeRequest request,
		CancellationToken cancellationToken)
		=> await Post<FxcmOrderResult, FxcmOpenTradeRequest>("trading/open_trade/", request, cancellationToken);

	public async Task<FxcmOrderResult> CreateEntryOrder(FxcmEntryOrderRequest request,
		CancellationToken cancellationToken)
		=> await Post<FxcmOrderResult, FxcmEntryOrderRequest>("trading/create_entry_order/", request,
			cancellationToken);

	public Task ChangeOrder(FxcmChangeOrderRequest request, CancellationToken cancellationToken)
		=> PostEmpty("trading/change_order/", request, cancellationToken);

	public Task DeleteOrder(long orderId, CancellationToken cancellationToken)
		=> PostEmpty("trading/delete_order/", new FxcmDeleteOrderRequest { OrderId = orderId },
			cancellationToken);

	public async Task<FxcmOrderResult> CloseTrade(FxcmCloseTradeRequest request,
		CancellationToken cancellationToken)
		=> await Post<FxcmOrderResult, FxcmCloseTradeRequest>("trading/close_trade/", request,
			cancellationToken);

	private Task<FxcmModelsData> GetModels(string model, CancellationToken cancellationToken)
		=> Get<FxcmModelsData, FxcmModelRequest>("trading/get_model/",
			new() { Models = model }, cancellationToken);

	private async Task<TData> Get<TData, TRequest>(string path, TRequest request,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		var envelope = await Send<FxcmApiEnvelope<TData>, TRequest>(HttpMethod.Get, path, request, true,
			cancellationToken);
		EnsureExecuted(envelope?.Response);
		return envelope == null ? default : envelope.Data;
	}

	private async Task<TData> Post<TData, TRequest>(string path, TRequest request,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		var envelope = await Send<FxcmApiEnvelope<TData>, TRequest>(HttpMethod.Post, path, request, false,
			cancellationToken);
		EnsureExecuted(envelope?.Response);
		return envelope == null ? default : envelope.Data;
	}

	private async Task PostEmpty<TRequest>(string path, TRequest request, CancellationToken cancellationToken)
		where TRequest : class
		=> await Post<FxcmEmptyData, TRequest>(path, request, cancellationToken);

	private async Task<TResponse> Send<TResponse, TRequest>(HttpMethod method, string path, TRequest request,
		bool isQuery, CancellationToken cancellationToken)
		where TRequest : class
	{
		await _requestGate.WaitAsync(cancellationToken);
		try
		{
			for (var attempt = 1; ; attempt++)
			{
				var query = isQuery ? FxcmFormEncoder.ToQueryString(request) : null;
				using var message = new HttpRequestMessage(method,
					query.IsEmpty() ? path : $"{path}?{query}");
				if (!isQuery)
					message.Content = new FormUrlEncodedContent(FxcmFormEncoder.Encode(request));

				try
				{
					using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
						cancellationToken);
					var content = await response.Content.ReadAsStringAsync(cancellationToken);
					if (!response.IsSuccessStatusCode)
						throw CreateHttpException(response.StatusCode, content);
					if (content.IsEmpty())
						throw new InvalidDataException("FXCM returned an empty response.");
					return JsonConvert.DeserializeObject<TResponse>(content, _jsonSettings)
						?? throw new InvalidDataException("FXCM returned an invalid response.");
				}
				catch (Exception ex) when (attempt < _maxAttempts && IsTransient(ex))
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

	private static bool IsActual(FxcmTableRow row)
		=> row != null && row.IsTotal != true;

	private static bool IsTransient(Exception exception)
		=> exception is HttpRequestException http &&
			(http.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
			 (int?)http.StatusCode >= 500);

	private static HttpRequestException CreateHttpException(HttpStatusCode statusCode, string content)
	{
		var message = content;
		try
		{
			var envelope = JsonConvert.DeserializeObject<FxcmApiEnvelope<FxcmEmptyData>>(content);
			message = GetErrorMessage(envelope?.Response?.Error).IsEmpty(content);
		}
		catch (JsonException)
		{
		}
		return new HttpRequestException($"FXCM request failed ({(int)statusCode}): {message}", null, statusCode);
	}

	private static void EnsureExecuted(FxcmApiResponse response)
	{
		if (response?.IsExecuted == true)
			return;
		throw new InvalidOperationException(GetErrorMessage(response?.Error)
			.IsEmpty("FXCM did not execute the request."));
	}

	private static string GetErrorMessage(string error)
	{
		if (error.IsEmpty() || !error.TrimStart().StartsWith('{'))
			return error;
		try
		{
			var details = JsonConvert.DeserializeObject<FxcmErrorDetails>(error);
			return details?.Text.IsEmpty(details?.Message).IsEmpty(error);
		}
		catch (JsonException)
		{
			return error;
		}
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}
}
