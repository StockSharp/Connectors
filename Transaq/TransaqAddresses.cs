namespace StockSharp.Transaq;

/// <summary>
/// Адреса серверов Transaq.
/// </summary>
public static class TransaqAddresses
{
	/// <summary>
	/// Финам демо сервер. IP адрес tr1-demo5.finam.ru, порт 3939.
	/// </summary>
	public static readonly EndPoint FinamDemo = "tr1-demo5.finam.ru:3939".To<EndPoint>();

	/// <summary>
	/// Финам боевой сервер 1. IP адрес tr1.finam.ru, порт 3900.
	/// </summary>
	public static readonly EndPoint FinamReal1 = "tr1.finam.ru:3900".To<EndPoint>();

	/// <summary>
	/// Финам боевой сервер 2. IP адрес tr2.finam.ru, порт 3900.
	/// </summary>
	public static readonly EndPoint FinamReal2 = "tr2.finam.ru:3900".To<EndPoint>();

	/// <summary>
	/// Финам HFT сервер. Адрес hft.finam.ru, порт 3900.
	/// </summary>
	public static readonly EndPoint FinamHft = "hft.finam.ru:3900".To<EndPoint>();

	/// <summary>
	/// Финам банк сервер 1. Адрес tr1.finambank.ru, порт 3324.
	/// </summary>
	public static readonly EndPoint FinamBank1 = "tr1.finambank.ru:3324".To<EndPoint>();

	/// <summary>
	/// Финам банк сервер 2. Адрес tr1.finambank.ru, порт 3324.
	/// </summary>
	public static readonly EndPoint FinamBank2 = "tr2.finambank.ru:3324".To<EndPoint>();
}