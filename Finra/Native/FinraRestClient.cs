namespace StockSharp.Finra.Native;

sealed class FinraApiException : InvalidOperationException
{
	public FinraApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class FinraRestClient : BaseLogReceiver, IDisposable
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
	};

	private readonly Uri _address;
	private readonly Uri _authAddress;
	private readonly string _clientId;
	private readonly string _clientSecret;
	private readonly bool _usesStaticToken;
	private readonly int _dataVersion;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _tokenSync = new(1, 1);

	private string _accessToken;
	private DateTimeOffset _tokenExpiresAt;

	public FinraRestClient(
		Uri address,
		Uri authAddress,
		string clientId,
		string clientSecret,
		string accessToken,
		int dataVersion,
		HttpMessageHandler handler = null)
	{
		_address = EnsureTrailingSlash(
			address ?? throw new ArgumentNullException(nameof(address)));
		_authAddress = authAddress ??
			throw new ArgumentNullException(nameof(authAddress));
		_clientId = clientId;
		_clientSecret = clientSecret;
		_accessToken = accessToken;
		_usesStaticToken = !accessToken.IsEmpty();
		_dataVersion = dataVersion;
		_tokenExpiresAt = _usesStaticToken
			? DateTimeOffset.MaxValue
			: DateTimeOffset.MinValue;
		_http = handler is null
			? new HttpClient()
			: new HttpClient(handler);
		_http.Timeout = TimeSpan.FromMinutes(2);
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"User-Agent", "StockSharp-Finra/1.0");
	}

	public Task EnsureAuthenticated(CancellationToken cancellationToken)
		=> GetAccessToken(cancellationToken);

	public async Task<FinraPartitionsResponse> GetPartitions(
		string dataset,
		CancellationToken cancellationToken)
	{
		var result = await SendApi(
			HttpMethod.Get,
			CreateAddress(
				$"partitions/group/otcMarket/name/{Escape(dataset)}"),
			null,
			cancellationToken);

		if (result.Body.IsEmpty())
			return new FinraPartitionsResponse
			{
				AvailablePartitions = [],
			};

		return JsonConvert.DeserializeObject<FinraPartitionsResponse>(
				result.Body, _jsonSettings)
			?? throw new InvalidOperationException(
				$"FINRA returned empty partition metadata for '{dataset}'.");
	}

	public async Task<FinraPage<T>> Query<T>(
		string dataset,
		FinraQueryRequest query,
		CancellationToken cancellationToken)
	{
		if (query is null)
			throw new ArgumentNullException(nameof(query));

		var payload = JsonConvert.SerializeObject(query, _jsonSettings);
		var result = await SendApi(
			HttpMethod.Post,
			CreateAddress($"data/group/otcMarket/name/{Escape(dataset)}"),
			payload,
			cancellationToken);

		T[] items;
		if (result.StatusCode == HttpStatusCode.NoContent ||
			result.Body.IsEmpty())
		{
			items = [];
		}
		else
		{
			items = JsonConvert.DeserializeObject<T[]>(
					result.Body, _jsonSettings)
				?? [];
		}

		return new FinraPage<T>
		{
			Items = items,
			TotalRecords = result.TotalRecords,
			RecordOffset = result.RecordOffset,
			RecordLimit = result.RecordLimit,
		};
	}

	public async Task<T[]> QueryAll<T>(
		string dataset,
		FinraQueryRequest query,
		int pageSize,
		int maxRecords,
		CancellationToken cancellationToken)
	{
		if (pageSize is < 1 or > 5000)
			throw new ArgumentOutOfRangeException(
				nameof(pageSize), pageSize,
				"FINRA synchronous page size must be from 1 to 5000.");
		if (maxRecords < 1)
			throw new ArgumentOutOfRangeException(nameof(maxRecords));

		var result = new List<T>(Math.Min(maxRecords, pageSize));
		var offset = 0;

		while (result.Count < maxRecords && offset <= 500000)
		{
			query.Offset = offset;
			query.Limit = Math.Min(pageSize, maxRecords - result.Count);

			var page = await Query<T>(
				dataset, query, cancellationToken);
			var items = page.Items ?? [];
			if (items.Length == 0)
				break;

			result.AddRange(items.Take(maxRecords - result.Count));
			offset += items.Length;

			if (page.TotalRecords is long total)
			{
				if (offset >= total)
					break;
			}
			else if (items.Length < query.Limit)
				break;
		}

		return [.. result];
	}

	private Uri CreateAddress(string relative)
		=> new(_address, relative);

	private async Task<string> GetAccessToken(
		CancellationToken cancellationToken)
	{
		if (!_accessToken.IsEmpty() &&
			_tokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
		{
			return _accessToken;
		}

		if (_usesStaticToken)
			return _accessToken.ThrowIfEmpty(nameof(_accessToken));
		if (_clientId.IsEmpty() || _clientSecret.IsEmpty())
		{
			throw new InvalidOperationException(
				"FINRA API client ID and client secret are required when an access token is not supplied.");
		}

		await _tokenSync.WaitAsync(cancellationToken);
		try
		{
			if (!_accessToken.IsEmpty() &&
				_tokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
			{
				return _accessToken;
			}

			var basic = Convert.ToBase64String(
				Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));

			for (var attempt = 0; attempt < 4; attempt++)
			{
				using var request = new HttpRequestMessage(
					HttpMethod.Post, _authAddress);
				request.Headers.Authorization =
					new AuthenticationHeaderValue("Basic", basic);
				request.Content = new StringContent(
					string.Empty,
					Encoding.UTF8,
					"application/x-www-form-urlencoded");

				using var response = await _http.SendAsync(
					request,
					HttpCompletionOption.ResponseContentRead,
					cancellationToken);
				var body = await response.Content.ReadAsStringAsync(
					cancellationToken);

				if (IsTransient(response.StatusCode) && attempt < 3)
				{
					await Task.Delay(
						GetRetryDelay(response, attempt),
						cancellationToken);
					continue;
				}
				if (!response.IsSuccessStatusCode)
					throw CreateApiError(response.StatusCode, body, _authAddress);

				var token = JsonConvert.DeserializeObject<FinraTokenResponse>(
						body, _jsonSettings)
					?? throw new InvalidOperationException(
						"FINRA identity platform returned an empty token response.");
				_accessToken = token.AccessToken.ThrowIfEmpty(
					nameof(token.AccessToken));

				var effectiveSeconds = token.ExpiresIn <= 0
					? 1800
					: Math.Min(Math.Max(token.ExpiresIn - 60, 30), 1800);
				_tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(
					effectiveSeconds);
				return _accessToken;
			}

			throw new InvalidOperationException(
				"FINRA token request exhausted its retry limit.");
		}
		finally
		{
			_tokenSync.Release();
		}
	}

	private async Task<FinraHttpResult> SendApi(
		HttpMethod method,
		Uri address,
		string payload,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; attempt < 4; attempt++)
		{
			var token = await GetAccessToken(cancellationToken);
			using var request = new HttpRequestMessage(method, address);
			request.Headers.Authorization =
				new AuthenticationHeaderValue("Bearer", token);
			if (_dataVersion > 0)
			{
				request.Headers.TryAddWithoutValidation(
					"data-version",
					_dataVersion.ToString(CultureInfo.InvariantCulture));
			}
			if (payload is not null)
			{
				request.Content = new StringContent(
					payload, Encoding.UTF8, "application/json");
			}

			using var response = await _http.SendAsync(
				request,
				HttpCompletionOption.ResponseContentRead,
				cancellationToken);
			var body = await response.Content.ReadAsStringAsync(
				cancellationToken);

			if (response.StatusCode == HttpStatusCode.Unauthorized &&
				!_usesStaticToken && attempt < 3)
			{
				InvalidateToken(token);
				continue;
			}
			if (IsTransient(response.StatusCode) && attempt < 3)
			{
				await Task.Delay(
					GetRetryDelay(response, attempt),
					cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body, address);

			return new FinraHttpResult
			{
				StatusCode = response.StatusCode,
				Body = body,
				TotalRecords = ReadLongHeader(
					response, "Record-Total", "Total-Records"),
				RecordOffset = ReadIntHeader(
					response, "Record-Offset"),
				RecordLimit = ReadIntHeader(
					response, "Record-Limit"),
			};
		}

		throw new InvalidOperationException(
			$"FINRA request '{address}' exhausted its retry limit.");
	}

	private void InvalidateToken(string token)
	{
		if (_accessToken == token)
		{
			_accessToken = null;
			_tokenExpiresAt = DateTimeOffset.MinValue;
		}
	}

	private static long? ReadLongHeader(
		HttpResponseMessage response,
		params string[] names)
	{
		foreach (var name in names)
		{
			if (response.Headers.TryGetValues(name, out var values) &&
				long.TryParse(
					values.FirstOrDefault(),
					NumberStyles.Integer,
					CultureInfo.InvariantCulture,
					out var value))
			{
				return value;
			}
		}

		return null;
	}

	private static int? ReadIntHeader(
		HttpResponseMessage response,
		params string[] names)
	{
		var value = ReadLongHeader(response, names);
		return value is >= int.MinValue and <= int.MaxValue
			? (int)value.Value
			: null;
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode is >= 500 and <= 511;

	private static TimeSpan GetRetryDelay(
		HttpResponseMessage response,
		int attempt)
	{
		var delay = response.Headers.RetryAfter?.Delta;
		if (delay is null && response.Headers.RetryAfter?.Date is not null)
		{
			delay = response.Headers.RetryAfter.Date.Value -
				DateTimeOffset.UtcNow;
		}

		if (delay is not null && delay.Value > TimeSpan.Zero)
		{
			return delay > TimeSpan.FromSeconds(30)
				? TimeSpan.FromSeconds(30)
				: delay.Value;
		}

		return TimeSpan.FromSeconds(Math.Pow(2, attempt));
	}

	private static FinraApiException CreateApiError(
		HttpStatusCode statusCode,
		string body,
		Uri address)
	{
		string details = null;
		try
		{
			var json = JObject.Parse(body);
			details = (string)json["message"] ??
				(string)json["statusDescription"] ??
				(string)json["error_description"] ??
				(string)json["error"];
		}
		catch (JsonException)
		{
		}

		if (details.IsEmpty())
		{
			details = body?.Length > 2000
				? body[..2000]
				: body;
		}

		return new FinraApiException(
			statusCode,
			$"FINRA request '{address}' failed ({(int)statusCode} {statusCode}): {details}");
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private static Uri EnsureTrailingSlash(Uri address)
	{
		if (!address.IsAbsoluteUri)
			throw new ArgumentException(
				"FINRA API address must be absolute.", nameof(address));

		var value = address.AbsoluteUri;
		return value.EndsWith('/')
			? address
			: new Uri(value + "/");
	}

	protected override void DisposeManaged()
	{
		_tokenSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	sealed class FinraHttpResult
	{
		public HttpStatusCode StatusCode { get; set; }
		public string Body { get; set; }
		public long? TotalRecords { get; set; }
		public int? RecordOffset { get; set; }
		public int? RecordLimit { get; set; }
	}
}
