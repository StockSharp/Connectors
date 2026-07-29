namespace StockSharp.Transaq.Native.Responses;

class PortfolioResponse : BaseResponse
{
	/// <summary>
	/// Код клиента.
	/// </summary>
	public string Client { get; set; }

	/// <summary>
	/// Код юниона.
	/// </summary>
	public string Union { get; set; }

	/// <summary>
	/// Входящая оценка портфеля без дисконта.
	/// </summary>
	public double? OpenEquity { get; set; }

	/// <summary>
	/// Текущая оценка портфеля без дисконта.
	/// </summary>
	public double? Equity { get; set; }

	/// <summary>
	/// Прибыль-убыток общий.
	/// </summary>
	public double? PnL { get; set; }

	/// <summary>
	/// Размер требуемого ГО FORTS (рассчитанный биржей).
	/// </summary>
	public double? Margin { get; set; }

	/// <summary>
	/// Плановое обеспечение (оценка ликвидационной стоимости портфеля).
	/// </summary>
	public double? Cover { get; set; }

	/// <summary>
	/// Критическая обеспеченность.
	/// </summary>
	public double? CoverageCrit { get; set; }

	/// <summary>
	/// Плановый риск (размер начальных требований).
	/// </summary>
	public double? ReqInit { get; set; }

	/// <summary>
	/// Размер минимальных требований.
	/// </summary>
	public double? ReqMaint { get; set; }

	/// <summary>
	/// Нереализованная прибыль-убыток.
	/// </summary>
	public double? PnLUnreal { get; set; }

	public IEnumerable<Asset> Assets { get; set; }

	public IEnumerable<Money> Money { get; set; }

	public IEnumerable<TPlusSecurity> Securities { get; set; }
}

abstract class PortfolioBasePart
{
	/// <summary>
	/// Наименование денежного раздела.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Код валюты.
	/// </summary>
	public string Currency { get; set; }

	/// <summary>
	/// Входящая денежная позиция.
	/// </summary>
	public double? OpenBalance { get; set; }

	/// <summary>
	/// Затрачено на покупки.
	/// </summary>
	public double? Bought { get; set; }

	/// <summary>
	/// Выручено с продаж.
	/// </summary>
	public double? Sold { get; set; }

	/// <summary>
	/// Текущая денежная позиция.
	/// </summary>
	public double? Balance { get; set; }

	/// <summary>
	/// Сумма плановых покупок.
	/// </summary>
	public double? Blocked { get; set; }

	/// <summary>
	/// Сумма плановых продаж.
	/// </summary>
	public double? Estimated { get; set; }
}

class Asset : PortfolioBasePart
{
	public string Code { get; set; }

	public double? SetoffRate { get; set; }
	public double? ReqInit { get; set; }
	public double? ReqMaint { get; set; }
}

class Money : PortfolioBasePart
{
	/// <summary>
	/// Код базового актива.
	/// </summary>
	public string Asset { get; set; }

	/// <summary>
	/// Вариационная маржа.
	/// </summary>
	public double? VarMargin { get; set; }

	/// <summary>
	/// Уплачено комиссии.
	/// </summary>
	public double? Fee { get; set; }

	/// <summary>
	/// Фин. результат последнего клиринга.
	/// </summary>
	public double? FinRes { get; set; }

	/// <summary>
	/// Вклад в плановое обеспечение.
	/// </summary>
	public double? Cover { get; set; }

	public IEnumerable<MoneyValuePart> ValueParts { get; set; }
}

class MoneyValuePart
{
	/// <summary>
	/// Регистр учета.
	/// </summary>
	public string Register { get; set; }

	/// <summary>
	/// Входящая денежная позиция.
	/// </summary>
	public double? OpenBalance { get; set; }

	/// <summary>
	/// Затрачено на покупки.
	/// </summary>
	public double? Bought { get; set; }

	/// <summary>
	/// Выручено с продаж.
	/// </summary>
	public double? Sold { get; set; }

	/// <summary>
	/// Исполнено.
	/// </summary>
	public double? Settled { get; set; }

	/// <summary>
	/// Текущая денежная позиция.
	/// </summary>
	public double? Balance { get; set; }
}

class TPlusSecurity
{
	/// <summary>
	/// Id инструмента.
	/// </summary>
	public int SecId { get; set; }

	/// <summary>
	/// Id рынка.
	/// </summary>
	public int Market { get; set; }

	/// <summary>
	/// Обозначение инструмента.
	/// </summary>
	public string SecCode { get; set; }

	/// <summary>
	/// Текущая цена.
	/// </summary>
	public double? Price { get; set; }

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
	/// Балансовая цена.
	/// </summary>
	public double? BalancePrc { get; set; }

	/// <summary>
	/// Нереализованные прибыли/убытки.
	/// </summary>
	public double? UnrealizedPnL { get; set; }

	/// <summary>
	/// Заявлено купить, штук.
	/// </summary>
	public int Buying { get; set; }

	/// <summary>
	/// Заявлено продать, штук.
	/// </summary>
	public int Selling { get; set; }

	/// <summary>
	/// Вклад бумаги в плановое обеспечение.
	/// </summary>
	public double? Cover { get; set; }

	/// <summary>
	/// Плановая начальная маржа(риск).
	/// </summary>
	public double? InitMargin { get; set; }

	/// <summary>
	/// Прибыль/убыток общий.
	/// </summary>
	public double? PnL { get; set; }

	/// <summary>
	/// Прибыль/убыток по входящим позициям.
	/// </summary>
	public double? PnLIncome { get; set; }

	/// <summary>
	/// Прибыль/убыток по сделкам.
	/// </summary>
	public double? PnLIntraday { get; set; }

	/// <summary>
	/// Ставка риска для лонгов.
	/// </summary>
	public double? RiskRateLong { get; set; }

	/// <summary>
	/// Ставка риска для шортов.
	/// </summary>
	public double? RiskRateShort { get; set; }

	/// <summary>
	/// Максимальная покупка, в лотах.
	/// </summary>
	public int MaxBuy { get; set; }

	/// <summary>
	/// Максимальная продажа, в лотах.
	/// </summary>
	public int MaxSell { get; set; }

	public IEnumerable<TPlusSecurityValuePart> ValueParts { get; set; }
}

class TPlusSecurityValuePart
{
	/// <summary>
	/// Регистр учета.
	/// </summary>
	public string Register { get; set; }

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
	/// Исполнено, штук.
	/// </summary>
	public int Settled { get; set; }

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
}
