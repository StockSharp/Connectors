namespace StockSharp.Orca.Native;

sealed class OrcaApiClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 16 * 1024 * 1024;
	private readonly HttpClient _httpClient;
	private readonly JsonSerializerSettings _serializerSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};
	private bool _isDisposed;

	public OrcaApiClient(string endpoint)
	{
		endpoint = NormalizeEndpoint(endpoint);
		_httpClient = new()
		{
			BaseAddress = new Uri(endpoint + '/', UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(30),
		};
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Orca/1.0");
	}

	public override string Name => "Orca_Public_API";

	public async ValueTask<OrcaApiPool[]> GetPoolsAsync(int maximum,
		CancellationToken cancellationToken)
	{
		if (maximum is < 1 or > 100)
			throw new ArgumentOutOfRangeException(nameof(maximum));
		var path = "pools?size=" + maximum.ToString(
			CultureInfo.InvariantCulture) +
			"&sortBy=volume&sortDirection=desc&hasAdaptiveFee=false" +
			"&includeBlocked=false";
		var response = await GetAsync<OrcaApiResponse<OrcaApiPool[]>>(path,
			cancellationToken);
		return response?.Data?.Where(static pool => pool is not null &&
			!pool.IsWarning).Take(maximum).ToArray() ?? [];
	}

	public async ValueTask<OrcaApiPool> GetPoolAsync(string address,
		CancellationToken cancellationToken)
	{
		address = address.NormalizePublicKey();
		try
		{
			return (await GetAsync<OrcaApiResponse<OrcaApiPool>>(
				"pools/" + Uri.EscapeDataString(address),
				cancellationToken))?.Data;
		}
		catch (OrcaApiException error) when (
			error.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_httpClient.Dispose();
		base.DisposeManaged();
	}

	private async ValueTask<TResult> GetAsync<TResult>(string path,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		using var response = await _httpClient.GetAsync(path,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (response.Content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				"Orca API response exceeds the configured safety limit.");
		var body = await ReadBodyAsync(response.Content, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			OrcaApiError apiError = null;
			try
			{
				apiError = JsonConvert.DeserializeObject<OrcaApiError>(body,
					_serializerSettings);
			}
			catch (JsonException)
			{
			}
			throw new OrcaApiException(response.StatusCode,
				$"Orca API request '{path}' failed: " +
				$"{Limit(apiError?.Message ?? apiError?.Error ?? body, 1024)}");
		}
		try
		{
			return JsonConvert.DeserializeObject<TResult>(body,
				_serializerSettings);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				$"Orca API returned malformed JSON for '{path}'.", error);
		}
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Orca API endpoint must use HTTP or HTTPS.", nameof(endpoint));
		return endpoint.TrimEnd('/');
	}

	private static string Limit(string value, int maximum)
		=> value.IsEmpty() || value.Length <= maximum
			? value
			: value[..maximum];

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
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
				throw new InvalidDataException(
					"Orca API response exceeds the configured safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}
}
