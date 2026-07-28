namespace StockSharp.MarketDataApp.Native;

sealed class MarketDataAppRestClient : BaseLogReceiver
{
	private readonly HttpClient _http;
	private readonly string _endpoint;
	private readonly string _token;

	public MarketDataAppRestClient(Uri endpoint, SecureString token,
		HttpMessageHandler handler = null)
	{
		if (endpoint is null || !endpoint.IsAbsoluteUri ||
			endpoint.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException(
				"MarketData.app endpoint must be an absolute HTTPS URI.",
				nameof(endpoint));
		_endpoint = endpoint.AbsoluteUri.TrimEnd('/');
		_token = token.IsEmpty() ? null : token.UnSecure();
		_http = handler is null ? new() : new(handler, true);
		_http.Timeout = TimeSpan.FromMinutes(2);
	}

	public async ValueTask<MarketDataAppQuote[]> GetQuotesAsync(
		MarketDataAppAssetKinds kind, string symbol,
		CancellationToken cancellationToken)
	{
		var section = kind switch
		{
			MarketDataAppAssetKinds.Option => "options",
			MarketDataAppAssetKinds.Index => "indices",
			MarketDataAppAssetKinds.Stock => "stocks",
			_ => throw new NotSupportedException(
				"MarketData.app mutual funds do not provide quotes."),
		};
		var response = await GetObjectAsync(
			$"/{section}/quotes/{Escape(symbol)}/", null,
			cancellationToken);
		return response.ToQuotes(symbol);
	}

	public async ValueTask<MarketDataAppQuote[]> GetOptionChainAsync(
		string underlying, DateTime? expiry,
		OptionTypes? optionType, decimal? strike, int limit,
		CancellationToken cancellationToken)
	{
		var response = await GetObjectAsync(
			$"/options/chain/{Escape(underlying)}/",
			Parameters(
				("expiration", expiry?.ToString("yyyy-MM-dd",
					CultureInfo.InvariantCulture)),
				("side", optionType?.ToString().ToLowerInvariant()),
				("strike", strike?.ToString(
					CultureInfo.InvariantCulture)),
				("limit", limit.ToString(
					CultureInfo.InvariantCulture))),
			cancellationToken);
		return response.ToQuotes(null);
	}

	public async ValueTask<MarketDataAppCandle[]> GetCandlesAsync(
		MarketDataAppAssetKinds kind, string resolution,
		string symbol, DateTime from, DateTime to,
		bool extendedHours, bool adjustSplits,
		CancellationToken cancellationToken)
	{
		var section = kind switch
		{
			MarketDataAppAssetKinds.Stock => "stocks",
			MarketDataAppAssetKinds.Index => "indices",
			MarketDataAppAssetKinds.Fund => "funds",
			_ => throw new NotSupportedException(
				"MarketData.app does not provide option candles."),
		};
		var parameters = Parameters(
			("from", ToUnix(from)),
			("to", ToUnix(to)));
		if (kind == MarketDataAppAssetKinds.Stock)
		{
			parameters["extended"] = extendedHours.ToString()
				.ToLowerInvariant();
			parameters["adjustsplits"] = adjustSplits.ToString()
				.ToLowerInvariant();
		}
		var response = await GetObjectAsync(
			$"/{section}/candles/{Escape(resolution)}/" +
				$"{Escape(symbol)}/",
			parameters, cancellationToken);
		return response.ToCandles();
	}

	private async ValueTask<JObject> GetObjectAsync(string path,
		IEnumerable<KeyValuePair<string, string>> parameters,
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get,
			_endpoint + path + BuildQuery(parameters));
		request.Headers.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		request.Headers.UserAgent.ParseAdd(
			"StockSharp.MarketDataApp/1.0");
		if (!_token.IsEmpty())
			request.Headers.Authorization =
				new AuthenticationHeaderValue("Bearer", _token);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead,
			cancellationToken);
		var text = await response.Content.ReadAsStringAsync(
			cancellationToken);
		JObject value = null;
		if (!text.IsEmpty())
		{
			try
			{
				value = JObject.Parse(text);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"MarketData.app returned invalid JSON.", error);
			}
		}
		value ??= new JObject { ["s"] = "no_data" };
		if (response.IsSuccessStatusCode ||
			response.StatusCode == HttpStatusCode.NotFound &&
			value.Value<string>("s").EqualsIgnoreCase("no_data"))
			return value;
		throw new HttpRequestException(
			value.Value<string>("errmsg").IsEmpty(
				$"MarketData.app returned HTTP " +
					$"{(int)response.StatusCode}."),
			null, response.StatusCode);
	}

	private static Dictionary<string, string> Parameters(
		params (string Name, string Value)[] values)
		=> values
			.Where(static value => !value.Value.IsEmpty())
			.ToDictionary(static value => value.Name,
				static value => value.Value,
				StringComparer.Ordinal);

	private static string BuildQuery(
		IEnumerable<KeyValuePair<string, string>> parameters)
	{
		var query = (parameters ?? [])
			.Where(static pair => !pair.Value.IsEmpty())
			.Select(static pair =>
				$"{Escape(pair.Key)}={Escape(pair.Value)}")
			.Join("&");
		return query.IsEmpty() ? string.Empty : $"?{query}";
	}

	private static string ToUnix(DateTime value)
		=> new DateTimeOffset(value.ToUniversalTime())
			.ToUnixTimeSeconds()
			.ToString(CultureInfo.InvariantCulture);

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
