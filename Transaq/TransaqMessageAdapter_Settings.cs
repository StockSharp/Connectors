namespace StockSharp.Transaq;

using System.ComponentModel;
using System.IO;
using System.Reflection;

/// <summary>
/// Адаптер сообщений для Transaq.
/// </summary>
[MediaIcon(Media.MediaNames.transaq)]
[Doc("topics/api/connectors/russia/transaq.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TransaqKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.RussiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Russia | MessageAdapterCategories.Transactions | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.News)]
[OrderCondition(typeof(TransaqOrderCondition))]
partial class TransaqMessageAdapter : MessageAdapter, ILoginPasswordAdapter, IAddressAdapter<EndPoint>, IDemoAdapter
{
	private class TransaqAddressSource : ItemsSourceBase<EndPoint>
	{
		private static readonly EndPoint[] _values = typeof(TransaqAddresses).GetFields(BindingFlags.Static | BindingFlags.Public).Select(f => (EndPoint)f.GetValue(null)).ToArray();

		protected override IEnumerable<EndPoint> GetValues() => _values;

		protected override string GetName(EndPoint value) => value.To<string>();
	}

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

	private EndPoint _address = TransaqAddresses.FinamReal1;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[ItemsSource(typeof(TransaqAddressSource))]
	[BasicSetting]
	public EndPoint Address
	{
		get => _address;
		set
		{
			_address = value ?? throw new ArgumentNullException(nameof(value));
			OnPropertyChanged(nameof(IsDemo));
		}
	}

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
		get => Address?.GetHost().ContainsIgnoreCase("demo") == true;
		set
		{
			Address = value ? TransaqAddresses.FinamDemo : TransaqAddresses.FinamReal1;
			OnPropertyChanged(nameof(Address));
		}
	}

	/// <summary>
	/// Прокси.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ProxyKey,
		Description = LocalizedStrings.ProxyUsedKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public Proxy Proxy { get; } = new Proxy();

	/// <summary>
	/// Уровень логирования коннектора. По умолчанию <see cref="ApiLogLevels.Standard"/>.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LogLevelKey,
		Description = LocalizedStrings.ConnectionLogLevelKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 2)]
	public ApiLogLevels ApiLogLevel { get; set; } = ApiLogLevels.Standard;

	/// <summary>
	/// Полный путь к dll файлу, содержащее Transaq API.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PathDllKey,
		Description = LocalizedStrings.PathDllDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 1)]
	[Editor(typeof(IFileBrowserEditor), typeof(IFileBrowserEditor))]
	public string DllPath { get; set; }

	///// <summary>
	///// Показать окно смены пароля при соединении.
	///// </summary>
	//[Category("Общее")]
	//[DisplayName("Сменить пароль")]
	//[Description("Показать окно смены пароля при соединении.")]
	//[PropertyOrder(4)]
	//public bool ShowChangePasswordWindowOnConnect { get; set; }

	/// <summary>
	/// Передавать ли данные для фондового рынка.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StockDataKey,
		Description = LocalizedStrings.StockDataDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 3)]
	public bool MicexRegisters { get; set; } = true;

	/// <summary>
	/// Подключаться ли к HFT серверу Финам.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.HFTKey,
		Description = LocalizedStrings.HFTFinamConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public bool IsHFT { get; set; }

	/// <summary>
	/// Период агрегирования данных на сервере Transaq.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AggPeriodKey,
		Description = LocalizedStrings.AggPeriodDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 4)]
	public TimeSpan? MarketDataInterval { get; set; }

	/// <summary>
	/// Путь к логам коннектора.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PathLogsKey,
		Description = LocalizedStrings.PathLogsDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 7)]
	[Editor(typeof(IFolderBrowserEditor), typeof(IFolderBrowserEditor))]
	public string ApiLogsPath { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "StockSharp.Transaq", "Logs");

	/// <summary>
	/// Версия коннектора.
	/// </summary>
	[Browsable(false)]
	public string ConnectorVersion { get; set; }

	/// <summary>
	/// Текущий сервер.
	/// </summary>
	[Browsable(false)]
	public int CurrentServer { get; set; }

	/// <summary>
	/// Разница между локальным и серверным временем.
	/// </summary>
	[Browsable(false)]
	public TimeSpan? ServerTimeDiff { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		storage.SetValue(nameof(Login), Login);
		storage.SetValue(nameof(Password), Password);
		storage.SetValue(nameof(Address), Address.To<string>());
		storage.SetValue(nameof(DllPath), DllPath);
		storage.SetValue(nameof(ApiLogsPath), ApiLogsPath);
		storage.SetValue(nameof(ApiLogLevel), ApiLogLevel.To<string>());
		storage.SetValue(nameof(MarketDataInterval), MarketDataInterval);
		storage.SetValue(nameof(Proxy), Proxy.Save());
		storage.SetValue(nameof(IsHFT), IsHFT);

		// не нужно сохранять это свойство, так как иначе окно смены пароля будет показывать каждый раз
		//storage.SetValue("ChangePasswordOnConnect", ShowChangePasswordWindowOnConnect);

		storage.SetValue(nameof(MicexRegisters), MicexRegisters);

		base.Save(storage);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		Address = storage.GetValue<EndPoint>(nameof(Address));
		DllPath = storage.GetValue(nameof(DllPath), DllPath);
		ApiLogsPath = storage.GetValue<string>(nameof(ApiLogsPath));
		ApiLogLevel = storage.GetValue<ApiLogLevels>(nameof(ApiLogLevel));
		MarketDataInterval = storage.GetValue<TimeSpan?>(nameof(MarketDataInterval));
		IsHFT = storage.GetValue<bool>(nameof(IsHFT));

		var proxy = storage.GetValue<SettingsStorage>(nameof(Proxy));
		if (proxy != null)
			((IPersistable)Proxy).Load(proxy);

		//ShowChangePasswordWindowOnConnect = storage.GetValue<bool>("ChangePasswordOnConnect");

		MicexRegisters = storage.GetValue(nameof(MicexRegisters), true);

		base.Load(storage);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + $": Login={Login} Addr={Address.To<string>()}";
	}
}