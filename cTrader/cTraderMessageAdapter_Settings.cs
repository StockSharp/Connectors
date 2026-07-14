namespace StockSharp.cTrader;

using System.ComponentModel.DataAnnotations;
using System.Net;

using Ecng.ComponentModel;

/// <summary>
/// The message adapter for <see cref="cTrader"/>.
/// </summary>
[MediaIcon(Media.MediaNames.ctrader)]
[Doc("topics/api/connectors/forex/ctrader.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.cTraderKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[MessageAdapterCategory(MessageAdapterCategories.FX | MessageAdapterCategories.Transactions | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Free)]
[OrderCondition(typeof(cTraderOrderCondition))]
public partial class cTraderMessageAdapter : MessageAdapter, IDemoAdapter, IAddressAdapter<EndPoint>
{
	private const string _liveHost = "live.ctraderapi.com";
	private const string _demoHost = "demo.ctraderapi.com";
	private const int _port = 5035;

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => Native.Extensions.TimeFrames.Keys.ToArray();

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
			if (value == _isDemo)
				return;

			_isDemo = value;
			Address = new DnsEndPoint(value ? _demoHost : _liveHost, _port);
			OnPropertyChanged(nameof(Address));
		}
	}

	private EndPoint _address = new DnsEndPoint(_liveHost, _port);

	/// <summary>
	/// Address.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public EndPoint Address
	{
		get => _address;
		set => _address = value ?? throw new ArgumentNullException(nameof(value));
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(Address), Address.To<string>())
			;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		Address = storage.GetValue<EndPoint>(nameof(Address));
	}
}