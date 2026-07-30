namespace StockSharp.CoinSwitch;

public partial class CoinSwitchMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(
		ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null || _wsClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		if (Key.IsEmpty() || Secret.IsEmpty())
			throw new InvalidOperationException(
				"CoinSwitch API key and Ed25519 secret are required " +
					"for REST market discovery.");
		if (PollingInterval <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(
				nameof(PollingInterval),
				PollingInterval,
				"CoinSwitch polling interval must be positive.");

		SpotExchange = NormalizeExchange(SpotExchange);
		RestEndpoint = NormalizeEndpoint(
			RestEndpoint, _defaultRestEndpoint, "https");
		HftEndpoint = NormalizeEndpoint(
			HftEndpoint, _defaultHftEndpoint, "https");
		WebSocketEndpoint = NormalizeEndpoint(
			WebSocketEndpoint, _defaultWebSocketEndpoint, "wss");

		ClearState();
		await SendOutConnectionStateAsync(
			ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(
				RestEndpoint,
				HftEndpoint,
				Key,
				Secret,
				SpotExchange)
			{
				Parent = this,
			};

			var markets = await DiscoverMarketsAsync(
				cancellationToken);
			if (markets.Length == 0)
				throw new InvalidDataException(
					"CoinSwitch returned no markets.");
			RegisterMarkets(markets);

			if (ProductType != CoinSwitchProductTypes.Options)
			{
				_wsClient = new(
					WebSocketEndpoint,
					ProductType,
					ProductType == CoinSwitchProductTypes.Spot
						? SpotExchange
						: "exchange_2",
					ReConnectionSettings.WorkingTime,
					ReConnectionSettings.ReAttemptCount)
				{
					Parent = this,
				};
				_wsClient.MarketDataReceived +=
					OnWebSocketMarketDataAsync;
				_wsClient.Error += OnWebSocketErrorAsync;
				_wsClient.StateChanged += OnWebSocketStateAsync;
				await _wsClient.ConnectAsync(cancellationToken);
			}

			await SendOutConnectionStateAsync(
				ConnectionStates.Connected,
				cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(cancellationToken);
			await SendOutConnectionStateAsync(
				ConnectionStates.Disconnected,
				cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		_ = disconnectMsg;
		EnsureConnected();
		await SendOutConnectionStateAsync(
			ConnectionStates.Disconnecting,
			cancellationToken);
		await DisposeClientsAsync(cancellationToken);
		await SendOutConnectionStateAsync(
			ConnectionStates.Disconnected,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(
		ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClientsAsync(cancellationToken);
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(
		TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		_ = timeMsg;
		if (_restClient is null ||
			CurrentTime - _lastPoll < PollingInterval)
			return;
		if (!await _pollSync.WaitAsync(0, cancellationToken))
			return;
		try
		{
			if (CurrentTime - _lastPoll < PollingInterval)
				return;
			_lastPoll = CurrentTime;
			await PollSubscriptionsAsync(cancellationToken);
		}
		catch (Exception error)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
		finally
		{
			_pollSync.Release();
		}
	}

	private async ValueTask<CoinSwitchMarket[]> DiscoverMarketsAsync(
		CancellationToken cancellationToken)
		=> ProductType switch
		{
			CoinSwitchProductTypes.Spot =>
				await DiscoverSpotMarketsAsync(cancellationToken),
			CoinSwitchProductTypes.Futures =>
				await DiscoverFuturesMarketsAsync(cancellationToken),
			CoinSwitchProductTypes.Options =>
				await DiscoverOptionMarketsAsync(cancellationToken),
			_ => throw new ArgumentOutOfRangeException(
				nameof(ProductType),
				ProductType,
				LocalizedStrings.InvalidValue),
		};

	private async ValueTask<CoinSwitchMarket[]>
		DiscoverSpotMarketsAsync(
			CancellationToken cancellationToken)
	{
		var symbols = await RestClient.GetSpotSymbolsAsync(
			cancellationToken);
		var rules = await RestClient.GetSpotTradeInfoAsync(
			null, cancellationToken);
		var result = new List<CoinSwitchMarket>(symbols.Length);

		foreach (var symbol in symbols)
		{
			var parts = symbol?.Split('/');
			if (parts is not { Length: 2 })
				continue;
			rules.TryGetValue(symbol, out var rule);
			result.Add(new()
			{
				NativeSymbol = symbol.Trim().ToUpperInvariant(),
				SecurityCode = CoinSwitchExtensions.CreateSecurityCode(
					parts[0], parts[1]),
				BaseCurrency = parts[0].Trim().ToUpperInvariant(),
				QuoteCurrency = parts[1].Trim().ToUpperInvariant(),
				SecurityType = SecurityTypes.CryptoCurrency,
				State = SecurityStates.Trading,
				PriceStep = rule?.PriceStep,
				VolumeStep = rule?.VolumeStep,
			});
		}

		return [.. result];
	}

	private async ValueTask<CoinSwitchMarket[]>
		DiscoverFuturesMarketsAsync(
			CancellationToken cancellationToken)
		=> [.. (await RestClient.GetFuturesInstrumentsAsync(
				cancellationToken))
			.Where(static instrument =>
				!instrument.NativeSymbol.IsEmpty() &&
				!instrument.BaseAsset.IsEmpty() &&
				!instrument.QuoteAsset.IsEmpty())
			.Select(static instrument => new CoinSwitchMarket
			{
				NativeSymbol = instrument.NativeSymbol,
				SecurityCode = instrument.SecurityCode,
				BaseCurrency =
					instrument.BaseAsset.ToUpperInvariant(),
				QuoteCurrency =
					instrument.QuoteAsset.ToUpperInvariant(),
				SecurityType = SecurityTypes.Future,
				State = instrument.Status.EqualsIgnoreCase("TRADING")
					? SecurityStates.Trading
					: SecurityStates.Stoped,
				PriceStep = instrument.PriceStep,
				VolumeStep = instrument.VolumeStep,
				MinimumVolume = instrument.MinimumVolume,
				MaximumVolume = instrument.MaximumVolume,
			})];

	private async ValueTask<CoinSwitchMarket[]>
		DiscoverOptionMarketsAsync(
			CancellationToken cancellationToken)
		=> [.. (await RestClient.GetHftInstrumentsAsync(
				cancellationToken))
			.Where(static instrument =>
				!instrument.Symbol.IsEmpty() &&
				!instrument.BaseCoin.IsEmpty())
			.Select(static instrument => new CoinSwitchMarket
			{
				NativeSymbol =
					instrument.Symbol.Trim().ToUpperInvariant(),
				SecurityCode =
					instrument.Symbol.Trim().ToUpperInvariant(),
				BaseCurrency =
					instrument.BaseCoin.Trim().ToUpperInvariant(),
				QuoteCurrency =
					(instrument.QuoteCoin ??
						instrument.SettlementCoin ??
						"USDT").Trim().ToUpperInvariant(),
				SecurityType = SecurityTypes.Option,
				State = instrument.Status.EqualsIgnoreCase("TRADING")
					? SecurityStates.Trading
					: SecurityStates.Stoped,
				PriceStep = instrument.PriceStep,
				VolumeStep = instrument.VolumeStep,
				MinimumVolume = instrument.LotSize?.MinimumQuantity,
				MaximumVolume = instrument.LotSize?.MaximumQuantity,
				ExpiryDate = instrument.DeliveryTime > 0
					? instrument.DeliveryTime
						.FromCoinSwitchMilliseconds()
					: null,
				OptionType = instrument.OptionType,
				Strike = instrument.Strike,
			})];

	private ValueTask OnWebSocketErrorAsync(
		Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnWebSocketStateAsync(
		ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
			await SendOutConnectionStateAsync(
				ConnectionStates.Failed,
				cancellationToken);
		else if (state == ConnectionStates.Restored)
			await SendOutConnectionStateAsync(
				ConnectionStates.Restored,
				cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var wsClient = _wsClient;
		_wsClient = null;
		if (wsClient is not null)
		{
			UnsubscribeClientEvents(wsClient);
			try
			{
				await wsClient.DisconnectAsync(cancellationToken);
			}
			catch (Exception error)
			{
				if (!cancellationToken.IsCancellationRequested)
					await SendOutErrorAsync(
						error, cancellationToken);
			}
			finally
			{
				wsClient.Dispose();
			}
		}
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}

	private void DisposeClients()
	{
		if (_wsClient is not null)
			UnsubscribeClientEvents(_wsClient);
		_wsClient?.Dispose();
		_wsClient = null;
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}

	private void UnsubscribeClientEvents(CoinSwitchWsClient client)
	{
		client.MarketDataReceived -= OnWebSocketMarketDataAsync;
		client.Error -= OnWebSocketErrorAsync;
		client.StateChanged -= OnWebSocketStateAsync;
	}
}
