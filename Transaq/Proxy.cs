namespace StockSharp.Transaq;

using System.ComponentModel;

/// <summary>
/// Прокси.
/// </summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public class Proxy : IPersistable
{
	/// <summary>
	/// Использовать прокси.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.EnabledKey,
		Description = LocalizedStrings.ProxyUsedKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public bool IsEnabled { get; set; }

	/// <summary>
	/// Тип протокола, который использует прокси.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ProtocolKey,
		Description = LocalizedStrings.ProxyProtocolKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public ProxyTypes Type { get; set; }

	/// <summary>
	/// Адрес прокси.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ProxyAddressKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 1)]
	public EndPoint Address { get; set; }

	/// <summary>
	/// Логин (если прокси требует авторизацию).
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.ProxyLoginKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 2)]
	public string Login { get; set; }

	private SecureString _password;

	/// <summary>
	/// Пароль (если прокси требует авторизацию).
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.ProxyPasswordKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 3)]
	public string Password
	{
		get => _password.UnSecure();
		set => _password = value.Secure();
	}

	void IPersistable.Load(SettingsStorage storage)
	{
		IsEnabled = storage.GetValue<bool>(nameof(IsEnabled));
		Address = storage.GetValue<EndPoint>(nameof(Address));
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<string>(nameof(Password));
		Type = storage.GetValue<ProxyTypes>(nameof(Type));
	}

	void IPersistable.Save(SettingsStorage storage)
	{
		storage.SetValue(nameof(IsEnabled), IsEnabled);
		storage.SetValue(nameof(Address), Address.To<string>());
		storage.SetValue(nameof(Login), Login);
		storage.SetValue(nameof(Password), Password);
		storage.SetValue(nameof(Type), Type.To<string>());
	}
}

/// <summary>
/// Типы протоколов прокси.
/// </summary>
public enum ProxyTypes
{
	/// <summary>
	/// SOCKS 4.
	/// </summary>
	[Display(Name = "SOCKS 4")]
	Socks4,

	/// <summary>
	/// SOCKS 5.
	/// </summary>
	[Display(Name = "SOCKS 5")]
	Socks5,

	/// <summary>
	/// HHTP Proxy.
	/// </summary>
	[Display(Name = "HTTP")]
	Http
}