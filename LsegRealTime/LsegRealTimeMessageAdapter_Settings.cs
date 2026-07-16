namespace StockSharp.LsegRealTime;

using System.ComponentModel.DataAnnotations;

/// <summary>The message adapter for the LSEG Real-Time WebSocket API.</summary>
[MediaIcon(Media.MediaNames.lsegrealtime)]
[Doc("topics/api/connectors/stock_market/lseg_real_time.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.LsegRealTimeKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options | MessageAdapterCategories.FX | MessageAdapterCategories.Commodities)]
public partial class LsegRealTimeMessageAdapter : MessageAdapter, IAddressAdapter<string>, ILoginPasswordAdapter
{
	private string _address = string.Empty;
	private string _standbyAddress = string.Empty;
	private string _applicationId = "256";
	private string _service = "ELEKTRON_DD";
	private string _region = "us-east-1";
	private string _position = string.Empty;
	private string _scope = "trapi.streaming.pricing.read";
	private string _authUrl = string.Empty;
	private string _discoveryUrl = "https://api.refinitiv.com/streaming/pricing/v1/";

	/// <summary>Authentication flow used by the LSEG environment.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LsegAuthenticationModeKey,
		Description = LocalizedStrings.LsegAuthenticationModeDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public LsegAuthenticationModes AuthenticationMode { get; set; }

	/// <summary>Primary WebSocket endpoint. Empty uses the environment default or service discovery.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.LsegAddressDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public string Address
	{
		get => _address;
		set => _address = value ?? string.Empty;
	}

	/// <summary>Secondary WebSocket endpoint for hot standby.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BackupServerKey,
		Description = LocalizedStrings.LsegStandbyAddressDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	public string StandbyAddress
	{
		get => _standbyAddress;
		set => _standbyAddress = value ?? string.Empty;
	}

	/// <summary>Connect to two LSEG hot-standby endpoints and fail over between them.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LsegHotStandbyKey,
		Description = LocalizedStrings.LsegHotStandbyDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public bool IsHotStandby { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LsegLoginDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.LsegPasswordDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>LSEG OAuth client identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ClientIdKey,
		Description = LocalizedStrings.LsegClientIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	[BasicSetting]
	public string ClientId { get; set; }

	/// <summary>LSEG OAuth v2 client secret.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.LsegSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>LSEG application identifier sent in Login messages.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LsegApplicationIdKey,
		Description = LocalizedStrings.LsegApplicationIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 8)]
	public string ApplicationId
	{
		get => _applicationId;
		set => _applicationId = value.ThrowIfEmpty(nameof(value));
	}

	/// <summary>LSEG source-directory service name.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ServiceKey,
		Description = LocalizedStrings.LsegServiceDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 9)]
	public string Service
	{
		get => _service;
		set => _service = value.ThrowIfEmpty(nameof(value));
	}

	/// <summary>Preferred LSEG service-discovery region.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.RegionKey,
		Description = LocalizedStrings.LsegRegionDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 10)]
	public string Region
	{
		get => _region;
		set => _region = value.ThrowIfEmpty(nameof(value));
	}

	/// <summary>DACS position. Empty derives the local IPv4 address.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PositionKey,
		Description = LocalizedStrings.LsegPositionDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 11)]
	public string Position
	{
		get => _position;
		set => _position = value ?? string.Empty;
	}

	/// <summary>LSEG OAuth scope.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LsegScopeKey,
		Description = LocalizedStrings.LsegScopeDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 12)]
	public string Scope
	{
		get => _scope;
		set => _scope = value.ThrowIfEmpty(nameof(value));
	}

	/// <summary>Optional OAuth endpoint override.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LsegAuthUrlKey,
		Description = LocalizedStrings.LsegAuthUrlDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 13)]
	public string AuthUrl
	{
		get => _authUrl;
		set => _authUrl = value ?? string.Empty;
	}

	/// <summary>LSEG WebSocket service-discovery endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LsegDiscoveryUrlKey,
		Description = LocalizedStrings.LsegDiscoveryUrlDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 14)]
	public string DiscoveryUrl
	{
		get => _discoveryUrl;
		set => _discoveryUrl = value.ThrowIfEmpty(nameof(value));
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(AuthenticationMode), AuthenticationMode)
			.Set(nameof(Address), Address)
			.Set(nameof(StandbyAddress), StandbyAddress)
			.Set(nameof(IsHotStandby), IsHotStandby)
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(ClientId), ClientId)
			.Set(nameof(Secret), Secret)
			.Set(nameof(ApplicationId), ApplicationId)
			.Set(nameof(Service), Service)
			.Set(nameof(Region), Region)
			.Set(nameof(Position), Position)
			.Set(nameof(Scope), Scope)
			.Set(nameof(AuthUrl), AuthUrl)
			.Set(nameof(DiscoveryUrl), DiscoveryUrl);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		AuthenticationMode = storage.GetValue(nameof(AuthenticationMode), AuthenticationMode);
		Address = storage.GetValue(nameof(Address), Address);
		StandbyAddress = storage.GetValue(nameof(StandbyAddress), StandbyAddress);
		IsHotStandby = storage.GetValue(nameof(IsHotStandby), IsHotStandby);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		ClientId = storage.GetValue<string>(nameof(ClientId));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		ApplicationId = storage.GetValue(nameof(ApplicationId), ApplicationId);
		Service = storage.GetValue(nameof(Service), Service);
		Region = storage.GetValue(nameof(Region), Region);
		Position = storage.GetValue(nameof(Position), Position);
		Scope = storage.GetValue(nameof(Scope), Scope);
		AuthUrl = storage.GetValue(nameof(AuthUrl), AuthUrl);
		DiscoveryUrl = storage.GetValue(nameof(DiscoveryUrl), DiscoveryUrl);
	}
}
