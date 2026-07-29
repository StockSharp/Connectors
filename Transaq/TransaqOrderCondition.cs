namespace StockSharp.Transaq;

using System.Runtime.Serialization;

// Из доки(стр. 23 низ): Защитный спред, объем quantity, цену заявки для stop loss и коррекцию можно задавать как в аболютной влеичине,
// так и в процентах(от цены, либо от позиции клиента по смылсу).

/// <summary>
/// Типы стоп-заявок заявок.
/// </summary>
[Serializable]
[DataContract]
public enum TransaqOrderConditionTypes
{
	/// <summary>
	/// SL предназначен для закрытия позиции с целью ограничения убытков от удержания позиции при неблагоприятном движении цены на рынке.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopOrderTypeKey)]
	[EnumMember]
	StopLoss,

	/// <summary>
	/// TP предназначен для закрытия позиции с фиксацией прибыли.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TakeProfitKey)]
	[EnumMember]
	TakeProfit,

	/// <summary>
	/// TP + SL. При выполнении условия для одной части, вторая часть снимается.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TakeProfitStopLossKey)]
	[EnumMember]
	TakeProfitStopLoss,

	/// <summary>
	/// Алгоритмическая заявка.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AlgoKey)]
	[EnumMember]
	Algo,
}

/// <summary>
/// Допустимые типы условия.
/// </summary>
[Serializable]
[DataContract]
public enum TransaqAlgoOrderConditionTypes
{
	/// <summary>
	/// Нет.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.NoKey)]
	[EnumMember]
	None,

	/// <summary>
	/// Лучшая цена покупки.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BidKey)]
	[EnumMember]
	Bid,

	/// <summary>
	/// Лучшая цена покупки или сделка по заданной цене и выше.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BidOrTradeKey)]
	[EnumMember]
	BidOrLast,

	/// <summary>
	/// Лучшая цена продажи.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AskKey)]
	[EnumMember]
	Ask,

	/// <summary>
	/// Лучшая цена продажи или сделка по заданной цене и ниже.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AskOrTradeKey)]
	[EnumMember]
	AskOrLast,

	/// <summary>
	/// Время выставления заявки на Биржу.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TimeKey)]
	[EnumMember]
	Time,

	/// <summary>
	/// Обеспеченность ниже заданной.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MarginLowKey)]
	[EnumMember]
	CovDown,

	/// <summary>
	/// Обеспеченность выше заданной.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MarginHighKey)]
	[EnumMember]
	CovUp,

	/// <summary>
	/// Сделка на рынке по заданной цене или выше.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UpKey)]
	[EnumMember]
	LastUp,

	/// <summary>
	/// Сделка на рынке по заданной цене или ниже.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DownKey)]
	[EnumMember]
	LastDown
}

/// <summary>
/// Условие действительности заявки.
/// </summary>
[Serializable]
[DataContract]
public enum TransaqAlgoOrderValidTypes
{
	/// <summary>
	/// По дате и времени.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DateKey)]
	[EnumMember]
	Date,

	/// <summary>
	/// Немедленно.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ImmediatelyKey)]
	[EnumMember]
	Immediately,

	/// <summary>
	/// До отмены.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.GTCKey)]
	[EnumMember]
	TillCancelled
}

/// <summary>
/// Условия стоп-заявок, специфичных для <see cref="Transaq"/>.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TransaqKey)]
public class TransaqOrderCondition : OrderCondition,
									 IStopLossOrderCondition, ITakeProfitOrderCondition,
									 IRepoOrderCondition, INtmOrderCondition
{
	/// <summary>
	/// Создать <see cref="TransaqOrderCondition"/>.
	/// </summary>
	public TransaqOrderCondition()
	{
		Type = TransaqOrderConditionTypes.StopLoss;

		IsRepo = false;
		IsNtm = false;
		RepoInfo = new RepoOrderInfo();
		NtmInfo = new NtmOrderInfo();
	}

	/// <summary>
	/// Тип стоп-заявки.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopOrderTypeKey,
		Description = LocalizedStrings.StopOrderTypeDescKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public TransaqOrderConditionTypes Type
	{
		get => (TransaqOrderConditionTypes)Parameters[nameof(Type)];
		set => Parameters[nameof(Type)] = value;
	}

	/// <summary>
	/// Идентификатор связанной заявки.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LinkedOrderKey,
		Description = LocalizedStrings.LinkedOrderDescKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public long? LinkedOrderId
	{
		get => (long?)Parameters.TryGetValue(nameof(LinkedOrderId));
		set => Parameters[nameof(LinkedOrderId)] = value;
	}

	/// <summary>
	/// Заявка действительна до.
	/// </summary>
	[DataMember]
	//[Nullable]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TillKey,
		Description = LocalizedStrings.OrderExpirationTimeKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 2)]
	public DateTime? ValidFor
	{
		get => (DateTime?)Parameters.TryGetValue(nameof(ValidFor));
		set => Parameters[nameof(ValidFor)] = value;
	}

	#region SL

	/// <summary>
	/// Цена активации, при достижении которой будет выставлена заявка по цене, указанной в <see cref="StopLossOrderPrice"/>.
	/// </summary>
	[DataMember]
	//[Nullable]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopLossActivationPriceKey,
		GroupName = LocalizedStrings.StopLossKey,
		Order = 10)]
	public decimal? StopLossActivationPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLossActivationPrice));
		set => Parameters[nameof(StopLossActivationPrice)] = value;
	}

	/// <summary>
	/// Цена выставляемой заявки, которая будет отправлена на биржу при активации по цене, указанной в <see cref="StopLossActivationPrice"/>.
	/// Абсолютное значение, или в процентах.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OrderPrice2Key,
		Description = LocalizedStrings.StopLossOrderPriceKey,
		GroupName = LocalizedStrings.StopLossKey,
		Order = 11)]
	public Unit StopLossOrderPrice
	{
		get => (Unit)Parameters.TryGetValue(nameof(StopLossOrderPrice));
		set => Parameters[nameof(StopLossOrderPrice)] = value;
	}

	/// <summary>
	/// Выставить заявку по рынку (в этом случае <see cref="StopLossOrderPrice"/> игнорируется).
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ByMarketKey,
		Description = LocalizedStrings.MarketByOrderKey,
		GroupName = LocalizedStrings.StopLossKey,
		Order = 12)]
	public bool? StopLossByMarket
	{
		get => (bool?)Parameters.TryGetValue(nameof(StopLossByMarket));
		set => Parameters[nameof(StopLossByMarket)] = value;
	}

	/// <summary>
	/// Объем (абсолютное значение или в процентах).
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OrderVolume2Key,
		Description = LocalizedStrings.OrderVolumeKey,
		GroupName = LocalizedStrings.StopLossKey,
		Order = 13)]
	public Unit StopLossVolume
	{
		get => (Unit)Parameters.TryGetValue(nameof(StopLossVolume));
		set => Parameters[nameof(StopLossVolume)] = value;
	}

	/// <summary>
	/// Использовать кредит.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UseCreditKey,
		Description = LocalizedStrings.UseCreditKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.StopLossKey,
		Order = 14)]
	public bool? StopLossUseCredit
	{
		get => (bool?)Parameters.TryGetValue(nameof(StopLossUseCredit));
		set => Parameters[nameof(StopLossUseCredit)] = value;
	}

	/// <summary>
	/// Защитное время, в сек. Защитное время позволяет предотвратить исполнение при "проколах" на рынке.
	/// Т.е. в таких ситуациях, когда цены на рынке лишь кратковременно достигают уровня <see cref="StopLossActivationPrice"/>, и вскоре возвращаются обратно.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeKey,
		Description = LocalizedStrings.ProtectionTimeKey,
		GroupName = LocalizedStrings.StopLossKey,
		Order = 15)]
	public int? StopLossProtectionTime
	{
		get => (int?)Parameters.TryGetValue(nameof(StopLossProtectionTime));
		set => Parameters[nameof(StopLossProtectionTime)] = value;
	}

	#endregion

	#region TP

	/// <summary>
	/// Цена активации, при достижении которой будет отправлена заявка на биржу с указанной ценой, с учетом <see cref="TakeProfitProtectionSpread"/>.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.ActivationPriceDescKey,
		GroupName = LocalizedStrings.TakeProfitKey,
		Order = 20)]
	public decimal? TakeProfitActivationPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TakeProfitActivationPrice));
		set => Parameters[nameof(TakeProfitActivationPrice)] = value;
	}

	/// <summary>
	/// Выставить заявку по рынку.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ByMarketKey,
		Description = LocalizedStrings.OrdersByMarketKey,
		GroupName = LocalizedStrings.TakeProfitKey,
		Order = 21)]
	public bool? TakeProfitByMarket
	{
		get => (bool?)Parameters.TryGetValue(nameof(TakeProfitByMarket));
		set => Parameters[nameof(TakeProfitByMarket)] = value;
	}

	/// <summary>
	/// Объем.
	/// Абсолютное значение, или в процентах.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.OrderVolume2Key,
		GroupName = LocalizedStrings.TakeProfitKey,
		Order = 21)]
	public Unit TakeProfitVolume
	{
		get => (Unit)Parameters.TryGetValue(nameof(TakeProfitVolume));
		set => Parameters[nameof(TakeProfitVolume)] = value;
	}

	/// <summary>
	/// Использовать кредит.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UseCreditKey,
		Description = LocalizedStrings.UseCreditKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.TakeProfitKey,
		Order = 22)]
	public bool? TakeProfitUseCredit
	{
		get => (bool?)Parameters.TryGetValue(nameof(TakeProfitUseCredit));
		set => Parameters[nameof(TakeProfitUseCredit)] = value;
	}

	/// <summary>
	/// Защитное время, в сек. Защитное время позволяет предотвратить исполнение при "проколах" на рынке.
	/// Т.е. в таких ситуациях, когда цены на рынке лишь кратковременно достигают уровня <see cref="StopLossActivationPrice"/>, и вскоре возвращаются обратно.
	/// Нужно при использовании трейлинга, при выставленном значении <see cref="TakeProfitCorrection"/>.
	/// </summary>
	[DataMember]
	//[Nullable]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeKey,
		Description = LocalizedStrings.ProtectionTimeKey,
		GroupName = LocalizedStrings.TakeProfitKey,
		Order = 23)]
	public int? TakeProfitProtectionTime
	{
		get => (int?)Parameters.TryGetValue(nameof(TakeProfitProtectionTime));
		set => Parameters[nameof(TakeProfitProtectionTime)] = value;
	}

	/// <summary>
	/// Коррекция. Если задано, то после активации заявки по <see cref="TakeProfitActivationPrice"/> и снижении цены (для TP на продажу)
	/// или повышения цены (для TP на покупку) будет послана заявка по цене, с учетом <see cref="TakeProfitProtectionSpread"/>.
	/// Абсолютное значение, или в процентах.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CorrectionKey,
		Description = LocalizedStrings.CorrectionDescKey,
		GroupName = LocalizedStrings.TakeProfitKey,
		Order = 25)]
	public Unit TakeProfitCorrection
	{
		get => (Unit)Parameters.TryGetValue(nameof(TakeProfitCorrection));
		set => Parameters[nameof(TakeProfitCorrection)] = value;
	}

	/// <summary>
	/// Защитный спред. Величина, которя будет прибавлятся (при TP на покупку) или отниматься (при TP на продажу)
	/// к цене <see cref="TakeProfitActivationPrice"/>, при отравке заявки на биржу.
	/// Абсолютное значение, или в процентах.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpreadKey,
		Description = LocalizedStrings.ProtectionSpreadKey,
		GroupName = LocalizedStrings.TakeProfitKey,
		Order = 26)]
	public Unit TakeProfitProtectionSpread
	{
		get => (Unit)Parameters.TryGetValue(nameof(TakeProfitProtectionSpread));
		set => Parameters[nameof(TakeProfitProtectionSpread)] = value;
	}

	#endregion TP

	#region Algo

	/// <summary>
	/// Условие.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ConditionKey,
		Description = LocalizedStrings.OrderConditionDescKey,
		GroupName = LocalizedStrings.AlgoKey,
		Order = 50)]
	public TransaqAlgoOrderConditionTypes? AlgoType
	{
		get => (TransaqAlgoOrderConditionTypes?)Parameters.TryGetValue(nameof(AlgoType));
		set => Parameters[nameof(AlgoType)] = value;
	}

	/// <summary>
	/// Цена для заявки, либо обеспеченность в процентах.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OrderPrice2Key,
		Description = LocalizedStrings.OrderPriceKey,
		GroupName = LocalizedStrings.AlgoKey,
		Order = 51)]
	public decimal? AlgoValue
	{
		get => (decimal?)Parameters.TryGetValue(nameof(AlgoValue));
		set => Parameters[nameof(AlgoValue)] = value;
	}

	/// <summary>
	/// Условие действительности заявки.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TypeKey,
		Description = LocalizedStrings.ValidConditionsKey,
		GroupName = LocalizedStrings.AlgoKey,
		Order = 52)]
	public TransaqAlgoOrderValidTypes? AlgoValidAfterType
	{
		get => (TransaqAlgoOrderValidTypes?)Parameters.TryGetValue(nameof(AlgoValidAfterType));
		set => Parameters[nameof(AlgoValidAfterType)] = value;
	}

	/// <summary>
	/// С какого момента времени действительна.
	/// </summary>
	[DataMember]
	//[Nullable]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ValidAfterKey,
		Description = LocalizedStrings.ValidAfterDescKey,
		GroupName = LocalizedStrings.AlgoKey,
		Order = 53)]
	public DateTime? AlgoValidAfter
	{
		get => (DateTime?)Parameters.TryGetValue(nameof(AlgoValidAfter));
		set => Parameters[nameof(AlgoValidAfter)] = value;
	}

	/// <summary>
	/// Условие действительности заявки.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ValidBeforeTypeKey,
		Description = LocalizedStrings.ValidConditionsKey,
		GroupName = LocalizedStrings.AlgoKey,
		Order = 54)]
	public TransaqAlgoOrderValidTypes? AlgoValidBeforeType
	{
		get => (TransaqAlgoOrderValidTypes?)Parameters.TryGetValue(nameof(AlgoValidBeforeType));
		set => Parameters[nameof(AlgoValidBeforeType)] = value;
	}

	/// <summary>
	/// До какого момента времени действительна.
	/// </summary>
	[DataMember]
	//[Nullable]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TillKey,
		Description = LocalizedStrings.OrderExpirationTimeKey,
		GroupName = LocalizedStrings.AlgoKey,
		Order = 55)]
	public DateTime? AlgoValidBefore
	{
		get => (DateTime?)Parameters.TryGetValue(nameof(AlgoValidBefore));
		set => Parameters[nameof(AlgoValidBefore)] = value;
	}

	#endregion

	/// <inheritdoc />
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UseKey,
		Description = LocalizedStrings.RepoKey,
		GroupName = LocalizedStrings.RepoKey,
		Order = 100)]
	public bool IsRepo
	{
		get => (bool)Parameters[nameof(IsRepo)];
		set => Parameters[nameof(IsRepo)] = value;
	}

	/// <inheritdoc />
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RepoKey,
		Description = LocalizedStrings.RepoInfoKey,
		GroupName = LocalizedStrings.RepoKey,
		Order = 101)]
	public RepoOrderInfo RepoInfo
	{
		get => (RepoOrderInfo)Parameters[nameof(RepoInfo)];
		set => Parameters[nameof(RepoInfo)] = value ?? throw new ArgumentNullException(nameof(value));
	}

	/// <inheritdoc />
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UseKey,
		Description = LocalizedStrings.NtmDescKey,
		GroupName = LocalizedStrings.NtmKey,
		Order = 200)]
	public bool IsNtm
	{
		get => (bool)Parameters[nameof(IsNtm)];
		set => Parameters[nameof(IsNtm)] = value;
	}

	/// <inheritdoc />
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.NtmKey,
		Description = LocalizedStrings.NtmInfoKey,
		GroupName = LocalizedStrings.NtmKey,
		Order = 201)]
	public NtmOrderInfo NtmInfo
	{
		get => (NtmOrderInfo)Parameters[nameof(NtmInfo)];
		set => Parameters[nameof(NtmInfo)] = value ?? throw new ArgumentNullException(nameof(value));
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => (decimal?)StopLossOrderPrice;
		set
		{
			if (value == null)
				StopLossByMarket = true;
			else
			{
				StopLossByMarket = false;
				StopLossOrderPrice = value;
			}
		}
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => TakeProfitActivationPrice;
		set
		{
			Type = StopLossActivationPrice == null ? TransaqOrderConditionTypes.StopLoss : TransaqOrderConditionTypes.TakeProfitStopLoss;
			TakeProfitActivationPrice = value;
		}
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set { }
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice
	{
		get => (decimal?)StopLossOrderPrice;
		set
		{
			if (value == null)
				TakeProfitByMarket = true;
			else
			{
				TakeProfitByMarket = false;
				StopLossOrderPrice = value;
			}
		}
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => StopLossActivationPrice;
		set
		{
			Type = TakeProfitActivationPrice == null ? TransaqOrderConditionTypes.TakeProfit : TransaqOrderConditionTypes.TakeProfitStopLoss;
			StopLossActivationPrice = value;
		}
	}
}
