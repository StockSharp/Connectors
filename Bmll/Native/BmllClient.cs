namespace StockSharp.Bmll.Native;

sealed class BmllApiException : InvalidOperationException
{
	public BmllApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class BmllClient : BaseLogReceiver
{
	private const string _sdkVersion = "1.24.7";
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly BmllAuthenticationModes _authenticationMode;
	private readonly Uri _apiAddress;
	private readonly Uri _authAddress;
	private readonly string _configuredToken;
	private readonly string _configuredApiKey;
	private readonly string _login;
	private readonly string _privateKeyPath;
	private readonly string _privateKeyPassphrase;
	private readonly int _maxAttempts;
	private readonly TimeSpan _queryPollingInterval;
	private readonly TimeSpan _queryTimeout;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _authLock = new(1, 1);
	private string _accessToken;
	private string _apiKey;
	private DateTime _accessTokenExpiresUtc;

	public BmllClient(BmllAuthenticationModes authenticationMode,
		Uri apiAddress, Uri authAddress, string token, string apiKey,
		string login, string privateKeyPath, string privateKeyPassphrase,
		int maxAttempts, TimeSpan queryPollingInterval, TimeSpan queryTimeout)
	{
		_authenticationMode = authenticationMode;
		_apiAddress = EnsureTrailingSlash(apiAddress ??
			throw new ArgumentNullException(nameof(apiAddress)));
		_authAddress = EnsureTrailingSlash(authAddress ??
			throw new ArgumentNullException(nameof(authAddress)));
		_configuredToken = token;
		_configuredApiKey = apiKey;
		_login = login;
		_privateKeyPath = privateKeyPath;
		_privateKeyPassphrase = privateKeyPassphrase;
		_maxAttempts = Math.Max(1, maxAttempts);
		_queryPollingInterval = queryPollingInterval;
		_queryTimeout = queryTimeout;
		_http = new(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip |
				DecompressionMethods.Deflate,
		})
		{
			Timeout = TimeSpan.FromMinutes(5),
		};
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"User-Agent", "StockSharp-BMLL/1.0");
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"x-bmll-version", _sdkVersion);
	}

	public Task<BmllDataset[]> GetDatasets(CancellationToken cancellationToken)
		=> SendApi<BmllDataset[]>(HttpMethod.Get,
			"datasets?describe=false&outputCase=pascal", null, cancellationToken);

	public async IAsyncEnumerable<BmllMarketDataRecord> Query(string dataset,
		BmllDataQueryRequest query,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		ValidateDataset(dataset);
		query = query ?? throw new ArgumentNullException(nameof(query));
		var path = $"data/{Uri.EscapeDataString(dataset)}";
		var initiated = await SendApi<BmllQueryResponse>(HttpMethod.Post,
			path, query, cancellationToken);
		var queryId = initiated.Id.ThrowIfEmpty(nameof(BmllQueryResponse.Id));
		var started = DateTime.UtcNow;
		string downloadLink = null;

		while (DateTime.UtcNow - started < _queryTimeout)
		{
			var status = await SendApi<BmllQueryResponse>(HttpMethod.Get,
				$"{path}?id={Uri.EscapeDataString(queryId)}", null, cancellationToken);
			switch (ParseStatus(status.Status))
			{
				case BmllQueryStatuses.Success:
					downloadLink = status.Link.ThrowIfEmpty(nameof(status.Link));
					break;
				case BmllQueryStatuses.Failed:
				case BmllQueryStatuses.Cancelled:
					throw new InvalidOperationException(
						$"BMLL query '{queryId}' ended with status '{status.Status}'.");
				case BmllQueryStatuses.Running:
				case BmllQueryStatuses.Processing:
					break;
				default:
					throw new InvalidDataException(
						$"BMLL query '{queryId}' returned unknown status '{status.Status}'.");
			}

			if (!downloadLink.IsEmpty())
				break;
			await Task.Delay(_queryPollingInterval, cancellationToken);
		}

		if (downloadLink.IsEmpty())
		{
			throw new TimeoutException(
				$"BMLL query '{queryId}' did not complete within {_queryTimeout}.");
		}

		using var response = await Download(downloadLink, cancellationToken);
		await foreach (var record in ReadRecords(response, cancellationToken))
			yield return record;
	}

	private async Task<TResponse> SendApi<TResponse>(HttpMethod method,
		string path, BmllDataQueryRequest body, CancellationToken cancellationToken)
		where TResponse : class
	{
		for (var attempt = 1; ; attempt++)
		{
			await EnsureAccessToken(false, cancellationToken);
			using var request = new HttpRequestMessage(method, new Uri(_apiAddress, path));
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
			if (!_apiKey.IsEmpty())
				request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
			if (body != null)
			{
				request.Content = new StringContent(JsonConvert.SerializeObject(body,
					_jsonSettings), Encoding.UTF8, "application/json");
			}

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request,
					HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			}
			catch (HttpRequestException) when (attempt < _maxAttempts)
			{
				await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
				continue;
			}
			catch (HttpRequestException error)
			{
				throw new InvalidOperationException(
					$"BMLL request '{GetSafePath(path)}' failed: {Redact(error.Message)}");
			}

			using (response)
			{
				var content = await response.Content.ReadAsStringAsync(cancellationToken);
				if (response.StatusCode == HttpStatusCode.Unauthorized &&
					_authenticationMode == BmllAuthenticationModes.SshKey &&
					attempt < _maxAttempts)
				{
					InvalidateAccessToken();
					continue;
				}
				if (IsTransient(response.StatusCode) && attempt < _maxAttempts)
				{
					await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
					continue;
				}
				if (!response.IsSuccessStatusCode)
					throw CreateError(response.StatusCode, content, GetSafePath(path));
				if (content.IsEmpty())
					throw new InvalidDataException(
						$"BMLL returned an empty response for '{GetSafePath(path)}'.");

				try
				{
					return JsonConvert.DeserializeObject<TResponse>(content, _jsonSettings)
						?? throw new InvalidDataException(
							$"BMLL returned an empty JSON document for '{GetSafePath(path)}'.");
				}
				catch (JsonException error)
				{
					throw new InvalidDataException(
						$"BMLL returned invalid JSON for '{GetSafePath(path)}'.", error);
				}
			}
		}
	}

	private async Task<TResponse> SendAuth<TRequest, TResponse>(string path,
		TRequest body, CancellationToken cancellationToken)
		where TRequest : class
		where TResponse : class
	{
		using var request = new HttpRequestMessage(HttpMethod.Post,
			new Uri(_authAddress, path))
		{
			Content = new StringContent(JsonConvert.SerializeObject(body,
				_jsonSettings), Encoding.UTF8, "application/json"),
		};
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateError(response.StatusCode, content, path);
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(content, _jsonSettings)
				?? throw new InvalidDataException(
					$"BMLL authentication returned an empty response for '{path}'.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				$"BMLL authentication returned invalid JSON for '{path}'.", error);
		}
	}

	private async Task EnsureAccessToken(bool force,
		CancellationToken cancellationToken)
	{
		if (_authenticationMode == BmllAuthenticationModes.BearerToken)
		{
			_accessToken ??= NormalizeBearer(_configuredToken);
			_apiKey ??= _configuredApiKey;
			return;
		}
		if (!force && !_accessToken.IsEmpty() &&
			DateTime.UtcNow < _accessTokenExpiresUtc)
		{
			return;
		}

		await _authLock.WaitAsync(cancellationToken);
		try
		{
			if (!force && !_accessToken.IsEmpty() &&
				DateTime.UtcNow < _accessTokenExpiresUtc)
			{
				return;
			}

			var identity = await SendAuth<BmllIdentityRequest, BmllIdentityResponse>(
				"auth/identity", new() { Issuer = _login }, cancellationToken);
			var expires = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
			var claims = new BmllTokenClaims
			{
				Issuer = _login,
				Audience = "dd-services",
				ExpiresAt = expires,
				SessionId = identity.SessionId.ThrowIfEmpty(
					nameof(BmllIdentityResponse.SessionId)),
			};
			var token = await SendAuth<BmllTokenRequest, BmllTokenResponse>(
				"auth/token", new()
				{
					Issuer = claims.Issuer,
					Audience = claims.Audience,
					ExpiresAt = claims.ExpiresAt,
					SessionId = claims.SessionId,
					Jws = Sign(claims),
				}, cancellationToken);
			_accessToken = token.Token.ThrowIfEmpty(nameof(BmllTokenResponse.Token));
			_apiKey = token.ApiKey;
			_accessTokenExpiresUtc = DateTime.UtcNow.AddHours(23);
		}
		finally
		{
			_authLock.Release();
		}
	}

	private string Sign(BmllTokenClaims claims)
	{
		var header = Base64Url(JsonConvert.SerializeObject(new BmllJwtHeader(),
			_jsonSettings));
		var payload = Base64Url(JsonConvert.SerializeObject(claims, _jsonSettings));
		var signingInput = $"{header}.{payload}";
		var pem = File.ReadAllText(_privateKeyPath.ThrowIfEmpty(nameof(_privateKeyPath)));
		using var rsa = RSA.Create();
		if (_privateKeyPassphrase.IsEmpty())
			rsa.ImportFromPem(pem);
		else
			rsa.ImportFromEncryptedPem(pem, _privateKeyPassphrase);
		var signature = rsa.SignData(Encoding.ASCII.GetBytes(signingInput),
			HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		return $"{signingInput}.{Base64Url(signature)}";
	}

	private async Task<HttpResponseMessage> Download(string link,
		CancellationToken cancellationToken)
	{
		if (!Uri.TryCreate(link, UriKind.Absolute, out var address) ||
			address.Scheme != Uri.UriSchemeHttps)
		{
			throw new InvalidDataException(
				"BMLL returned an invalid non-HTTPS download address.");
		}

		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, address);
			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request,
					HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			}
			catch (HttpRequestException) when (attempt < _maxAttempts)
			{
				await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
				continue;
			}
			catch (HttpRequestException)
			{
				throw new InvalidOperationException("BMLL result download failed.");
			}

			if (IsTransient(response.StatusCode) && attempt < _maxAttempts)
			{
				var delay = GetRetryDelay(response, attempt);
				response.Dispose();
				await Task.Delay(delay, cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
			{
				var status = response.StatusCode;
				response.Dispose();
				throw new BmllApiException(status,
					$"BMLL result download failed ({(int)status} {status}).");
			}
			return response;
		}
	}

	private static async IAsyncEnumerable<BmllMarketDataRecord> ReadRecords(
		HttpResponseMessage response,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var source = await response.Content.ReadAsStreamAsync(cancellationToken);
		var prefix = new byte[2];
		var prefixLength = 0;
		while (prefixLength < prefix.Length)
		{
			var read = await source.ReadAsync(prefix.AsMemory(prefixLength,
				prefix.Length - prefixLength), cancellationToken);
			if (read == 0)
				break;
			prefixLength += read;
		}

		Stream payload = new PrefixReadStream(prefix, prefixLength, source);
		if (prefixLength == 2 && prefix[0] == 0x1f && prefix[1] == 0x8b)
			payload = new GZipStream(payload, CompressionMode.Decompress);
		using (payload)
		using (var reader = new StreamReader(payload, Encoding.UTF8, true, 64 * 1024))
		{
			while (await reader.ReadLineAsync(cancellationToken) is { } line)
			{
				line = line.Trim();
				if (line.IsEmpty())
					continue;
				if (line[0] == '[')
				{
					throw new InvalidDataException(
						"BMLL returned a JSON array instead of the documented JSON-lines format.");
				}
				BmllMarketDataRecord record;
				try
				{
					record = JsonConvert.DeserializeObject<BmllMarketDataRecord>(
						line, _jsonSettings);
				}
				catch (JsonException error)
				{
					throw new InvalidDataException(
						"BMLL returned an invalid JSON-lines record.", error);
				}
				if (record != null)
					yield return record;
			}
		}
	}

	private BmllApiException CreateError(HttpStatusCode statusCode,
		string content, string path)
	{
		BmllErrorResponse response = null;
		try
		{
			response = JsonConvert.DeserializeObject<BmllErrorResponse>(
				content, _jsonSettings);
		}
		catch (JsonException)
		{
		}
		var details = Redact(response?.GetMessage().IsEmpty(content))?.Trim();
		if (details?.Length > 1000)
			details = details[..1000];
		return new(statusCode,
			$"BMLL request '{path}' failed ({(int)statusCode} {statusCode})" +
			(details.IsEmpty() ? "." : $": {details}"));
	}

	private void InvalidateAccessToken()
	{
		_accessToken = null;
		_apiKey = null;
		_accessTokenExpiresUtc = default;
	}

	private static BmllQueryStatuses ParseStatus(string status)
		=> status?.ToUpperInvariant() switch
		{
			"RUNNING" => BmllQueryStatuses.Running,
			"PROCESSING" => BmllQueryStatuses.Processing,
			"SUCCESS" => BmllQueryStatuses.Success,
			"FAILED" => BmllQueryStatuses.Failed,
			"CANCELLED" => BmllQueryStatuses.Cancelled,
			_ => BmllQueryStatuses.Unknown,
		};

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode is >= 500 and <= 511;

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response?.Headers.RetryAfter?.Delta is { } delta)
			return ClampDelay(delta);
		if (response?.Headers.RetryAfter?.Date is { } date)
			return ClampDelay(date.UtcDateTime - DateTime.UtcNow);
		return TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
	}

	private static TimeSpan ClampDelay(TimeSpan delay)
		=> delay <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) :
			delay > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : delay;

	private static string Base64Url(string value)
		=> Base64Url(Encoding.UTF8.GetBytes(value));

	private static string Base64Url(byte[] value)
		=> Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	private static string NormalizeBearer(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
			? value[7..].Trim() : value;
	}

	private static void ValidateDataset(string value)
	{
		value.ThrowIfEmpty(nameof(value));
		if (value.Any(character => !char.IsLetterOrDigit(character) &&
			character is not '_' and not '-' and not '.'))
		{
			throw new ArgumentException(
				"BMLL dataset name contains unsupported characters.", nameof(value));
		}
	}

	private static string GetSafePath(string value)
		=> value?.Split('?', 2)[0];

	private string Redact(string value)
	{
		if (value.IsEmpty())
			return value;
		foreach (var secret in new[]
		{
			_accessToken,
			_apiKey,
			_configuredToken,
			_configuredApiKey,
			_privateKeyPassphrase,
		})
		{
			if (!secret.IsEmpty())
				value = value.Replace(secret, "***", StringComparison.Ordinal);
		}
		return value;
	}

	private static Uri EnsureTrailingSlash(Uri address)
		=> address.AbsoluteUri.EndsWith('/') ? address : new(address.AbsoluteUri + "/");

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_authLock.Dispose();
		base.DisposeManaged();
	}

	private sealed class PrefixReadStream : Stream
	{
		private readonly byte[] _prefix;
		private readonly int _prefixLength;
		private readonly Stream _source;
		private int _position;

		public PrefixReadStream(byte[] prefix, int prefixLength, Stream source)
		{
			_prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
			_prefixLength = prefixLength;
			_source = source ?? throw new ArgumentNullException(nameof(source));
		}

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			var copied = CopyPrefix(buffer.AsSpan(offset, count));
			return copied == count ? copied :
				copied + _source.Read(buffer, offset + copied, count - copied);
		}

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
			CancellationToken cancellationToken = default)
		{
			var copied = CopyPrefix(buffer.Span);
			return copied == buffer.Length ? copied : copied +
				await _source.ReadAsync(buffer[copied..], cancellationToken);
		}

		private int CopyPrefix(Span<byte> destination)
		{
			var available = _prefixLength - _position;
			if (available <= 0 || destination.Length == 0)
				return 0;
			var count = Math.Min(available, destination.Length);
			_prefix.AsSpan(_position, count).CopyTo(destination);
			_position += count;
			return count;
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin)
			=> throw new NotSupportedException();

		public override void SetLength(long value)
			=> throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				_source.Dispose();
			base.Dispose(disposing);
		}
	}
}
