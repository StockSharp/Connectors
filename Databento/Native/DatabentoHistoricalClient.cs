namespace StockSharp.Databento.Native;

internal sealed class DatabentoHistoricalRequest
{
	public string Dataset { get; init; }
	public string Schema { get; init; }
	public string Symbology { get; init; }
	public string[] Symbols { get; init; }
	public DateTime Start { get; init; }
	public DateTime End { get; init; }
	public long? Limit { get; init; }

	public HttpContent ToContent()
	{
		var fields = new List<KeyValuePair<string, string>>
		{
			new("dataset", Dataset),
			new("schema", Schema),
			new("encoding", "dbn"),
			new("compression", "none"),
			new("stype_in", Symbology),
			new("stype_out", "instrument_id"),
			new("symbols", Symbols.JoinComma()),
			new("start", Start.ToUnixNanoseconds().ToString(CultureInfo.InvariantCulture)),
			new("end", End.ToUnixNanoseconds().ToString(CultureInfo.InvariantCulture)),
		};
		if (Limit is > 0)
			fields.Add(new("limit", Limit.Value.ToString(CultureInfo.InvariantCulture)));
		return new FormUrlEncodedContent(fields);
	}
}

internal sealed class DatabentoHistoricalClient : BaseLogReceiver
{
	private readonly string _apiKey;
	private readonly Uri _address;
	private readonly HttpClient _httpClient;

	public DatabentoHistoricalClient(string apiKey, string address)
	{
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		if (!Uri.TryCreate(address, UriKind.Absolute, out _address) ||
			_address.Scheme is not ("http" or "https"))
		{
			throw new ArgumentException("The Databento historical address must be an HTTP URL.",
				nameof(address));
		}
		_httpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
	}

	public override string Name => nameof(DatabentoHistoricalClient);

	public async IAsyncEnumerable<DbnRecord> GetRange(DatabentoHistoricalRequest range,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		if (range == null)
			throw new ArgumentNullException(nameof(range));
		if (range.Start >= range.End)
			throw new ArgumentOutOfRangeException(nameof(range), "The historical start must precede the end.");

		using var request = new HttpRequestMessage(HttpMethod.Post, _address)
		{
			Content = range.ToContent(),
		};
		request.Headers.Accept.Add(new("application/octet-stream"));
		request.Headers.Authorization = new("Basic",
			Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_apiKey}:")));
		request.Headers.UserAgent.ParseAdd("StockSharp/1.0");

		using var response = await _httpClient.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new HttpRequestException(
				$"Databento historical request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {body}",
				null, response.StatusCode);
		}

		if (response.Headers.TryGetValues("X-Warning", out var warnings))
		{
			foreach (var warning in warnings)
				this.AddWarningLog("Databento: {0}", warning);
		}

		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		var decoder = new DbnDecoder();
		var metadata = await decoder.ReadMetadata(stream, cancellationToken);
		if (metadata.NotFoundSymbols.Length > 0)
			this.AddWarningLog("Databento did not resolve: {0}.", metadata.NotFoundSymbols.JoinComma());

		await foreach (var record in decoder.ReadRecords(stream, cancellationToken))
			yield return record;
	}

	protected override void DisposeManaged()
	{
		_httpClient.Dispose();
		base.DisposeManaged();
	}
}
