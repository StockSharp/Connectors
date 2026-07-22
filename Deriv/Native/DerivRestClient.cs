namespace StockSharp.Deriv.Native;

sealed class DerivRestClient : BaseLogReceiver
{
	private readonly Uri _address;
	private readonly string _token;
	private readonly string _appId;
	private readonly HttpClient _client = new();

	public DerivRestClient(Uri address, SecureString token, string appId)
	{
		if (address is null || !address.IsAbsoluteUri || address.Scheme is not ("http" or "https"))
			throw new ArgumentException("Deriv REST address must be an absolute HTTP or HTTPS URI.",
				nameof(address));

		_address = new(address.ToString().TrimEnd('/') + "/", UriKind.Absolute);
		_token = (token?.UnSecure()).ThrowIfEmpty(nameof(token));
		_appId = appId.ThrowIfEmpty(nameof(appId));
		_client.Timeout = TimeSpan.FromSeconds(30);
	}

	public override string Name => nameof(Deriv) + "_REST";

	public async ValueTask<DerivRestAccount[]> GetAccountsAsync(
		CancellationToken cancellationToken)
	{
		var envelope = await SendAsync<DerivRestEnvelope<DerivRestAccount[]>>(
			HttpMethod.Get, "trading/v1/options/accounts", cancellationToken);
		return envelope?.Data ?? [];
	}

	public async ValueTask<string> GetWebSocketUrlAsync(string accountId,
		CancellationToken cancellationToken)
	{
		var path = $"trading/v1/options/accounts/{Uri.EscapeDataString(
			accountId.ThrowIfEmpty(nameof(accountId)))}/otp";
		var envelope = await SendAsync<DerivRestEnvelope<DerivWebSocketData>>(
			HttpMethod.Post, path, cancellationToken);
		var url = envelope?.Data?.Url;
		if (url.IsEmpty())
			throw new InvalidDataException("Deriv did not return an authenticated WebSocket URL.");
		return url;
	}

	private async ValueTask<T> SendAsync<T>(HttpMethod method, string path,
		CancellationToken cancellationToken)
		where T : class
	{
		using var request = new HttpRequestMessage(method, new Uri(_address, path));
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
		request.Headers.TryAddWithoutValidation("Deriv-App-ID", _appId);
		request.Headers.UserAgent.ParseAdd("StockSharp-Deriv-Connector/1.0");
		if (method == HttpMethod.Post)
			request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

		using var response = await _client.SendAsync(request,
			HttpCompletionOption.ResponseContentRead, cancellationToken);
		var payload = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateError(response.StatusCode, payload);

		try
		{
			return JsonConvert.DeserializeObject<T>(payload)
				?? throw new InvalidDataException("Deriv REST returned an empty JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Deriv REST returned invalid JSON.", error);
		}
	}

	private static Exception CreateError(HttpStatusCode statusCode, string payload)
	{
		try
		{
			var error = JsonConvert.DeserializeObject<DerivRestErrorEnvelope>(payload)
				?.Errors?.FirstOrDefault();
			if (error is not null)
			{
				var field = error.Field.IsEmpty() ? string.Empty : $" Field: {error.Field}.";
				return new HttpRequestException(
					$"Deriv REST error {error.Code.IsEmpty(statusCode.ToString())}:{field} " +
					$"{error.Message.IsEmpty(statusCode.ToString())}",
					null, statusCode);
			}
		}
		catch (JsonException)
		{
		}

		return new HttpRequestException(
			$"Deriv REST request failed with HTTP {(int)statusCode} {statusCode}.",
			null, statusCode);
	}

	protected override void DisposeManaged()
	{
		_client.Dispose();
		base.DisposeManaged();
	}
}
