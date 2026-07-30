namespace StockSharp.Fix;

using System.ComponentModel;
using System.Net;
using System.Security;
using System.Security.Authentication;

/// <summary>
/// FIX message adapter.
/// </summary>
[MediaIcon(Media.MediaNames.fix)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FIXKey,
	Description = LocalizedStrings.FixConnectorKey)]
[Doc("topics/api/connectors/common/fix_protocol.html")]
[MessageAdapterCategory(MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.Paid | MessageAdapterCategories.Ticks | MessageAdapterCategories.OrderLog |
	MessageAdapterCategories.FX | MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles | MessageAdapterCategories.Transactions)]
partial class FixMessageAdapter : ILoginPasswordAdapter, IAddressAdapter<EndPoint>, ISenderTargetAdapter, IDemoAdapter
{
	/// <summary>
	/// The FIX dialect.
	/// </summary>
	[Browsable(false)]
	public IFixDialect FixDialect { get; private set; }

	private EndPoint _address = new IPEndPoint(IPAddress.Loopback, 5001);

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.SessionKey,
		Order = 0)]
	[BasicSetting]
	public EndPoint Address
	{
		get => _address;
		set => _address = value;
	}

	private static readonly HashSet<MessageTypes> _extended = new(
	[
		MessageTypes.UserInfo,
		MessageTypes.UserLookup,
		MessageTypes.UserRequest,

		MessageTypes.EmulationState,
		MessageTypes.SecurityLegsRequest,
		MessageTypes.Command,

		MessageTypes.SubscriptionListRequest,
		MessageTypes.SecurityMapping,

		MessageTypes.Security,
		MessageTypes.Remove,

		MessageTypes.RemoteFileCommand,
		MessageTypes.DataTypeLookup,
	]);

	private Type _dialect;

	/// <summary>
	/// The FIX dialect. The default is <see cref="DefaultFixDialect"/>.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DialectKey,
		Description = LocalizedStrings.FixDialectProtocolKey,
		GroupName = LocalizedStrings.SessionKey,
		Order = 1)]
	[ItemsSource(typeof(DialectSource<IFixDialect, FixMessageAdapter>))]
	[BasicSetting]
	public Type Dialect
	{
		get => _dialect;
		set
		{
			if (value == null)
				throw new ArgumentNullException(nameof(value));

			FixDialect?.Dispose();

			FixDialect = value.CreateInstance<IFixDialect>(TransactionIdGenerator);
			FixDialect.Parent = this;

			_dialect = value;

			OnPropertyChanged(nameof(SenderCompId));
			OnPropertyChanged(nameof(TargetCompId));
			OnPropertyChanged(nameof(Login));
			OnPropertyChanged(nameof(Password));
			OnPropertyChanged(nameof(IsResetCounter));
			OnPropertyChanged(nameof(DateFormat));
			OnPropertyChanged(nameof(TimeStampFormat));
			OnPropertyChanged(nameof(TimeFormat));
			OnPropertyChanged(nameof(YearMonthFormat));
			OnPropertyChanged(nameof(SupportUnknownExecutions));
			OnPropertyChanged(nameof(Encoding));
			OnPropertyChanged(nameof(ExchangeBoard));
			OnPropertyChanged(nameof(ClientCode));

			// to update SupportedInMessages
			PossibleSupportedMessages = PossibleSupportedMessages;

			if (FixDialect is DefaultFixDialect)
				SupportedInMessages = [.. SupportedInMessages.Where(m => !_extended.Contains(m))];

			OnPropertyChanged(nameof(Icon));
			OnPropertyChanged(nameof(Name));
			OnPropertyChanged(nameof(Categories));
			OnPropertyChanged(nameof(FeatureName));
		}
	}

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SenderKey,
		Description = LocalizedStrings.SenderCompIdKey,
		GroupName = LocalizedStrings.SessionKey,
		Order = 2)]
	[BasicSetting]
	public string SenderCompId
	{
		get => FixDialect.SenderCompId;
		set => FixDialect.SenderCompId = value;
	}

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TargetKey,
		Description = LocalizedStrings.TargetCompIdKey,
		GroupName = LocalizedStrings.SessionKey,
		Order = 3)]
	[BasicSetting]
	public string TargetCompId
	{
		get => FixDialect.TargetCompId;
		set => FixDialect.TargetCompId = value;
	}

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.SessionKey,
		Order = 4)]
	[BasicSetting]
	public string Login
	{
		get => FixDialect.Login;
		set => FixDialect.Login = value;
	}

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.PasswordKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.SessionKey,
		Order = 5)]
	[BasicSetting]
	public SecureString Password
	{
		get => FixDialect.Password;
		set => FixDialect.Password = value;
	}

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.SessionKey,
		Order = 8)]
	[BasicSetting]
	public bool IsDemo
	{
		get => FixDialect.IsDemo;
		set => FixDialect.IsDemo = value;
	}

	/// <summary>
	/// The encoding used for data transmission.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.EncodingKey,
		Description = LocalizedStrings.EncodingDescKey,
		GroupName = LocalizedStrings.SessionKey,
		Order = 11)]
	public Encoding Encoding
	{
		get => FixDialect.Encoding;
		set => FixDialect.Encoding = value;
	}

	/// <summary>
	/// Should the sequence counter be reset.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CounterKey,
		Description = LocalizedStrings.ResetCounterKey,
		GroupName = LocalizedStrings.SessionKey,
		Order = 12)]
	public bool IsResetCounter
	{
		get => FixDialect.IsResetCounter;
		set
		{
			FixDialect.IsResetCounter = value;
			OnPropertyChanged(nameof(PossibleSupportedMessages));
		}
	}

	/// <summary>
	/// Date format.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DatesFormatKey,
		Description = LocalizedStrings.DatesFormatKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.SessionKey,
		Order = 16)]
	public string DateFormat
	{
		get => FixDialect.DateParser.Template;
		set => FixDialect.DateParser = new(value);
	}

	/// <summary>
	/// Timestamp format.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DateTimeFormatKey,
		Description = LocalizedStrings.DateTimeFormatKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.SessionKey,
		Order = 17)]
	public string TimeStampFormat
	{
		get => FixDialect.TimeStampParser.Template;
		set => FixDialect.TimeStampParser = new(value);
	}

	/// <summary>
	/// Time format.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeFormatKey,
		Description = LocalizedStrings.TimeFormatKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.SessionKey,
		Order = 18)]
	public string TimeFormat
	{
		get => FixDialect.TimeParser.Template;
		set => FixDialect.TimeParser = new(value);
	}

	/// <summary>
	/// Year month format.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.YearMonthKey,
		Description = LocalizedStrings.YearMonthFormatKey,
		GroupName = LocalizedStrings.SessionKey,
		Order = 19)]
	public string YearMonthFormat
	{
		get => FixDialect.YearMonthParser.Template;
		set => FixDialect.YearMonthParser = new(value);
	}

	private TimeSpan _readTimeout;

	/// <summary>
	/// The timeout of reading data. The default value is <see cref="TimeSpan.Zero"/>.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ReadTimeOutKey,
		Description = LocalizedStrings.ReadTimeOutDescKey,
		GroupName = LocalizedStrings.SessionKey,
		Order = 20)]
	public TimeSpan ReadTimeout
	{
		get => _readTimeout;
		set
		{
			if (value < TimeSpan.Zero)
				throw new ArgumentOutOfRangeException();

			_readTimeout = value;
		}
	}

	private TimeSpan _writeTimeout;

	/// <summary>
	/// The timeout of sending data. The default value is <see cref="TimeSpan.Zero"/>.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WriteTimeOutKey,
		Description = LocalizedStrings.WriteTimeOutDescKey,
		GroupName = LocalizedStrings.SessionKey,
		Order = 21)]
	public TimeSpan WriteTimeout
	{
		get => _writeTimeout;
		set
		{
			if (value < TimeSpan.Zero)
				throw new ArgumentOutOfRangeException();

			_writeTimeout = value;
		}
	}

	/// <summary>
	/// Board, where securities are traded.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BoardKey,
		Description = LocalizedStrings.FixConnectorBoardKey,
		GroupName = LocalizedStrings.SessionKey,
		Order = 22)]
	public string ExchangeBoard
	{
		get => FixDialect.ExchangeBoard;
		set => FixDialect.ExchangeBoard = value;
	}

	/// <summary>
	/// Client code assigned by the broker.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ClientCodeKey,
		Description = LocalizedStrings.ClientCodeDescKey,
		GroupName = LocalizedStrings.SessionKey,
		Order = 23)]
	public string ClientCode
	{
		get => FixDialect.ClientCode;
		set => FixDialect.ClientCode = value;
	}

	/// <summary>
	/// SSL protocol to establish connect.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ProtocolKey,
		Description = LocalizedStrings.SslProtocolKey,
		GroupName = LocalizedStrings.SslKey,
		Order = 201)]
	public SslProtocols SslProtocol { get; set; }

	/// <summary>
	/// SSL certificate.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CertificateKey,
		Description = LocalizedStrings.SslCertificateKey,
		GroupName = LocalizedStrings.SslKey,
		Order = 202)]
	[Editor(typeof(IFileBrowserEditor), typeof(IFileBrowserEditor))]
	public string SslCertificate { get; set; }

	/// <summary>
	/// SSL certificate password.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.SslCertificatePasswordKey,
		GroupName = LocalizedStrings.SslKey,
		Order = 203)]
	public SecureString SslCertificatePassword { get; set; }

	/// <summary>
	/// Check certificate revocation.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CheckCertificateRevocationKey,
		Description = LocalizedStrings.CheckCertificateRevocationDescKey,
		GroupName = LocalizedStrings.SslKey,
		Order = 204)]
	public bool CheckCertificateRevocation { get; set; }

	/// <summary>
	/// Validate remove certificates.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ValidateRemoteCertificatesKey,
		Description = LocalizedStrings.ValidateRemoteCertificatesDescKey,
		GroupName = LocalizedStrings.SslKey,
		Order = 205)]
	public bool ValidateRemoteCertificates { get; set; }

	/// <summary>
	/// The name of the server that shares SSL connection.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TargetHostKey,
		Description = LocalizedStrings.TargetHostDescKey,
		GroupName = LocalizedStrings.SslKey,
		Order = 206)]
	public string TargetHost { get; set; }

	/// <summary>
	/// Support executions processing, generated by third-party software.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UnknownTransactionsKey,
		Description = LocalizedStrings.UnknownTransactionsDescKey,
		GroupName = LocalizedStrings.SessionKey,
		Order = 24)]
	public bool SupportUnknownExecutions
	{
		get => FixDialect.SupportUnknownExecutions;
		set => FixDialect.SupportUnknownExecutions = value;
	}

	/// <summary>
	/// Cancel On Disconnect.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CancelOnDisconnectKey,
		Description = LocalizedStrings.CancelOnDisconnectKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.SessionKey,
		Order = 25)]
	public bool CancelOnDisconnect
	{
		get => FixDialect.CancelOnDisconnect;
		set => FixDialect.CancelOnDisconnect = value;
	}

	/// <summary>
	/// Do not send <see cref="FixTags.Account"/>.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.DoNotSendAccountKey,
		GroupName = LocalizedStrings.SessionKey,
		Order = 26)]
	public bool DoNotSendAccount
	{
		get => FixDialect.DoNotSendAccount;
		set => FixDialect.DoNotSendAccount = value;
	}

	/// <summary>
	/// Override exec id by native identifier (if present in FIX message).
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExecIdKey,
		Description = LocalizedStrings.OverrideExecIdByNativeKey,
		GroupName = LocalizedStrings.SessionKey,
		Order = 27)]
	public bool OverrideExecIdByNative
	{
		get => FixDialect.OverrideExecIdByNative;
		set => FixDialect.OverrideExecIdByNative = value;
	}

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VersionKey,
		Description = LocalizedStrings.ClientVersionKey,
		GroupName = LocalizedStrings.SessionKey,
		Order = 28)]
	public string ClientVersion
	{
		get => FixDialect.ClientVersion;
		set => FixDialect.ClientVersion = value;
	}

	/// <summary>
	/// Accounts associated with FIX login.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PortfoliosKey,
		//Description = LocalizedStrings.ClientVersionKey,
		GroupName = LocalizedStrings.SessionKey,
		Order = 29)]
	public string Accounts
	{
		get => FixDialect.Accounts;
		set => FixDialect.Accounts = value;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		Dialect = storage.GetValue<string>(nameof(Dialect)).ToDialect(this) ?? typeof(DefaultFixDialect);
		SenderCompId = storage.GetValue<string>(nameof(SenderCompId));
		TargetCompId = storage.GetValue<string>(nameof(TargetCompId));
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		Address = storage.GetValue<string>(nameof(Address)).To<EndPoint>();

		if (storage.ContainsKey(nameof(Encoding)))
			Encoding = storage.GetValue<int>(nameof(Encoding)).To<Encoding>();

		IsResetCounter = storage.GetValue<bool>(nameof(IsResetCounter));
		DateFormat = storage.GetValue(nameof(DateFormat), DateFormat);
		TimeStampFormat = storage.GetValue(nameof(TimeStampFormat), TimeStampFormat);
		TimeFormat = storage.GetValue(nameof(TimeFormat), TimeFormat);
		YearMonthFormat = storage.GetValue(nameof(YearMonthFormat), YearMonthFormat);
		var readTimeout = storage.GetValue(nameof(ReadTimeout), ReadTimeout);
		ReadTimeout = readTimeout == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : readTimeout;
		var writeTimeout = storage.GetValue(nameof(WriteTimeout), WriteTimeout);
		WriteTimeout = writeTimeout == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : writeTimeout;
		ExchangeBoard = storage.GetValue(nameof(ExchangeBoard), ExchangeBoard);
		ClientCode = storage.GetValue(nameof(ClientCode), ClientCode);

		SslProtocol = storage.GetValue<SslProtocols>(nameof(SslProtocol));
		SslCertificate = storage.GetValue<string>(nameof(SslCertificate));
		SslCertificatePassword = storage.GetValue<SecureString>(nameof(SslCertificatePassword));
		CheckCertificateRevocation = storage.GetValue<bool>(nameof(CheckCertificateRevocation));
		ValidateRemoteCertificates = storage.GetValue<bool>(nameof(ValidateRemoteCertificates));
		TargetHost = storage.GetValue<string>(nameof(TargetHost));

		SupportUnknownExecutions = storage.GetValue(nameof(SupportUnknownExecutions), SupportUnknownExecutions);
		CancelOnDisconnect = storage.GetValue(nameof(CancelOnDisconnect), CancelOnDisconnect);
		DoNotSendAccount = storage.GetValue(nameof(DoNotSendAccount), DoNotSendAccount);
		OverrideExecIdByNative = storage.GetValue(nameof(OverrideExecIdByNative), OverrideExecIdByNative);
		ClientVersion = storage.GetValue(nameof(ClientVersion), ClientVersion);
		Accounts = storage.GetValue(nameof(Accounts), Accounts);
		base.Load(storage);
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		storage.SetValue(nameof(Dialect), Dialect.FromDialect());
		storage.SetValue(nameof(SenderCompId), SenderCompId);
		storage.SetValue(nameof(TargetCompId), TargetCompId);
		storage.SetValue(nameof(Login), Login);
		storage.SetValue(nameof(Password), Password);
		storage.SetValue(nameof(IsDemo), IsDemo);
		storage.SetValue(nameof(Address), Address.To<string>());
		storage.SetValue(nameof(Encoding), Encoding.To<int>());
		storage.SetValue(nameof(IsResetCounter), IsResetCounter);
		storage.SetValue(nameof(DateFormat), DateFormat);
		storage.SetValue(nameof(TimeStampFormat), TimeStampFormat);
		storage.SetValue(nameof(TimeFormat), TimeFormat);
		storage.SetValue(nameof(YearMonthFormat), YearMonthFormat);
		storage.SetValue(nameof(ReadTimeout), ReadTimeout);
		storage.SetValue(nameof(WriteTimeout), WriteTimeout);
		storage.SetValue(nameof(ExchangeBoard), ExchangeBoard);
		storage.SetValue(nameof(ClientCode), ClientCode);

		storage.SetValue(nameof(SslProtocol), SslProtocol);
		storage.SetValue(nameof(SslCertificate), SslCertificate);
		storage.SetValue(nameof(SslCertificatePassword), SslCertificatePassword);
		storage.SetValue(nameof(CheckCertificateRevocation), CheckCertificateRevocation);
		storage.SetValue(nameof(ValidateRemoteCertificates), ValidateRemoteCertificates);
		storage.SetValue(nameof(TargetHost), TargetHost);

		storage.SetValue(nameof(SupportUnknownExecutions), SupportUnknownExecutions);
		storage.SetValue(nameof(CancelOnDisconnect), CancelOnDisconnect);
		storage.SetValue(nameof(DoNotSendAccount), DoNotSendAccount);
		storage.SetValue(nameof(OverrideExecIdByNative), OverrideExecIdByNative);
		storage.SetValue(nameof(ClientVersion), ClientVersion);
		storage.SetValue(nameof(Accounts), Accounts);
		base.Save(storage);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return FixDialect?.ToString() ?? base.ToString();
	}
}
