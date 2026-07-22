namespace StockSharp.CryptoQuant;

public partial class CryptoQuantMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		_ = PriceTimeFrame.ToWindow();

		var client = new CryptoQuantRestClient(ApiEndpoint, Token, RequestInterval)
		{
			Parent = this,
		};
		_rest = client;
		try
		{
			CacheInstruments(await client.GetEndpointsAsync(cancellationToken));
			if (GetInstruments().Length == 0)
				throw new InvalidDataException(
					"CryptoQuant discovery returned no supported price OHLCV endpoints.");
			await base.ConnectAsync(message, cancellationToken);
		}
		catch
		{
			DisposeClient();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		DisposeClient();
		await base.DisconnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message,
		CancellationToken cancellationToken)
	{
		DisposeClient();
		await base.ResetAsync(message, cancellationToken);
	}

	private CryptoQuantRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void DisposeClient()
	{
		_rest?.Dispose();
		_rest = null;
		using (_sync.EnterScope())
			_instruments.Clear();
	}

	private void CacheInstruments(IEnumerable<CryptoQuantEndpoint> endpoints)
	{
		var instruments = BuildInstruments(endpoints);
		using (_sync.EnterScope())
		{
			_instruments.Clear();
			foreach (var instrument in instruments)
				_instruments[instrument.Key] = instrument;
		}
	}

	private static CryptoQuantInstrument[] BuildInstruments(
		IEnumerable<CryptoQuantEndpoint> endpoints)
	{
		var result = new Dictionary<string, CryptoQuantInstrument>(
			StringComparer.OrdinalIgnoreCase);
		foreach (var endpoint in endpoints ?? [])
		{
			if (endpoint?.Path.IsEmpty() != false)
				continue;
			var segments = endpoint.Path.Trim().Trim('/').Split('/');
			if (segments.Length != 4 ||
				!segments[0].EqualsIgnoreCase("v1") ||
				!segments[2].EqualsIgnoreCase("market-data") ||
				!segments[3].EqualsIgnoreCase("price-ohlcv"))
				continue;

			string routeNamespace;
			try
			{
				routeNamespace = CryptoQuantExtensions.NormalizeIdentifier(
					segments[1], nameof(endpoint.Path));
			}
			catch (ArgumentException)
			{
				continue;
			}

			var parameters = endpoint.Parameters ?? [];
			var windows = parameters
				.Where(static parameter => parameter is not null)
				.SelectMany(static parameter => parameter.Window ?? [])
				.Where(static window => window != CryptoQuantWindows.Unknown)
				.Distinct()
				.OrderBy(static window => window)
				.ToArray();
			if (windows.Length == 0)
				windows =
				[
					CryptoQuantWindows.Minute,
					CryptoQuantWindows.Hour,
					CryptoQuantWindows.Day,
				];

			var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var token in parameters
				.Where(static parameter => parameter is not null)
				.SelectMany(static parameter => parameter.Token ?? []))
			{
				try
				{
					tokens.Add(CryptoQuantExtensions.NormalizeIdentifier(token,
						nameof(CryptoQuantEndpointParameter.Token)));
				}
				catch (ArgumentException)
				{
				}
			}

			if (tokens.Count == 0)
			{
				var instrument = new CryptoQuantInstrument
				{
					Kind = CryptoQuantInstrumentKinds.NativeAsset,
					Namespace = routeNamespace,
					Token = string.Empty,
					Windows = windows,
				};
				result[instrument.Key] = instrument;
			}
			else
			{
				foreach (var token in tokens)
				{
					var instrument = new CryptoQuantInstrument
					{
						Kind = CryptoQuantInstrumentKinds.Token,
						Namespace = routeNamespace,
						Token = token,
						Windows = windows,
					};
					result[instrument.Key] = instrument;
				}
			}
		}
		return [.. result.Values];
	}

	private CryptoQuantInstrument[] GetInstruments()
	{
		using (_sync.EnterScope())
			return [.. _instruments.Values];
	}

	private CryptoQuantInstrument ResolveInstrument(SecurityId securityId)
	{
		var native = (securityId.Native as string)?.Trim();
		var requestedCode = securityId.SecurityCode?.Trim();
		using (_sync.EnterScope())
		{
			if (!native.IsEmpty() && _instruments.TryGetValue(native, out var exact))
				return exact;
			if (requestedCode.IsEmpty())
				throw new ArgumentException(
					"CryptoQuant security code is not specified.", nameof(securityId));
			var matches = _instruments.Values.Where(instrument =>
				instrument.Code.EqualsIgnoreCase(requestedCode) ||
				instrument.Symbol.EqualsIgnoreCase(requestedCode) ||
				instrument.Key.EqualsIgnoreCase(requestedCode)).Take(2).ToArray();
			if (matches.Length == 1)
				return matches[0];
		}
		throw new InvalidOperationException(
			$"CryptoQuant instrument '{requestedCode}' is unknown or ambiguous. Use security lookup to preserve its discovery identity.");
	}
}
