namespace StockSharp.Quodd.Native;

sealed class QuoddApiException : InvalidOperationException
{
	public QuoddApiException(string message)
		: base(message)
	{
	}

	public QuoddApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode? StatusCode { get; }
}

sealed class QuoddAuthClient : IDisposable
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly Uri _address;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

	public QuoddAuthClient(Uri address)
	{
		if (address == null || !address.IsAbsoluteUri || address.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException("QUODD authentication address must be an absolute HTTPS URI.", nameof(address));

		_address = address;
		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "StockSharp-QUODD/1.0");
	}

	public Task<string> GetTrialToken(string username, string password,
		CancellationToken cancellationToken)
	{
		username.ThrowIfEmpty(nameof(username));
		password.ThrowIfEmpty(nameof(password));
		return GetToken("/vor/quodd/login/trial/token", request =>
		{
			request.Headers.TryAddWithoutValidation("username", username);
			request.Headers.TryAddWithoutValidation("password", password);
		}, cancellationToken);
	}

	public Task<string> GetFirmToken(string username, string firmLogin, string firmPassword,
		CancellationToken cancellationToken)
	{
		username.ThrowIfEmpty(nameof(username));
		firmLogin.ThrowIfEmpty(nameof(firmLogin));
		firmPassword.ThrowIfEmpty(nameof(firmPassword));
		return GetToken("/vor/quodd/api/login/token", request =>
		{
			request.Headers.TryAddWithoutValidation("username", username);
			var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{firmLogin}:{firmPassword}"));
			request.Headers.Authorization = new("Basic", basic);
		}, cancellationToken);
	}

	private async Task<string> GetToken(string path, Action<HttpRequestMessage> configure,
		CancellationToken cancellationToken)
	{
		var uri = new Uri(_address, path);
		using var request = new HttpRequestMessage(HttpMethod.Post, uri);
		configure(request);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateError(response.StatusCode, content, uri.AbsolutePath);

		QuoddTokenResponse value;
		try
		{
			value = JsonConvert.DeserializeObject<QuoddTokenResponse>(content, _jsonSettings);
		}
		catch (JsonException ex)
		{
			throw new QuoddApiException($"QUODD token response is not valid JSON: {ex.Message}");
		}

		return (value?.Token).ThrowIfEmpty("QUODD token response");
	}

	private static QuoddApiException CreateError(HttpStatusCode statusCode, string content,
		string path)
	{
		QuoddErrorResponse value = null;
		try
		{
			value = JsonConvert.DeserializeObject<QuoddErrorResponse>(content, _jsonSettings);
		}
		catch (JsonException)
		{
		}

		var details = (value?.GetMessage()).IsEmpty(content);
		if (details?.Length > 1000)
			details = details[..1000];
		return new(statusCode, $"QUODD request '{path}' failed ({(int)statusCode} {statusCode})" +
			(details.IsEmpty() ? "." : $": {details}"));
	}

	public void Dispose()
		=> _http.Dispose();
}
