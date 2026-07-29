namespace StockSharp.Transaq.Native.Responses;

class PortfolioMctResponse : BaseResponse
{
	/// <summary>
	/// Код клиента.
	/// </summary>
	public string Client { get; set; }

	/// <summary>
	/// Валюта портфеля клиента.
	/// </summary>
	public string Currency { get; set; }

	/// <summary>
	/// Величина капитала.
	/// </summary>
	public double? Capital { get; set; }

	/// <summary>
	/// Использование капитала (фактическое).
	/// </summary>
	public double? UtilizationFact { get; set; }

	/// <summary>
	/// Использование капитала (плановое).
	/// </summary>
	public double? UtilizationPlan { get; set; }

	/// <summary>
	/// Фактическая обеспеченность.
	/// </summary>
	public double? CoverageFact { get; set; }

	/// <summary>
	/// Плановая обеспеченность.
	/// </summary>
	public double? CoveragePlan { get; set; }

	/// <summary>
	/// Входящее сальдо.
	/// </summary>
	public double? OpenBalance { get; set; }

	/// <summary>
	/// Суммарная комиссия.
	/// </summary>
	public double? Tax { get; set; }

	/// <summary>
	/// Прибыль/убыток по входящим позициям.
	/// </summary>
	public double? PnLIncome { get; set; }

	/// <summary>
	/// Прибыль/убыток по сделкам.
	/// </summary>
	public double? PnLIntraday { get; set; }

	public IEnumerable<MctSecurity> Securities { get; set; }
}

class MctSecurity
{
	/// <summary>
	/// Id инструмента.
	/// </summary>
	public string SecId { get; set; }

	/// <summary>
	/// Id рынка.
	/// </summary>
	public int Market { get; set; }

	/// <summary>
	/// Обозначение инструмента.
	/// </summary>
	public string SecCode { get; set; }

	/// <summary>
	/// Валюта цены инструмента.
	/// </summary>
	public string Currency { get; set; }

	/// <summary>
	/// Ставка ГО (либо long, либо short, в зависимости от позиции клиента) по инструменту для клиента.
	/// </summary>
	public double? GoRate { get; set; }

	/// <summary>
	/// Ставка ГО long по инструменту для клиента.
	/// </summary>
	public double? GoRateLong { get; set; }

	/// <summary>
	/// Ставка ГО short по инструменту для клиента.
	/// </summary>
	public double? GoRateShort { get; set; }

	/// <summary>
	/// Текущая цена.
	/// </summary>
	public double? Price { get; set; }

	/// <summary>
	/// Входящая цена позиции (цена последнего клиринга).
	/// </summary>
	public double? InitRate { get; set; }

	/// <summary>
	/// Кросс-курс валюты портфеля к валюте контракта.
	/// </summary>
	public double? CrossRate { get; set; }

	/// <summary>
	/// Входящий кросс-курс валюты портфеля к валюте контракта.
	/// </summary>
	public double? InitCrossRate { get; set; }

	/// <summary>
	/// Входящая позиция, штук.
	/// </summary>
	public int OpenBalance { get; set; }

	/// <summary>
	/// Куплено, штук.
	/// </summary>
	public int Bought { get; set; }

	/// <summary>
	/// Продано, штук.
	/// </summary>
	public int Sold { get; set; }

	/// <summary>
	/// Текущая позиция, штук.
	/// </summary>
	public int Balance { get; set; }

	/// <summary>
	/// Заявлено купить, штук.
	/// </summary>
	public int Buying { get; set; }

	/// <summary>
	/// Заявлено продать, штук.
	/// </summary>
	public int Selling { get; set; }

	/// <summary>
	/// Текущая стоимость позиции.
	/// </summary>
	public double? PosCost { get; set; }

	/// <summary>
	/// ГО позиции (фактическое).
	/// </summary>
	public double? GoPosFact { get; set; }

	/// <summary>
	/// ГО позиции (плановое).
	/// </summary>
	public double? GoPosPlan { get; set; }

	/// <summary>
	/// Комиссия по сделкам в инструменте.
	/// </summary>
	public double? Tax { get; set; }

	/// <summary>
	/// Прибыль/убыток по входящим позициям в инструменте.
	/// </summary>
	public double? PnLIncome { get; set; }

	/// <summary>
	/// Прибыль/убыток по сделкам в инструменте.
	/// </summary>
	public double? PnLIntraday { get; set; }

	/// <summary>
	/// Максимум купить (лот).
	/// </summary>
	public long MaxBuy { get; set; }

	/// <summary>
	/// Максимум продать (лот).
	/// </summary>
	public long MaxSell { get; set; }

	/// <summary>
	/// Средняя цена покупки.
	/// </summary>
	public double? BoughtAverage { get; set; }

	/// <summary>
	/// Средняя цена продажи.
	/// </summary>
	public double? SoldAverage { get; set; }
}