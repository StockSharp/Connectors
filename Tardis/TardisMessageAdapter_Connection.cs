namespace StockSharp.Tardis;

public partial class TardisMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest is not null || _machine is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		Exchange = TardisExtensions.NormalizeExchange(Exchange);

		var rest = new TardisRestClient(ApiEndpoint, Token, RequestInterval)
		{
			Parent = this,
		};
		var machine = new TardisMachineRestClient(MachineHttpEndpoint)
		{
			Parent = this,
		};
		ValidateSocketEndpoint(MachineSocketEndpoint);
		_rest = rest;
		_machine = machine;
		try
		{
			CacheInstruments(await rest.GetInstrumentsAsync(Exchange,
				cancellationToken));
			if (GetInstruments().Length == 0)
				throw new InvalidDataException(
					"Tardis returned an empty instrument catalogue for the configured exchange.");
			await base.ConnectAsync(message, cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest is null || _machine is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await DisposeClientsAsync(cancellationToken);
		ClearState();
		await base.DisconnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message,
		CancellationToken cancellationToken)
	{
		await DisposeClientsAsync(cancellationToken);
		ClearState();
		await base.ResetAsync(message, cancellationToken);
	}

	private TardisMachineRestClient SafeMachine()
		=> _machine ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void CacheInstruments(IEnumerable<TardisInstrument> instruments)
	{
		using (_sync.EnterScope())
		{
			_instruments.Clear();
			foreach (var instrument in instruments.Where(IsValidInstrument))
				_instruments[instrument.Key] = instrument;
		}
	}

	private TardisInstrument[] GetInstruments()
	{
		using (_sync.EnterScope())
			return [.. _instruments.Values];
	}

	private TardisInstrument ResolveInstrument(SecurityId securityId)
	{
		var native = (securityId.Native as string)?.Trim();
		var code = securityId.SecurityCode?.Trim();
		using (_sync.EnterScope())
		{
			if (!native.IsEmpty() && _instruments.TryGetValue(native, out var exact))
				return exact;
			if (code.IsEmpty())
				throw new ArgumentException("Tardis security code is not specified.",
					nameof(securityId));
			var matches = _instruments.Values.Where(instrument =>
				instrument.Id.EqualsIgnoreCase(code) ||
				instrument.DatasetId.EqualsIgnoreCase(code)).Take(2).ToArray();
			if (matches.Length == 1)
				return matches[0];
		}
		throw new InvalidOperationException(
			$"Tardis instrument '{code}' is unknown or ambiguous. Use security lookup to preserve its metadata identity.");
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		Exception firstError = null;
		await _streamGate.WaitAsync(cancellationToken);
		try
		{
			TardisMachineStreamClient[] streams;
			using (_sync.EnterScope())
			{
				streams = [.. _streams.Values];
				_streams.Clear();
				_liveSubscriptions.Clear();
			}
			foreach (var stream in streams)
			{
				stream.MessageReceived -= OnStreamMessageAsync;
				stream.Error -= SendOutErrorAsync;
				try
				{
					await stream.DisconnectAsync(cancellationToken);
				}
				catch (Exception error)
				{
					firstError ??= error;
				}
				finally
				{
					stream.Dispose();
				}
			}
		}
		finally
		{
			_streamGate.Release();
		}

		_machine?.Dispose();
		_machine = null;
		_rest?.Dispose();
		_rest = null;
		if (firstError is not null)
			throw firstError;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_instruments.Clear();
			_liveSubscriptions.Clear();
			_streams.Clear();
		}
	}

	private bool IsValidInstrument(TardisInstrument instrument)
	{
		if (instrument is null || instrument.Id.IsEmpty() ||
			instrument.Exchange.IsEmpty() || instrument.AvailableSince.IsEmpty() ||
			instrument.Type == TardisInstrumentTypes.Unknown ||
			!instrument.Exchange.EqualsIgnoreCase(Exchange))
			return false;
		try
		{
			_ = instrument.AvailableSince.ParseTardisTime("availableSince");
			_ = instrument.AvailableTo.TryParseTardisTime("availableTo");
			_ = instrument.Expiry.TryParseTardisTime("expiry");
			return true;
		}
		catch (InvalidDataException)
		{
			return false;
		}
	}

	private static void ValidateSocketEndpoint(string endpoint)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/') + "/",
			UriKind.Absolute, out var uri) || uri.Scheme is not ("ws" or "wss") ||
			uri.Host.IsEmpty() || !uri.UserInfo.IsEmpty() || !uri.Query.IsEmpty() ||
			!uri.Fragment.IsEmpty())
			throw new ArgumentException(
				"Tardis Machine WebSocket endpoint must be an absolute WS(S) URI without credentials, query, or fragment.",
				nameof(endpoint));
	}
}
