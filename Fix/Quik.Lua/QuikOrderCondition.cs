namespace StockSharp.Fix.Quik.Lua;

using System.Globalization;
using System.Runtime.Serialization;

/// <summary>
/// Stop price conditions relative to the price of the last trade of the instrument.
/// </summary>
[Serializable]
[DataContract]
public enum QuikStopPriceConditions
{
	/// <summary>
	/// Greater than or equal.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MoreOrEqualKey)]
	[EnumMember]
	MoreOrEqual,

	/// <summary>
	/// Less than or equal.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LessOrEqualKey)]
	[EnumMember]
	LessOrEqual,
}

/// <summary>
/// Types of order conditions specific to <see cref="QuikOrderCondition"/>.
/// </summary>
[Serializable]
[DataContract]
public enum QuikOrderConditionTypes
{
	/// <summary>
	/// Two orders for the same instrument, identical in direction and volume. The first order is of type "Stop-limit", the second is a limit order.
	/// When one order is executed, the other is canceled.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LinkedOrderKey)]
	[EnumMember]
	LinkedOrder,

	/// <summary>
	/// An order of type "Stop-limit" whose stop-price condition is checked against one instrument, while another instrument is specified in the resulting limit order.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecurityKey)]
	[EnumMember]
	OtherSecurity,

	/// <summary>
	/// A stop order that generates a limit order upon activation.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopLimitKey)]
	[EnumMember]
	StopLimit,

	/// <summary>
	/// An order with the condition: "Execute when the price worsens by the specified value from the reached maximum (for sell) or minimum (for buy)."
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TakeProfitKey)]
	[EnumMember]
	TakeProfit,

	/// <summary>
	/// An order with two conditions: "take-profit" if the last trade price, after reaching a maximum, worsens by more than the specified offset;
	/// "stop-limit" if the last trade price worsens to the specified level.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TakeProfitStopLossKey)]
	[EnumMember]
	TakeProfitStopLimit,
}

///// <summary>
///// Result of execution for orders specific to <see cref="QuikTrader"/>.
///// </summary>
//[Serializable]
//[System.Runtime.Serialization.DataContract]
//public enum QuikOrderConditionResults
//{
//	/// <summary>
//	/// The order was accepted by the trading system.
//	/// </summary>
//	[EnumMember]
//	SentToTS,
//
//	/// <summary>
//	/// The order was rejected by the trading system.
//	/// </summary>
//	[EnumMember]
//	RejectedByTS,
//
//	/// <summary>
//	/// The order was canceled by the user.
//	/// </summary>
//	[EnumMember]
//	Killed,
//
//	/// <summary>
//	/// Insufficient client funds to execute the order.
//	/// </summary>
//	[EnumMember]
//	LimitControlFailed,
//
//	/// <summary>
//	/// The limit order linked to the stop order was canceled by the user.
//	/// </summary>
//	[EnumMember]
//	LinkedOrderKilled,
//
//	/// <summary>
//	/// The linked limit order was satisfied by the trading system.
//	/// </summary>
//	[EnumMember]
//	LinkedOrderFilled,
//
//	/// <summary>
//	/// The activation condition did not occur. Parameter for orders of types "Take-profit" and "On execution".
//	/// </summary>
//	[EnumMember]
//	WaitingForActivation,
//
//	/// <summary>
//	/// The activation condition has occurred; calculation of the minimum/maximum price has started. Parameter for orders of types "Take-profit" and "Take-profit by order".
//	/// </summary>
//	[EnumMember]
//	CalculateMinMax,
//
//	/// <summary>
//	/// The order was activated for a partial volume due to partial execution of the conditional order; calculation of the minimum/maximum price has started.
//	/// Parameter for orders of type "Take-profit by order" with the flag "Partial execution of the order is taken into account" enabled.
//	/// </summary>
//	[EnumMember]
//	CalculateMinMaxAndWaitForActivation,
//}

/// <summary>
/// Order condition specific to <see cref="Quik"/>.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.QuikKey)]
public class QuikOrderCondition : OrderCondition,
                                  IStopLossOrderCondition, ITakeProfitOrderCondition,
								  IRepoOrderCondition, INtmOrderCondition, IWireCondition
{
	private const string _wireVersion = "v1";
	private const string _activeTimeFromKey = nameof(ActiveTime) + ".From";
	private const string _activeTimeToKey = nameof(ActiveTime) + ".To";
	private const string _repoPrefix = "Repo.";
	private const string _ntmPrefix = "Ntm.";

	/// <summary>
	/// Create <see cref="QuikOrderCondition"/>.
	/// </summary>
	public QuikOrderCondition()
	{
		IsRepo = false;
		IsNtm = false;
		RepoInfo = new RepoOrderInfo();
		NtmInfo = new NtmOrderInfo();
	}

	/// <inheritdoc />
	public override QuikOrderCondition Clone()
	{
		var parameters = ((SynchronizedDictionary<string, object>)Parameters).SyncGet(source =>
			source.Select(pair => new KeyValuePair<string, object>(
				pair.Key,
				pair.Value is ICloneable cloneable ? cloneable.Clone() : pair.Value)).ToArray());

		var clone = new QuikOrderCondition();
		clone.Parameters.Clear();
		clone.Parameters.AddRange(parameters);
		return clone;
	}

	/// <summary>
	/// Stop-order type.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopOrderTypeKey,
		Description = LocalizedStrings.StopOrderTypeDescKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public QuikOrderConditionTypes? Type
	{
		get => (QuikOrderConditionTypes?)Parameters.TryGetValue(nameof(Type));
		set => Parameters[nameof(Type)] = value;
	}

	/// <summary>
	/// Instrument identifier for stop orders with a condition based on another instrument.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecurityIdKey,
		Description = LocalizedStrings.OtherSecurityIdKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public SecurityId? OtherSecurityId
	{
		get => (SecurityId?)Parameters.TryGetValue(nameof(OtherSecurityId));
		set => Parameters[nameof(OtherSecurityId)] = value;
	}

	/// <summary>
	/// Stop-price condition. Used for orders of type "Stop price by another instrument".
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ConditionKey,
		Description = LocalizedStrings.StopPriceConditionKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public QuikStopPriceConditions? StopPriceCondition
	{
		get => (QuikStopPriceConditions?)Parameters.TryGetValue(nameof(StopPriceCondition));
		set => Parameters[nameof(StopPriceCondition)] = value;
	}

	/// <summary>
	/// Stop price that defines the activation condition of the stop order. For example, for orders of type "Stop price by another instrument", the condition looks like:
	/// "If price &lt;=" (or ">=") and means execution when the last trade price for the other instrument crosses the specified value.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>
	/// Stop-limit price. Similar to <see cref="StopPrice"/>, but used only for the order type "Take-profit and stop-limit".
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLimitPriceKey,
		Description = LocalizedStrings.StopLimitPriceDescKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public decimal? StopLimitPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLimitPrice));
		set => Parameters[nameof(StopLimitPrice)] = value;
	}

	/// <summary>
	/// Execute a "Stop-limit" order at market price.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IsMarketStopLimitKey,
		Description = LocalizedStrings.IsMarketStopLimitKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public bool? IsMarketStopLimit
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsMarketStopLimit));
		set => Parameters[nameof(IsMarketStopLimit)] = value;
	}

	/// <summary>
	/// The condition is checked only during the specified time range (if <see langword="null"/>, do not check).
	/// Used for order types "Take-profit and stop-limit" and "Take-profit and stop-limit by order".
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeKey,
		Description = LocalizedStrings.TimeKey,
		GroupName = LocalizedStrings.ParametersKey)]
	//[Editor(typeof(RangeEditor<DateTime>), typeof(RangeEditor<DateTime>))]
	public (DateTime from, DateTime to) ActiveTime
	{
		get => Parameters.TryGetValue(nameof(ActiveTime), out var value) && value is ValueTuple<DateTime, DateTime> activeTime
			? activeTime
			: default;
		set => Parameters[nameof(ActiveTime)] = value;
	}

	/// <summary>
	/// Conditional order identifier.
	/// </summary>
	public long? ConditionOrderId
	{
		get => (long?)Parameters.TryGetValue(nameof(ConditionOrderId));
		set => Parameters[nameof(ConditionOrderId)] = value;
	}

	/// <summary>
	/// Conditional order side.
	/// </summary>
	public Sides? ConditionOrderSide
	{
		get => (Sides?)Parameters.TryGetValue(nameof(ConditionOrderSide));
		set => Parameters[nameof(ConditionOrderSide)] = value;
	}

	/// <summary>
	/// Partial execution is taken into account. The "on execution" order will be activated upon partial execution of the conditional order <see cref="ConditionOrderId"/>.
	/// If <see langword="false"/> (or <see langword="null"/>), the "on execution" order is activated only upon full execution of the conditional order <see cref="ConditionOrderId"/>.
	/// </summary>
	[DataMember]
	public bool? ConditionOrderPartiallyMatched
	{
		get => (bool?)Parameters.TryGetValue(nameof(ConditionOrderPartiallyMatched));
		set => Parameters[nameof(ConditionOrderPartiallyMatched)] = value;
	}

	/// <summary>
	/// Use the executed volume of the conditional order as the quantity of the placed stop order. The number of instruments in the "on execution" order
	/// is taken from the executed volume of the conditional order <see cref="ConditionOrderId"/>. If <see langword="false"/> (or <see langword="null"/>), the order volume is explicitly specified in <see cref="ExecutionMessage.OrderVolume"/>.
	/// </summary>
	[DataMember]
	public bool? ConditionOrderUseMatchedBalance
	{
		get => (bool?)Parameters.TryGetValue(nameof(ConditionOrderUseMatchedBalance));
		set => Parameters[nameof(ConditionOrderUseMatchedBalance)] = value;
	}

	/// <summary>
	/// Price of the linked limit order.
	/// </summary>
	[DataMember]
	public decimal? LinkedOrderPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(LinkedOrderPrice));
		set => Parameters[nameof(LinkedOrderPrice)] = value;
	}

	/// <summary>
	/// Cancel the stop order upon partial execution of the linked limit order.
	/// </summary>
	[DataMember]
	public bool? LinkedOrderCancel
	{
		get => (bool?)Parameters.TryGetValue(nameof(LinkedOrderCancel));
		set => Parameters[nameof(LinkedOrderCancel)] = value;
	}

	/// <summary>
	/// Offset value from the maximum (minimum) of the last trade price.
	/// </summary>
	[DataMember]
	public Unit Offset
	{
		get => (Unit)Parameters.TryGetValue(nameof(Offset));
		set => Parameters[nameof(Offset)] = value;
	}

	/// <summary>
	/// Protective spread value.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpreadKey,
		Description = LocalizedStrings.SpreadKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public Unit Spread
	{
		get => (Unit)Parameters.TryGetValue(nameof(Spread));
		set => Parameters[nameof(Spread)] = value;
	}

	/// <summary>
	/// Execute a "Take-profit" order at market price.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IsMarketTakeProfitKey,
		Description = LocalizedStrings.IsMarketTakeProfitKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public bool? IsMarketTakeProfit
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsMarketTakeProfit));
		set => Parameters[nameof(IsMarketTakeProfit)] = value;
	}

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
		get => StopLimitPrice;
		set => StopLimitPrice = value;
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopPrice;
		set
		{
			Type = QuikOrderConditionTypes.StopLimit;
			StopPrice = value;
		}
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set { }
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => StopPrice;
		set
		{
			Type = QuikOrderConditionTypes.TakeProfit;
			StopPrice = value;
		}
	}

	/// <inheritdoc />
	public string ToWire()
	{
		var values = new List<string> { _wireVersion };

		if (Type is { } type)
			AddWireValue(values, nameof(Type), ((int)type).ToString(CultureInfo.InvariantCulture));

		if (OtherSecurityId is { } otherSecurityId)
			AddWireValue(values, nameof(OtherSecurityId), otherSecurityId.ToStringId());

		if (StopPriceCondition is { } stopPriceCondition)
			AddWireValue(values, nameof(StopPriceCondition), ((int)stopPriceCondition).ToString(CultureInfo.InvariantCulture));

		AddWireValue(values, nameof(StopPrice), StopPrice);
		AddWireValue(values, nameof(StopLimitPrice), StopLimitPrice);
		AddWireValue(values, nameof(IsMarketStopLimit), IsMarketStopLimit);

		var activeTime = ActiveTime;

		if (activeTime.from != default)
			AddWireValue(values, _activeTimeFromKey, activeTime.from);

		if (activeTime.to != default)
			AddWireValue(values, _activeTimeToKey, activeTime.to);

		AddWireValue(values, nameof(ConditionOrderId), ConditionOrderId);

		if (ConditionOrderSide is { } conditionOrderSide)
			AddWireValue(values, nameof(ConditionOrderSide), ((int)conditionOrderSide).ToString(CultureInfo.InvariantCulture));

		AddWireValue(values, nameof(ConditionOrderPartiallyMatched), ConditionOrderPartiallyMatched);
		AddWireValue(values, nameof(ConditionOrderUseMatchedBalance), ConditionOrderUseMatchedBalance);
		AddWireValue(values, nameof(LinkedOrderPrice), LinkedOrderPrice);
		AddWireValue(values, nameof(LinkedOrderCancel), LinkedOrderCancel);

		if (Offset is { } offset)
			AddWireValue(values, nameof(Offset), offset.ToString());

		if (Spread is { } spread)
			AddWireValue(values, nameof(Spread), spread.ToString());

		AddWireValue(values, nameof(IsMarketTakeProfit), IsMarketTakeProfit);
		AddWireValue(values, nameof(IsRepo), IsRepo);

		var repo = RepoInfo;

		AddWireValue(values, _repoPrefix + nameof(RepoOrderInfo.Partner), repo.Partner);
		AddWireValue(values, _repoPrefix + nameof(RepoOrderInfo.Term), repo.Term);
		AddWireValue(values, _repoPrefix + nameof(RepoOrderInfo.Rate), repo.Rate);
		AddWireValue(values, _repoPrefix + nameof(RepoOrderInfo.BlockSecurities), repo.BlockSecurities);
		AddWireValue(values, _repoPrefix + nameof(RepoOrderInfo.RefundRate), repo.RefundRate);
		AddWireValue(values, _repoPrefix + nameof(RepoOrderInfo.MatchRef), repo.MatchRef);
		AddWireValue(values, _repoPrefix + nameof(RepoOrderInfo.SettleCode), repo.SettleCode);
		AddWireValue(values, _repoPrefix + nameof(RepoOrderInfo.SecondPrice), repo.SecondPrice);
		AddWireValue(values, _repoPrefix + nameof(RepoOrderInfo.SettleDate), repo.SettleDate);
		AddWireValue(values, _repoPrefix + nameof(RepoOrderInfo.StartDiscount), repo.StartDiscount);
		AddWireValue(values, _repoPrefix + nameof(RepoOrderInfo.LowerDiscount), repo.LowerDiscount);
		AddWireValue(values, _repoPrefix + nameof(RepoOrderInfo.UpperDiscount), repo.UpperDiscount);
		AddWireValue(values, _repoPrefix + nameof(RepoOrderInfo.Value), repo.Value);
		AddWireValue(values, _repoPrefix + nameof(RepoOrderInfo.IsModified), repo.IsModified);

		AddWireValue(values, nameof(IsNtm), IsNtm);

		var ntm = NtmInfo;

		AddWireValue(values, _ntmPrefix + nameof(NtmOrderInfo.Partner), ntm.Partner);
		AddWireValue(values, _ntmPrefix + nameof(NtmOrderInfo.SettleDate), ntm.SettleDate);
		AddWireValue(values, _ntmPrefix + nameof(NtmOrderInfo.MatchRef), ntm.MatchRef);
		AddWireValue(values, _ntmPrefix + nameof(NtmOrderInfo.SettleCode), ntm.SettleCode);
		AddWireValue(values, _ntmPrefix + nameof(NtmOrderInfo.ForAccount), ntm.ForAccount);
		AddWireValue(values, _ntmPrefix + nameof(NtmOrderInfo.CurrencyType), ((int)ntm.CurrencyType).ToString(CultureInfo.InvariantCulture));

		return string.Join(';', values);
	}

	/// <inheritdoc />
	public void FromWire(string payload)
	{
		if (payload.IsEmpty())
			return;

		var fields = payload.Split(';');

		if (!fields[0].Equals(_wireVersion, StringComparison.Ordinal))
			throw new ArgumentOutOfRangeException(nameof(payload), payload, LocalizedStrings.InvalidValue);

		foreach (var field in fields.Skip(1))
		{
			var separatorIndex = field.IndexOf('=');

			if (separatorIndex <= 0)
				continue;

			var key = field[..separatorIndex];
			var value = UnescapeWireValue(field[(separatorIndex + 1)..]);

			switch (key)
			{
				case nameof(Type):
					Type = (QuikOrderConditionTypes)value.To<int>();
					break;
				case nameof(OtherSecurityId):
					OtherSecurityId = value.ToSecurityId();
					break;
				case nameof(StopPriceCondition):
					StopPriceCondition = (QuikStopPriceConditions)value.To<int>();
					break;
				case nameof(StopPrice):
					StopPrice = value.To<decimal>();
					break;
				case nameof(StopLimitPrice):
					StopLimitPrice = value.To<decimal>();
					break;
				case nameof(IsMarketStopLimit):
					IsMarketStopLimit = value.To<bool>();
					break;
				case _activeTimeFromKey:
				{
					var activeTime = ActiveTime;
					ActiveTime = (value.To<long>().FromUnixMcs(), activeTime.to);
					break;
				}
				case _activeTimeToKey:
				{
					var activeTime = ActiveTime;
					ActiveTime = (activeTime.from, value.To<long>().FromUnixMcs());
					break;
				}
				case nameof(ConditionOrderId):
					ConditionOrderId = value.To<long>();
					break;
				case nameof(ConditionOrderSide):
					ConditionOrderSide = (Sides)value.To<int>();
					break;
				case nameof(ConditionOrderPartiallyMatched):
					ConditionOrderPartiallyMatched = value.To<bool>();
					break;
				case nameof(ConditionOrderUseMatchedBalance):
					ConditionOrderUseMatchedBalance = value.To<bool>();
					break;
				case nameof(LinkedOrderPrice):
					LinkedOrderPrice = value.To<decimal>();
					break;
				case nameof(LinkedOrderCancel):
					LinkedOrderCancel = value.To<bool>();
					break;
				case nameof(Offset):
					Offset = value.ToUnit();
					break;
				case nameof(Spread):
					Spread = value.ToUnit();
					break;
				case nameof(IsMarketTakeProfit):
					IsMarketTakeProfit = value.To<bool>();
					break;
				case nameof(IsRepo):
					IsRepo = value.To<bool>();
					break;
				case _repoPrefix + nameof(RepoOrderInfo.Partner):
					RepoInfo.Partner = value;
					break;
				case _repoPrefix + nameof(RepoOrderInfo.Term):
					RepoInfo.Term = value.To<decimal>();
					break;
				case _repoPrefix + nameof(RepoOrderInfo.Rate):
					RepoInfo.Rate = value.To<decimal>();
					break;
				case _repoPrefix + nameof(RepoOrderInfo.BlockSecurities):
					RepoInfo.BlockSecurities = value.To<bool>();
					break;
				case _repoPrefix + nameof(RepoOrderInfo.RefundRate):
					RepoInfo.RefundRate = value.To<decimal>();
					break;
				case _repoPrefix + nameof(RepoOrderInfo.MatchRef):
					RepoInfo.MatchRef = value;
					break;
				case _repoPrefix + nameof(RepoOrderInfo.SettleCode):
					RepoInfo.SettleCode = value;
					break;
				case _repoPrefix + nameof(RepoOrderInfo.SecondPrice):
					RepoInfo.SecondPrice = value.To<decimal>();
					break;
				case _repoPrefix + nameof(RepoOrderInfo.SettleDate):
					RepoInfo.SettleDate = value.To<long>().FromUnixMcs();
					break;
				case _repoPrefix + nameof(RepoOrderInfo.StartDiscount):
					RepoInfo.StartDiscount = value.To<decimal>();
					break;
				case _repoPrefix + nameof(RepoOrderInfo.LowerDiscount):
					RepoInfo.LowerDiscount = value.To<decimal>();
					break;
				case _repoPrefix + nameof(RepoOrderInfo.UpperDiscount):
					RepoInfo.UpperDiscount = value.To<decimal>();
					break;
				case _repoPrefix + nameof(RepoOrderInfo.Value):
					RepoInfo.Value = value.To<decimal>();
					break;
				case _repoPrefix + nameof(RepoOrderInfo.IsModified):
					RepoInfo.IsModified = value.To<bool>();
					break;
				case nameof(IsNtm):
					IsNtm = value.To<bool>();
					break;
				case _ntmPrefix + nameof(NtmOrderInfo.Partner):
					NtmInfo.Partner = value;
					break;
				case _ntmPrefix + nameof(NtmOrderInfo.SettleDate):
					NtmInfo.SettleDate = value.To<long>().FromUnixMcs();
					break;
				case _ntmPrefix + nameof(NtmOrderInfo.MatchRef):
					NtmInfo.MatchRef = value;
					break;
				case _ntmPrefix + nameof(NtmOrderInfo.SettleCode):
					NtmInfo.SettleCode = value;
					break;
				case _ntmPrefix + nameof(NtmOrderInfo.ForAccount):
					NtmInfo.ForAccount = value;
					break;
				case _ntmPrefix + nameof(NtmOrderInfo.CurrencyType):
					NtmInfo.CurrencyType = (CurrencyTypes)value.To<int>();
					break;
			}
		}
	}

	private static void AddWireValue(ICollection<string> values, string key, string value)
	{
		if (value is not null)
			values.Add($"{key}={EscapeWireValue(value)}");
	}

	private static void AddWireValue(ICollection<string> values, string key, decimal? value)
	{
		if (value is not null)
			AddWireValue(values, key, value.Value.ToString(CultureInfo.InvariantCulture));
	}

	private static void AddWireValue(ICollection<string> values, string key, long? value)
	{
		if (value is not null)
			AddWireValue(values, key, value.Value.ToString(CultureInfo.InvariantCulture));
	}

	private static void AddWireValue(ICollection<string> values, string key, bool? value)
	{
		if (value is not null)
			AddWireValue(values, key, value.Value.ToString());
	}

	private static void AddWireValue(ICollection<string> values, string key, DateTime? value)
	{
		if (value is not null)
			AddWireValue(values, key, value.Value.ToUnixMcs().ToString(CultureInfo.InvariantCulture));
	}

	private static string EscapeWireValue(string value)
		=> value
			.Replace("%", "%25", StringComparison.Ordinal)
			.Replace(";", "%3B", StringComparison.Ordinal)
			.Replace("=", "%3D", StringComparison.Ordinal);

	private static string UnescapeWireValue(string value)
		=> value
			.Replace("%3D", "=", StringComparison.Ordinal)
			.Replace("%3B", ";", StringComparison.Ordinal)
			.Replace("%25", "%", StringComparison.Ordinal);
}
