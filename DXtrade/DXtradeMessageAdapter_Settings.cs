namespace StockSharp.DXtrade;

using System.ComponentModel.DataAnnotations;

using Ecng.ComponentModel;

/// <summary>
/// The message adapter for <see cref="DXtrade"/>.
/// </summary>
[MediaIcon(Media.MediaNames.devexperts)]
[Doc("topics/api/connectors/forex/dxtrade.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DXTradeKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[MessageAdapterCategory(MessageAdapterCategories.FX | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
public partial class DXtradeMessageAdapter : MessageAdapter, ILoginPasswordAdapter, IDemoAdapter, IAddressAdapter<string>
{
	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => Native.Extensions.TimeFrames.Keys.ToArray();

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.PasswordKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Password { get; set; }

	private string _domainAddress;

	/// <summary>
	/// Domain address.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DomainAddressKey,
		Description = LocalizedStrings.DomainAddressDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string DomainAddress
	{
		get => _domainAddress;
		set
		{
			_domainAddress = value;
			OnPropertyChanged(nameof(DomainAddress));
		}
	}

	private bool _isDemo;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public bool IsDemo
	{
		get => _isDemo;
		set
		{
			_isDemo = value;
			Address = IsDemo ? "demo.dx.trade" : "api.dx.trade";
		}
	}

	private string _address = "api.dx.trade";

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public string Address
	{
		get => _address;
		set
		{
			_address = value;
			OnPropertyChanged(nameof(Address));
		}
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(DomainAddress), DomainAddress)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(Address), Address)
		;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		DomainAddress = storage.GetValue<string>(nameof(DomainAddress));
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		Address = storage.GetValue<string>(nameof(Address));
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return $"{base.ToString()}: {LocalizedStrings.Login} = {Login}";
	}
}