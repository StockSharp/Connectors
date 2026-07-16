namespace StockSharp.LsegRealTime.Native;

internal sealed class LsegRealTimeClient : IDisposable
{
	private sealed class LsegSnapshotRequest
	{
		public string Ric { get; init; }
		public TaskCompletionSource<LsegSecuritySnapshot> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private static readonly string[] _marketPriceView =
	[
		"BID", "ASK", "BIDSIZE", "ASKSIZE", "TRDPRC_1", "TRDVOL_1", "OPEN_PRC", "HIGH_1",
		"LOW_1", "HST_CLOSE", "ACVOL_1", "OPEN_INT", "VWAP", "NETCHNG_1", "PCTCHNG",
		"QUOTIM_MS", "TRDTIM_MS", "TRADE_DATE", "DSPLY_NAME", "CURRENCY", "RDN_EXCHID",
		"RDN_EXCHD2", "LOT_SIZE_A", "RECORDTYPE",
	];

	private static readonly string[] _marketByPriceView =
	[
		"ORDER_PRC", "ORDER_SIDE", "ACC_SIZE", "NO_ORD", "LV_DATE", "LV_TIM_MS",
	];

	private readonly LsegClientConfiguration _configuration;
	private readonly HttpClient _httpClient;
	private readonly ConcurrentDictionary<long, LsegSubscription> _subscriptions = [];
	private readonly ConcurrentDictionary<long, long> _externalStreams = [];
	private readonly ConcurrentDictionary<long, LsegSnapshotRequest> _snapshots = [];
	private readonly ConcurrentDictionary<string, string> _directoryNames = [];
	private readonly ConcurrentDictionary<int, byte> _reconnectingSlots = [];
	private readonly object _connectionsSync = new();

	private LsegWebSocketConnection[] _connections = [];
	private CancellationTokenSource _lifetime;
	private Task _tokenRenewal;
	private string _accessToken;
	private string _refreshToken;
	private int _expiresIn;
	private int _activeSlot;
	private int _isReady;
	private int _isStopping;
	private int _serviceAvailable = 1;
	private long _nextStreamId = 4;
	private long _eventId;

	public LsegRealTimeClient(LsegClientConfiguration configuration)
	{
		_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
		_httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
	}

	public event Func<LsegMarketPriceUpdate, CancellationToken, ValueTask> MarketPriceReceived;
	public event Func<LsegDepthUpdate, CancellationToken, ValueTask> DepthReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<Exception, CancellationToken, ValueTask> ConnectionLost;

	public async Task ConnectAsync(CancellationToken cancellationToken)
	{
		if (_lifetime != null)
			throw new InvalidOperationException("The LSEG Real-Time client is already connected.");

		ValidateConfiguration();
		Interlocked.Exchange(ref _isStopping, 0);
		Interlocked.Exchange(ref _isReady, 0);
		_lifetime = new CancellationTokenSource();

		try
		{
			if (_configuration.AuthenticationMode != LsegAuthenticationModes.Deployed)
				ApplyToken(await RequestTokenAsync(false, cancellationToken));

			var addresses = await ResolveAddressesAsync(cancellationToken);
			var connections = addresses
				.Select((address, slot) => CreateConnection(slot, address))
				.ToArray();
			_connections = connections;

			await Task.WhenAll(connections.Select(connection => connection.ConnectAsync(CreateLoginRequest(false), cancellationToken)));
			_activeSlot = 0;
			Interlocked.Exchange(ref _isReady, 1);
			await Task.WhenAll(connections.Select(connection => connection.SendAsync(new LsegSourceRequest(), cancellationToken)));

			if (_configuration.AuthenticationMode != LsegAuthenticationModes.Deployed)
				_tokenRenewal = Task.Run(() => RenewTokenLoopAsync(_lifetime.Token));
		}
		catch
		{
			await DisconnectCoreAsync();
			throw;
		}
	}

	public async ValueTask DisconnectAsync()
	{
		if (Interlocked.Exchange(ref _isStopping, 1) != 0)
			return;
		await DisconnectCoreAsync();
	}

	public async Task SubscribeAsync(long externalId, string ric, LsegSubscriptionKinds kind, CancellationToken cancellationToken)
	{
		EnsureConnected();
		if (_externalStreams.ContainsKey(externalId))
			throw new InvalidOperationException($"LSEG subscription {externalId} already exists.");

		var streamId = Interlocked.Increment(ref _nextStreamId);
		var subscription = new LsegSubscription
		{
			StreamId = streamId,
			ExternalId = externalId,
			Ric = ric.ThrowIfEmpty(nameof(ric)),
			Kind = kind,
		};
		if (!_externalStreams.TryAdd(externalId, streamId) || !_subscriptions.TryAdd(streamId, subscription))
			throw new InvalidOperationException($"LSEG subscription {externalId} already exists.");

		try
		{
			var request = CreateItemRequest(subscription, true);
			await Task.WhenAll(GetOpenConnections().Select(connection => connection.SendAsync(request, cancellationToken)));
		}
		catch
		{
			_externalStreams.TryRemove(externalId, out _);
			_subscriptions.TryRemove(streamId, out _);
			throw;
		}
	}

	public async Task UnsubscribeAsync(long externalId, CancellationToken cancellationToken)
	{
		EnsureConnected();
		if (!_externalStreams.TryRemove(externalId, out var streamId))
			return;
		_subscriptions.TryRemove(streamId, out _);
		var close = new LsegCloseRequest { Id = streamId };
		await Task.WhenAll(GetOpenConnections().Select(connection => connection.SendAsync(close, cancellationToken)));
	}

	public async Task<LsegSecuritySnapshot> GetSnapshotAsync(string ric, CancellationToken cancellationToken)
	{
		EnsureConnected();
		var streamId = Interlocked.Increment(ref _nextStreamId);
		var pending = new LsegSnapshotRequest { Ric = ric.ThrowIfEmpty(nameof(ric)) };
		if (!_snapshots.TryAdd(streamId, pending))
			throw new InvalidOperationException("The LSEG snapshot stream identifier is already in use.");

		try
		{
			var request = CreateSnapshotRequest(streamId, pending.Ric);
			await GetActiveConnection().SendAsync(request, cancellationToken);
			return await pending.Completion.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
		}
		finally
		{
			_snapshots.TryRemove(streamId, out _);
			var connection = TryGetActiveConnection();
			if (connection != null)
			{
				try
				{
					await connection.SendAsync(new LsegCloseRequest { Id = streamId }, CancellationToken.None);
				}
				catch
				{
				}
			}
		}
	}

	public void Dispose()
	{
		Interlocked.Exchange(ref _isStopping, 1);
		_lifetime?.Cancel();
		foreach (var connection in _connections)
			connection.Dispose();
		_httpClient.Dispose();
		_lifetime?.Dispose();
		_lifetime = null;
		_connections = [];
		_accessToken = null;
		_refreshToken = null;
	}

	private async Task DisconnectCoreAsync()
	{
		Interlocked.Exchange(ref _isReady, 0);
		var lifetime = _lifetime;
		lifetime?.Cancel();

		var connections = _connections;
		await Task.WhenAll(connections.Select(connection => connection.DisconnectAsync()));
		foreach (var connection in connections)
			connection.Dispose();

		if (_tokenRenewal != null)
		{
			try
			{
				await _tokenRenewal;
			}
			catch (OperationCanceledException)
			{
			}
		}

		var disconnected = new InvalidOperationException("The LSEG Real-Time connection was closed.");
		foreach (var snapshot in _snapshots.ToArray())
		{
			if (_snapshots.TryRemove(snapshot.Key, out var pending))
				pending.Completion.TrySetException(disconnected);
		}

		_subscriptions.Clear();
		_externalStreams.Clear();
		_directoryNames.Clear();
		_connections = [];
		_tokenRenewal = null;
		_lifetime = null;
		_accessToken = null;
		_refreshToken = null;
		lifetime?.Dispose();
	}

	private LsegWebSocketConnection CreateConnection(int slot, Uri address)
		=> new(slot, address, ProcessWireMessageAsync, ProcessConnectionClosedAsync);

	private async ValueTask ProcessWireMessageAsync(int slot, LsegWireMessage message, CancellationToken cancellationToken)
	{
		if (message.Domain.EqualsIgnoreCase("Source") || message.Id == 2)
		{
			if (slot == _activeSlot)
				await ProcessDirectoryAsync(message, cancellationToken);
			return;
		}

		if (_snapshots.TryGetValue(message.Id, out var snapshot))
		{
			ProcessSnapshot(message, snapshot);
			return;
		}

		if (!_subscriptions.TryGetValue(message.Id, out var subscription) || slot != _activeSlot)
			return;

		if (message.Type.EqualsIgnoreCase("Error"))
		{
			await RaiseErrorAsync(CreateWireError(message, subscription.Ric), cancellationToken);
			return;
		}

		if (message.Type.EqualsIgnoreCase("Status"))
		{
			if (CanRecover(message.State))
				await RecoverSubscriptionAsync(subscription, cancellationToken);
			else if (message.State?.Stream.EqualsIgnoreCase("Closed") == true)
				await RaiseErrorAsync(CreateWireError(message, subscription.Ric), cancellationToken);
			return;
		}

		if (!message.Type.EqualsIgnoreCase("Refresh") && !message.Type.EqualsIgnoreCase("Update"))
			return;

		if (message.Type.EqualsIgnoreCase("Refresh"))
		{
			subscription.LastSequence = message.SeqNumber ?? 0;
			Interlocked.Exchange(ref subscription.IsRecovering, 0);
		}
		else if (message.SeqNumber is long sequence)
		{
			if (subscription.LastSequence >= sequence)
				return;
			subscription.LastSequence = sequence;
		}

		var receivedTime = DateTime.UtcNow;
		if (subscription.Kind == LsegSubscriptionKinds.MarketPrice)
		{
			if (message.Fields == null)
				return;
			var handler = MarketPriceReceived;
			if (handler != null)
			{
				await handler(new LsegMarketPriceUpdate
				{
					SubscriptionId = subscription.ExternalId,
					Ric = subscription.Ric,
					IsRefresh = message.Type.EqualsIgnoreCase("Refresh"),
					UpdateType = message.UpdateType,
					Sequence = message.SeqNumber ?? 0,
					EventId = Interlocked.Increment(ref _eventId),
					ReceivedTime = receivedTime,
					State = message.State,
					Fields = message.Fields,
				}, cancellationToken);
			}
		}
		else
		{
			var handler = DepthReceived;
			if (handler != null)
			{
				await handler(new LsegDepthUpdate
				{
					SubscriptionId = subscription.ExternalId,
					Ric = subscription.Ric,
					IsRefresh = message.Type.EqualsIgnoreCase("Refresh"),
					IsComplete = message.Complete != false,
					IsClearCache = message.ClearCache == true,
					PartNumber = message.PartNum,
					Sequence = message.SeqNumber ?? 0,
					ReceivedTime = receivedTime,
					State = message.State,
					Entries = message.Map?.Entries ?? [],
				}, cancellationToken);
			}
		}
	}

	private void ProcessSnapshot(LsegWireMessage message, LsegSnapshotRequest snapshot)
	{
		if (message.Type.EqualsIgnoreCase("Refresh") && message.Fields != null)
		{
			if (message.State != null && !message.State.IsOpenAndOk)
				snapshot.Completion.TrySetException(CreateWireError(message, snapshot.Ric));
			else
				snapshot.Completion.TrySetResult(new LsegSecuritySnapshot
				{
					Ric = message.Key?.Name.IsEmpty(snapshot.Ric),
					ReceivedTime = DateTime.UtcNow,
					Fields = message.Fields,
					State = message.State,
				});
		}
		else if (message.Type.EqualsIgnoreCase("Status") || message.Type.EqualsIgnoreCase("Error"))
			snapshot.Completion.TrySetException(CreateWireError(message, snapshot.Ric));
	}

	private async ValueTask ProcessDirectoryAsync(LsegWireMessage message, CancellationToken cancellationToken)
	{
		if (message.Type.EqualsIgnoreCase("Status") || message.Type.EqualsIgnoreCase("Error"))
		{
			await RaiseErrorAsync(CreateWireError(message, "Source directory"), cancellationToken);
			return;
		}

		foreach (var entry in message.Map?.Entries ?? [])
		{
			string serviceName = null;
			int? serviceState = null;
			int? acceptingRequests = null;
			foreach (var filter in entry.FilterList?.Entries ?? [])
			{
				var elements = filter.Elements;
				if (elements == null)
					continue;
				serviceName = elements.Name.IsEmpty(serviceName);
				serviceState ??= elements.ServiceState;
				acceptingRequests ??= elements.AcceptingRequests;
			}

			if (!serviceName.IsEmpty())
				_directoryNames[entry.Key] = serviceName;
			else
				serviceName = _directoryNames.TryGetValue(entry.Key, out var knownName) ? knownName : null;
			if (!serviceName.EqualsIgnoreCase(_configuration.Service))
				continue;

			var isAvailable = !entry.Action.EqualsIgnoreCase("Delete") && serviceState != 0 && acceptingRequests != 0;
			var previous = Interlocked.Exchange(ref _serviceAvailable, isAvailable ? 1 : 0);
			if (!isAvailable && previous != 0)
				await RaiseErrorAsync(new InvalidOperationException($"LSEG service '{serviceName}' is unavailable."), cancellationToken);
			else if (isAvailable && previous == 0)
				await ReissueAllAsync(GetActiveConnection(), cancellationToken);
		}
	}

	private async ValueTask ProcessConnectionClosedAsync(int slot, Exception error)
	{
		if (Interlocked.CompareExchange(ref _isStopping, 0, 0) != 0 || Interlocked.CompareExchange(ref _isReady, 0, 0) == 0)
			return;

		var activeConnection = TryFindOpenConnection(slot);
		if (slot == _activeSlot)
		{
			if (activeConnection == null)
			{
				FailSnapshots(error);
				await RaiseConnectionLostAsync(error);
				return;
			}

			_activeSlot = activeConnection.Slot;
			foreach (var subscription in _subscriptions.Values)
				subscription.LastSequence = 0;
			await ReissueAllAsync(activeConnection, CancellationToken.None);
			await ReissueSnapshotsAsync(activeConnection, CancellationToken.None);
		}
		else
			await RaiseErrorAsync(error, CancellationToken.None);

		if (_configuration.IsHotStandby && activeConnection != null)
			_ = ReconnectSlotAsync(slot, _lifetime.Token);
	}

	private async Task ReconnectSlotAsync(int slot, CancellationToken cancellationToken)
	{
		if (!_reconnectingSlots.TryAdd(slot, 0))
			return;

		Exception lastError = null;
		try
		{
			for (var attempt = 1; attempt <= 3 && !cancellationToken.IsCancellationRequested; attempt++)
			{
				LsegWebSocketConnection replacement = null;
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(5 * attempt), cancellationToken);
					var old = GetConnection(slot);
					replacement = CreateConnection(slot, old.Address);
					await replacement.ConnectAsync(CreateLoginRequest(false), cancellationToken);
					lock (_connectionsSync)
						_connections[slot] = replacement;
					old.Dispose();
					await replacement.SendAsync(new LsegSourceRequest(), cancellationToken);
					await ReissueAllAsync(replacement, cancellationToken);
					replacement = null;
					return;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception error)
				{
					lastError = error;
				}
				finally
				{
					replacement?.Dispose();
				}
			}

			if (lastError != null)
				await RaiseErrorAsync(lastError, CancellationToken.None);
		}
		finally
		{
			_reconnectingSlots.TryRemove(slot, out _);
		}
	}

	private async Task RecoverSubscriptionAsync(LsegSubscription subscription, CancellationToken cancellationToken)
	{
		if (Interlocked.Exchange(ref subscription.IsRecovering, 1) != 0)
			return;
		try
		{
			subscription.LastSequence = 0;
			await GetActiveConnection().SendAsync(CreateItemRequest(subscription, true), cancellationToken);
		}
		catch
		{
			Interlocked.Exchange(ref subscription.IsRecovering, 0);
			throw;
		}
	}

	private async Task ReissueAllAsync(LsegWebSocketConnection connection, CancellationToken cancellationToken)
	{
		var requests = _subscriptions.Values
			.OrderBy(subscription => subscription.StreamId)
			.Select(subscription => CreateItemRequest(subscription, true))
			.ToArray();
		if (requests.Length > 0)
			await connection.SendAsync(requests, cancellationToken);
	}

	private async Task ReissueSnapshotsAsync(LsegWebSocketConnection connection, CancellationToken cancellationToken)
	{
		var requests = _snapshots
			.OrderBy(pair => pair.Key)
			.Select(pair => CreateSnapshotRequest(pair.Key, pair.Value.Ric))
			.ToArray();
		if (requests.Length > 0)
			await connection.SendAsync(requests, cancellationToken);
	}

	private async Task RenewTokenLoopAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			var delaySeconds = Math.Max(30, (int)(_expiresIn * 0.8));
			await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
			try
			{
				LsegTokenResponse token;
				try
				{
					token = await RequestTokenAsync(true, cancellationToken);
				}
				catch when (_configuration.AuthenticationMode == LsegAuthenticationModes.PasswordGrant && !_refreshToken.IsEmpty())
				{
					_refreshToken = null;
					token = await RequestTokenAsync(false, cancellationToken);
				}
				ApplyToken(token);
				var login = CreateLoginRequest(true);
				await Task.WhenAll(GetOpenConnections().Select(connection => connection.SendAsync(login, cancellationToken)));
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				await RaiseErrorAsync(error, CancellationToken.None);
				_expiresIn = 38;
			}
		}
	}

	private async Task<LsegTokenResponse> RequestTokenAsync(bool isRenewal, CancellationToken cancellationToken)
	{
		var isPasswordGrant = _configuration.AuthenticationMode == LsegAuthenticationModes.PasswordGrant;
		var authUrl = _configuration.AuthUrl.IsEmpty(isPasswordGrant
			? "https://api.refinitiv.com/auth/oauth2/v1/token"
			: "https://api.refinitiv.com/auth/oauth2/v2/token");
		var request = isPasswordGrant
			? new LsegTokenRequest
			{
				GrantType = isRenewal && !_refreshToken.IsEmpty() ? "refresh_token" : "password",
				UserName = _configuration.Login,
				Password = isRenewal && !_refreshToken.IsEmpty() ? null : _configuration.Password.UnSecure(),
				ClientId = _configuration.ClientId,
				Scope = isRenewal && !_refreshToken.IsEmpty() ? null : _configuration.Scope,
				RefreshToken = isRenewal ? _refreshToken : null,
				IsExclusiveSignOn = !isRenewal || _refreshToken.IsEmpty(),
			}
			: new LsegTokenRequest
			{
				GrantType = "client_credentials",
				ClientId = _configuration.ClientId,
				ClientSecret = _configuration.Secret.UnSecure(),
				Scope = _configuration.Scope,
			};

		var currentUrl = new Uri(authUrl);
		for (var redirect = 0; redirect < 5; redirect++)
		{
			using var content = new StringContent(request.ToFormBody(), Encoding.ASCII, "application/x-www-form-urlencoded");
			using var httpRequest = new HttpRequestMessage(HttpMethod.Post, currentUrl) { Content = content };
			httpRequest.Headers.UserAgent.ParseAdd("StockSharp-LsegRealTime");
			using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (IsRedirect(response.StatusCode) && response.Headers.Location != null)
			{
				currentUrl = response.Headers.Location.IsAbsoluteUri
					? response.Headers.Location
					: new Uri(currentUrl, response.Headers.Location);
				continue;
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			var token = JsonConvert.DeserializeObject<LsegTokenResponse>(body);
			if (!response.IsSuccessStatusCode || token == null || token.AccessToken.IsEmpty())
				throw new HttpRequestException(token?.ErrorDescription.IsEmpty(token?.Error).IsEmpty($"LSEG OAuth returned HTTP {(int)response.StatusCode}."), null, response.StatusCode);
			return token;
		}

		throw new HttpRequestException("LSEG OAuth returned too many redirects.");
	}

	private async Task<Uri[]> ResolveAddressesAsync(CancellationToken cancellationToken)
	{
		var addresses = new List<Uri>();
		if (!_configuration.Address.IsEmpty())
			addresses.Add(NormalizeAddress(_configuration.Address));
		if (_configuration.IsHotStandby && !_configuration.StandbyAddress.IsEmpty())
			addresses.Add(NormalizeAddress(_configuration.StandbyAddress));

		if (addresses.Count == 0)
		{
			if (_configuration.AuthenticationMode == LsegAuthenticationModes.Deployed)
				addresses.Add(new Uri("ws://localhost:15000/WebSocket"));
			else
				addresses.AddRange((await DiscoverServicesAsync(cancellationToken)).Select(endpoint => endpoint.ToWebSocketUri()));
		}

		addresses = addresses.Distinct().ToList();
		if (_configuration.IsHotStandby && addresses.Count < 2)
			throw new InvalidOperationException("LSEG hot standby requires two distinct WebSocket endpoints.");
		return [.. addresses.Take(_configuration.IsHotStandby ? 2 : 1)];
	}

	private async Task<LsegEndpoint[]> DiscoverServicesAsync(CancellationToken cancellationToken)
	{
		var baseUrl = _configuration.DiscoveryUrl.IsEmpty("https://api.refinitiv.com/streaming/pricing/v1/");
		var separator = baseUrl.Contains('?') ? '&' : '?';
		var currentUrl = new Uri($"{baseUrl}{separator}transport=websocket");
		for (var redirect = 0; redirect < 5; redirect++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
			request.Headers.UserAgent.ParseAdd("StockSharp-LsegRealTime");
			using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (IsRedirect(response.StatusCode) && response.Headers.Location != null)
			{
				currentUrl = response.Headers.Location.IsAbsoluteUri
					? response.Headers.Location
					: new Uri(currentUrl, response.Headers.Location);
				continue;
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException($"LSEG service discovery returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
			var discovery = JsonConvert.DeserializeObject<LsegServiceDiscoveryResponse>(body)
				?? throw new InvalidOperationException("LSEG service discovery returned an empty response.");
			var matches = discovery.Services
				.Where(service => service.Endpoint.IsEmpty() == false && service.Port > 0 &&
					(service.Locations?.Any(location => location.StartsWithIgnoreCase(_configuration.Region)) ?? false))
				.ToArray();
			if (_configuration.IsHotStandby)
				matches = matches.Where(service => service.Locations.Length == 1).ToArray();
			else
			{
				var loadBalanced = matches.Where(service => service.Locations.Length >= 2).ToArray();
				if (loadBalanced.Length > 0)
					matches = loadBalanced;
			}
			if (matches.Length == 0)
				throw new InvalidOperationException($"LSEG service discovery returned no WebSocket endpoints for region '{_configuration.Region}'.");
			return matches
				.Select(service => new LsegEndpoint { Host = service.Endpoint, Port = service.Port, Locations = service.Locations ?? [] })
				.ToArray();
		}

		throw new HttpRequestException("LSEG service discovery returned too many redirects.");
	}

	private LsegLoginRequest CreateLoginRequest(bool isReissue)
	{
		var isDeployed = _configuration.AuthenticationMode == LsegAuthenticationModes.Deployed;
		return new LsegLoginRequest
		{
			Refresh = isReissue ? false : null,
			Key = new LsegLoginKey
			{
				Name = isDeployed ? _configuration.Login : null,
				NameType = isDeployed ? null : "AuthnToken",
				Elements = new LsegLoginElements
				{
					ApplicationId = _configuration.ApplicationId,
					Position = GetPosition(),
					AuthenticationToken = isDeployed ? null : _accessToken,
				},
			},
		};
	}

	private LsegItemRequest CreateItemRequest(LsegSubscription subscription, bool isStreaming)
		=> new()
		{
			Id = subscription.StreamId,
			Domain = subscription.Kind == LsegSubscriptionKinds.MarketByPrice ? "MarketByPrice" : "MarketPrice",
			Key = new LsegItemKey { Name = subscription.Ric, Service = _configuration.Service },
			Streaming = isStreaming,
			View = subscription.Kind == LsegSubscriptionKinds.MarketByPrice ? _marketByPriceView : _marketPriceView,
		};

	private LsegItemRequest CreateSnapshotRequest(long streamId, string ric)
		=> new()
		{
			Id = streamId,
			Domain = "MarketPrice",
			Key = new LsegItemKey { Name = ric, Service = _configuration.Service },
			Streaming = false,
			View = _marketPriceView,
		};

	private void ApplyToken(LsegTokenResponse token)
	{
		_accessToken = token.AccessToken;
		if (!token.RefreshToken.IsEmpty())
			_refreshToken = token.RefreshToken;
		_expiresIn = token.ExpiresIn > 0 ? token.ExpiresIn : 300;
	}

	private void ValidateConfiguration()
	{
		_configuration.ApplicationId.ThrowIfEmpty(nameof(_configuration.ApplicationId));
		_configuration.Service.ThrowIfEmpty(nameof(_configuration.Service));
		if (_configuration.AuthenticationMode == LsegAuthenticationModes.Deployed)
			_configuration.Login.ThrowIfEmpty(nameof(_configuration.Login));
		else
		{
			_configuration.ClientId.ThrowIfEmpty(nameof(_configuration.ClientId));
			_configuration.Scope.ThrowIfEmpty(nameof(_configuration.Scope));
			if (_configuration.AuthenticationMode == LsegAuthenticationModes.PasswordGrant)
			{
				_configuration.Login.ThrowIfEmpty(nameof(_configuration.Login));
				if (_configuration.Password.IsEmpty())
					throw new InvalidOperationException("LSEG password is not specified.");
			}
			else if (_configuration.Secret.IsEmpty())
				throw new InvalidOperationException("LSEG client secret is not specified.");
		}
	}

	private string GetPosition()
	{
		if (!_configuration.Position.IsEmpty())
			return _configuration.Position;
		try
		{
			var address = Dns.GetHostEntry(Dns.GetHostName()).AddressList
				.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
			return address?.ToString() ?? "127.0.0.1/net";
		}
		catch
		{
			return "127.0.0.1/net";
		}
	}

	private Uri NormalizeAddress(string address)
	{
		if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
		{
			var scheme = _configuration.AuthenticationMode == LsegAuthenticationModes.Deployed ? "ws" : "wss";
			uri = new Uri($"{scheme}://{address}");
		}
		if (uri.Scheme is not ("ws" or "wss"))
			throw new ArgumentOutOfRangeException(nameof(address), address, "LSEG address must use ws or wss.");
		if (uri.AbsolutePath is "/" or "")
			uri = new UriBuilder(uri) { Path = "/WebSocket" }.Uri;
		return uri;
	}

	private LsegWebSocketConnection[] GetOpenConnections()
	{
		lock (_connectionsSync)
			return _connections.Where(connection => connection.IsOpen).ToArray();
	}

	private LsegWebSocketConnection GetConnection(int slot)
	{
		lock (_connectionsSync)
			return _connections[slot];
	}

	private LsegWebSocketConnection GetActiveConnection()
		=> TryGetActiveConnection() ?? throw new InvalidOperationException("No LSEG WebSocket connection is open.");

	private LsegWebSocketConnection TryGetActiveConnection()
	{
		lock (_connectionsSync)
			return _activeSlot < _connections.Length && _connections[_activeSlot].IsOpen ? _connections[_activeSlot] : null;
	}

	private LsegWebSocketConnection TryFindOpenConnection(int excludedSlot)
	{
		lock (_connectionsSync)
			return _connections.FirstOrDefault(connection => connection.Slot != excludedSlot && connection.IsOpen);
	}

	private void EnsureConnected()
	{
		if (Interlocked.CompareExchange(ref _isReady, 0, 0) == 0 || Interlocked.CompareExchange(ref _isStopping, 0, 0) != 0)
			throw new InvalidOperationException("The LSEG Real-Time client is not connected.");
	}

	private void FailSnapshots(Exception error)
	{
		foreach (var snapshot in _snapshots.Values)
			snapshot.Completion.TrySetException(error);
	}

	private static bool CanRecover(LsegWireState state)
		=> state?.Stream.EqualsIgnoreCase("ClosedRecover") == true || state?.Data.EqualsIgnoreCase("Suspect") == true;

	private static bool IsRedirect(HttpStatusCode statusCode)
		=> statusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
			HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

	private static Exception CreateWireError(LsegWireMessage message, string subject)
	{
		var detail = message.State?.Text.IsEmpty(message.Text)
			.IsEmpty(message.State?.Code)
			.IsEmpty("Unknown protocol error.");
		return new InvalidOperationException($"LSEG {subject}: {detail}");
	}

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error?.Invoke(error, cancellationToken) ?? default;

	private ValueTask RaiseConnectionLostAsync(Exception error)
		=> ConnectionLost?.Invoke(error, CancellationToken.None) ?? default;
}
