namespace StockSharp.Velora.Native;

sealed class VeloraHttpClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 8 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly VeloraChains _chain;
	private readonly string _partner;
	private readonly HttpClient _http = new(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.GZip |
			DecompressionMethods.Deflate,
	});
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequest;

	public VeloraHttpClient(string endpoint, VeloraChains chain,
		string partner)
	{
		if (!System.Enum.IsDefined(chain))
			throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported Velora chain.");
		_chain = chain;
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/') +
			"/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
				_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
			throw new ArgumentException(
				"Velora API endpoint must be an absolute HTTP or HTTPS URI.",
				nameof(endpoint));
		_partner = NormalizePartner(partner);
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Velora-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "Velora_REST";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<JObject> GetPriceAsync(string source,
		int sourceDecimals, string destination, int destinationDecimals,
		BigInteger amount, string userAddress,
		CancellationToken cancellationToken)
	{
		source = source.ToVeloraAddress();
		destination = destination.ToVeloraAddress();
		if (source.EqualsIgnoreCase(destination))
			throw new ArgumentException(
				"Velora quote tokens must be different.",
				nameof(destination));
		if (sourceDecimals is < 0 or > 255)
			throw new ArgumentOutOfRangeException(nameof(sourceDecimals));
		if (destinationDecimals is < 0 or > 255)
			throw new ArgumentOutOfRangeException(nameof(destinationDecimals));
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		if (!userAddress.IsEmpty())
			userAddress = userAddress.NormalizeAddress();

		var query = "prices?srcToken=" + Escape(source) +
			"&srcDecimals=" + sourceDecimals.ToString(
				CultureInfo.InvariantCulture) +
			"&destToken=" + Escape(destination) +
			"&destDecimals=" + destinationDecimals.ToString(
				CultureInfo.InvariantCulture) +
			"&amount=" + Escape(amount.ToString(
				CultureInfo.InvariantCulture)) +
			"&side=SELL&network=" + ((int)_chain).ToString(
				CultureInfo.InvariantCulture) +
			"&partner=" + Escape(_partner) +
			"&version=6.2";
		if (!userAddress.IsEmpty())
			query += "&userAddress=" + Escape(userAddress);
		var response = await SendAsync<VeloraPriceResponse>(HttpMethod.Get,
			query, null, cancellationToken);
		return response.PriceRoute ?? throw new InvalidDataException(
			"Velora API returned no price route.");
	}

	public ValueTask<VeloraTransactionData> BuildTransactionAsync(
		JObject priceRoute, string source, int sourceDecimals,
		string destination, int destinationDecimals, BigInteger sourceAmount,
		string walletAddress, decimal slippageTolerance,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(priceRoute);
		source = source.ToVeloraAddress();
		destination = destination.ToVeloraAddress();
		walletAddress = walletAddress.NormalizeAddress();
		if (sourceDecimals is < 0 or > 255)
			throw new ArgumentOutOfRangeException(nameof(sourceDecimals));
		if (destinationDecimals is < 0 or > 255)
			throw new ArgumentOutOfRangeException(nameof(destinationDecimals));
		if (sourceAmount <= 0)
			throw new ArgumentOutOfRangeException(nameof(sourceAmount));
		if (slippageTolerance is < 0 or > 100)
			throw new ArgumentOutOfRangeException(nameof(slippageTolerance));
		var slippageBps = checked((int)decimal.Round(
			slippageTolerance * 100m, 0, MidpointRounding.AwayFromZero));
		var request = new VeloraBuildRequest
		{
			PriceRoute = priceRoute,
			SourceToken = source,
			DestinationToken = destination,
			UserAddress = walletAddress,
			SourceDecimals = sourceDecimals,
			DestinationDecimals = destinationDecimals,
			SourceAmount = sourceAmount.ToString(CultureInfo.InvariantCulture),
			SlippageBps = slippageBps,
			Partner = _partner,
		};
		var body = JsonConvert.SerializeObject(request, _jsonSettings);
		return SendAsync<VeloraTransactionData>(HttpMethod.Post,
			$"transactions/{(int)_chain}?ignoreChecks=true" +
				"&ignoreGasEstimate=true",
			body, cancellationToken);
	}

	private async ValueTask<TResult> SendAsync<TResult>(HttpMethod method,
		string path, string body, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(method);
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRequestAsync(cancellationToken);
			using var request = new HttpRequestMessage(method,
				new Uri(_endpoint, path));
			if (!body.IsEmpty())
				request.Content = new StringContent(body, Encoding.UTF8,
					"application/json");
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var responseBody = await ReadBodyAsync(response.Content,
				cancellationToken);
			if (attempt < 3 && (response.StatusCode == (HttpStatusCode)429 ||
				(int)response.StatusCode >= 500))
			{
				var delay = response.Headers.RetryAfter?.Delta ??
					TimeSpan.FromSeconds(1 << attempt);
				await Task.Delay(delay.Min(TimeSpan.FromSeconds(8)),
					cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateApiException(response.StatusCode, responseBody);
			try
			{
				var result = JsonConvert.DeserializeObject<TResult>(
					responseBody, _jsonSettings);
				return result is null
					? throw new InvalidDataException(
						"Velora API returned an empty JSON value.")
					: result;
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Velora API returned an unexpected response shape.",
					error);
			}
		}
	}

	private VeloraApiException CreateApiException(HttpStatusCode statusCode,
		string body)
	{
		VeloraApiError error = null;
		try
		{
			error = JsonConvert.DeserializeObject<VeloraApiError>(body,
				_jsonSettings);
		}
		catch (JsonException)
		{
		}
		var detail = error?.Message;
		if (detail.IsEmpty())
			detail = error?.Error;
		if (detail.IsEmpty() && error?.Details is not null)
			detail = error.Details.Type == JTokenType.String
				? error.Details.Value<string>()
				: error.Details.ToString(Formatting.None);
		if (detail.IsEmpty())
			detail = body?.Trim().Truncate(512, string.Empty);
		if (detail.IsEmpty())
			detail = "request rejected";
		if (error?.ErrorType.IsEmpty() == false)
			detail += $" ({error.ErrorType})";
		return new(statusCode,
			$"Velora HTTP {(int)statusCode}: {detail}");
	}

	private async ValueTask WaitForRequestAsync(
		CancellationToken cancellationToken)
	{
		await _requestGate.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequest - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequest = DateTime.UtcNow + TimeSpan.FromMilliseconds(1050);
		}
		finally
		{
			_requestGate.Release();
		}
	}

	private static string NormalizePartner(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Length > 64 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not '-' and not '_' and not '.'))
			throw new ArgumentException(
				"Velora partner must contain at most 64 letters, digits, dots, " +
					"underscores, or hyphens.", nameof(value));
		return value;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"Velora response exceeds the 8 MiB safety limit.");
		await using var source = await content.ReadAsStreamAsync(
			cancellationToken);
		using var target = new MemoryStream();
		var block = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(block, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"Velora response exceeds the 8 MiB safety limit.");
			target.Write(block, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}
}
