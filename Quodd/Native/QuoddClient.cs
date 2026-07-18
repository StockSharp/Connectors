namespace StockSharp.Quodd.Native;

sealed class QuoddClient : IAsyncDisposable
{
	private static readonly string[] _tickerInfoFields =
	[
		"name",
		"shares_outstanding_weighted_adj",
		"sector",
	];

	private readonly QuoddTokenProvider _tokens;
	private readonly GrpcChannel _channel;
	private readonly SnapService.SnapServiceClient _snaps;
	private readonly TickerInfoService.TickerInfoServiceClient _tickerInfo;
	private readonly OptionLookupService.OptionLookupServiceClient _options;
	private readonly QuoddStreamGroup _equities;
	private readonly QuoddStreamGroup _optionQuotes;
	private readonly int _maxAttempts;
	private bool _isStarted;

	public QuoddClient(Uri address, QuoddTokenProvider tokens, int maxAttempts)
	{
		if (address == null || !address.IsAbsoluteUri || address.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException("QUODD gRPC address must be an absolute HTTPS URI.", nameof(address));

		_tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
		_maxAttempts = Math.Max(2, maxAttempts);
		_channel = GrpcChannel.ForAddress(address);
		_snaps = new(_channel);
		_tickerInfo = new(_channel);
		_options = new(_channel);
		_equities = new(this, QuoddAssetTypes.Equities, _maxAttempts);
		_optionQuotes = new(this, QuoddAssetTypes.Options, _maxAttempts);
		_equities.SnapReceived += ForwardSnap;
		_equities.Error += ForwardError;
		_optionQuotes.SnapReceived += ForwardSnap;
		_optionQuotes.Error += ForwardError;
	}

	public event Func<SnapMessage, QuoddAssetTypes, CancellationToken, ValueTask> SnapReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async Task Connect(string validationTicker, CancellationToken cancellationToken)
	{
		if (_isStarted)
			throw new InvalidOperationException("QUODD client is already connected.");

		if (validationTicker.IsEmpty())
			_ = await _tokens.GetToken(cancellationToken);
		else
		{
			var values = await GetSnaps([validationTicker], QuoddAssetTypes.Equities,
				cancellationToken);
			if (values.Length == 0)
				throw new QuoddApiException($"QUODD returned no snapshot for validation ticker '{validationTicker}'.");
		}

		_equities.Start();
		_optionQuotes.Start();
		_isStarted = true;
	}

	public async Task<SnapMessage[]> GetSnaps(IEnumerable<string> tickers,
		QuoddAssetTypes assetType, CancellationToken cancellationToken)
	{
		var request = new SnapsRequest();
		request.Tickers.Add(tickers.Where(ticker => !ticker.IsEmpty())
			.Distinct(StringComparer.OrdinalIgnoreCase));
		if (request.Tickers.Count == 0)
			return [];

		var response = await Unary((headers, token) =>
			_snaps.GetSnapsAsync(request, headers, cancellationToken: token), assetType,
			cancellationToken);
		if (!response.Error.IsEmpty())
			throw new QuoddApiException($"QUODD snapshot request failed: {response.Error}");
		return [.. response.Data];
	}

	public Task<TickerInfoResponse> GetTickerInfo(IEnumerable<string> tickers,
		CancellationToken cancellationToken)
	{
		var request = new TickerInfoRequest();
		request.Tickers.Add(tickers.Where(ticker => !ticker.IsEmpty())
			.Distinct(StringComparer.OrdinalIgnoreCase));
		request.Fields.Add(_tickerInfoFields);
		return request.Tickers.Count == 0
			? Task.FromResult(new TickerInfoResponse())
			: Unary((headers, token) => _tickerInfo.GetTickerInfoAsync(request, headers,
				cancellationToken: token), null, cancellationToken);
	}

	public async Task<Grpc.Options.Option[]> GetOptionTickers(string underlyingTicker,
		OptionTypes? optionType, DateTime? expirationDate, CancellationToken cancellationToken)
	{
		var request = new GetOptionTickersRequest
		{
			UnderlyingTicker = underlyingTicker.ThrowIfEmpty(nameof(underlyingTicker)),
			OptionType = optionType switch
			{
				OptionTypes.Call => "C",
				OptionTypes.Put => "P",
				_ => string.Empty,
			},
			ExpirationDate = expirationDate?.ToString("yyyy-MM-dd",
				CultureInfo.InvariantCulture) ?? string.Empty,
		};
		var response = await Unary((headers, token) => _options.GetOptionTickersAsync(request,
			headers, cancellationToken: token), null, cancellationToken);
		if (!response.Error.IsEmpty())
			throw new QuoddApiException($"QUODD option lookup failed: {response.Error}");
		return [.. response.Options];
	}

	public void Subscribe(QuoddAssetTypes assetType, string ticker)
	{
		if (!_isStarted)
			throw new InvalidOperationException("QUODD client is not connected.");
		GetGroup(assetType).Add(ticker);
	}

	public void Unsubscribe(QuoddAssetTypes assetType, string ticker)
	{
		if (_isStarted)
			GetGroup(assetType).Remove(ticker);
	}

	internal async Task StreamSnaps(string[] tickers, QuoddAssetTypes assetType,
		Func<SnapMessage, CancellationToken, ValueTask> handler,
		CancellationToken cancellationToken)
	{
		var request = new SnapStreamRequest();
		request.Tickers.Add(tickers);
		var headers = await CreateHeaders(assetType, cancellationToken);
		using var call = _snaps.GetSnapsStream(request, headers,
			cancellationToken: cancellationToken);
		while (await call.ResponseStream.MoveNext(cancellationToken))
			await handler(call.ResponseStream.Current, cancellationToken);
	}

	internal void InvalidateToken()
		=> _tokens.Invalidate();

	private async Task<T> Unary<T>(
		Func<Metadata, CancellationToken, AsyncUnaryCall<T>> call,
		QuoddAssetTypes? assetType, CancellationToken cancellationToken)
	{
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				var headers = await CreateHeaders(assetType, cancellationToken);
				using var operation = call(headers, cancellationToken);
				return await operation.ResponseAsync.WaitAsync(cancellationToken);
			}
			catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated &&
				attempt < _maxAttempts)
			{
				_tokens.Invalidate();
				await RetryDelay(attempt, cancellationToken);
			}
			catch (RpcException ex) when (IsTransient(ex.StatusCode) &&
				attempt < _maxAttempts)
			{
				await RetryDelay(attempt, cancellationToken);
			}
		}
	}

	private async Task<Metadata> CreateHeaders(QuoddAssetTypes? assetType,
		CancellationToken cancellationToken)
	{
		var metadata = new Metadata
		{
			{ "authorization", $"Bearer {await _tokens.GetToken(cancellationToken)}" },
		};
		if (assetType != null)
			metadata.Add("assettype", assetType.Value.ToHeader());
		return metadata;
	}

	private static bool IsTransient(StatusCode statusCode)
		=> statusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or
			StatusCode.ResourceExhausted or StatusCode.Internal;

	private static Task RetryDelay(int attempt, CancellationToken cancellationToken)
		=> Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5))),
			cancellationToken);

	private QuoddStreamGroup GetGroup(QuoddAssetTypes assetType)
		=> assetType switch
		{
			QuoddAssetTypes.Equities => _equities,
			QuoddAssetTypes.Options => _optionQuotes,
			_ => throw new ArgumentOutOfRangeException(nameof(assetType), assetType, null),
		};

	private ValueTask ForwardSnap(SnapMessage message, QuoddAssetTypes assetType,
		CancellationToken cancellationToken)
		=> SnapReceived == null ? default : SnapReceived(message, assetType, cancellationToken);

	private ValueTask ForwardError(Exception error, CancellationToken cancellationToken)
		=> Error == null ? default : Error(error, cancellationToken);

	public async ValueTask DisposeAsync()
	{
		_isStarted = false;
		_equities.SnapReceived -= ForwardSnap;
		_equities.Error -= ForwardError;
		_optionQuotes.SnapReceived -= ForwardSnap;
		_optionQuotes.Error -= ForwardError;
		await _equities.DisposeAsync();
		await _optionQuotes.DisposeAsync();
		_channel.Dispose();
		_tokens.Dispose();
	}
}
