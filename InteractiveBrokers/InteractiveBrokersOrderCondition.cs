namespace StockSharp.InteractiveBrokers;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// <see cref="InteractiveBrokers"/> order condition.
/// </summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.InteractiveBrokersKey)]
public class InteractiveBrokersOrderCondition : OrderCondition, IStopLossOrderCondition
{
	/// <summary>
	/// 
	/// </summary>
	public abstract class ExtraOrderCondition
	{
		private class TimeCondition : ExtraOrderCondition
		{
			public override PeggedOrderConditionTypes Type => PeggedOrderConditionTypes.Time;
		}

		private class VolumeCondition : ExtraOrderCondition
		{
			public override PeggedOrderConditionTypes Type => PeggedOrderConditionTypes.Volume;
		}

		/// <summary>
		/// Type.
		/// </summary>
		public abstract PeggedOrderConditionTypes Type { get; }

		/// <summary>
		/// 
		/// </summary>
		public bool IsConjunctionConnection { get; set; }

		private ExtraOrderCondition()
		{
		}

		/// <summary>
		/// Create extra condition.
		/// </summary>
		/// <param name="type">Type.</param>
		/// <returns></returns>
		public static ExtraOrderCondition Create(PeggedOrderConditionTypes type)
		{
			ExtraOrderCondition condition;

			switch (type)
			{
				//case PeggedOrderConditionTypes.Execution:
				//	condition = new ExecutionCondition();
				//	break;

				//case PeggedOrderConditionTypes.Margin:
				//	condition = new MarginCondition();
				//	break;

				//case PeggedOrderConditionTypes.PercentChange:
				//	condition = new PercentChangeCondition();
				//	break;

				//case PeggedOrderConditionTypes.Price:
				//	condition = new PriceCondition();
				//	break;

				case PeggedOrderConditionTypes.Time:
					condition = new TimeCondition();
					break;

				case PeggedOrderConditionTypes.Volume:
					condition = new VolumeCondition();
					break;

				default:
					throw new ArgumentOutOfRangeException();
			}

			return condition;
		}
	}

	/// <summary>
	/// 
	/// </summary>
	public enum PeggedOrderConditionTypes
	{
		/// <summary>
		/// Price.
		/// </summary>
		Price = 1,

		/// <summary>
		/// Time.
		/// </summary>
		Time = 3,

		/// <summary>
		/// Margin.
		/// </summary>
		Margin = 4,

		/// <summary>
		/// Execution.
		/// </summary>
		Execution = 5,

		/// <summary>
		/// Used with conditional orders to submit or cancel an order based on a specified volume change in a security.
		/// </summary>
		Volume = 6,

		/// <summary>
		/// Percent change.
		/// </summary>
		PercentChange = 7
	}

	/// <summary>
	/// Base condition.
	/// </summary>
	public abstract class BaseCondition
	{
		private readonly InteractiveBrokersOrderCondition _condition;

		internal BaseCondition(InteractiveBrokersOrderCondition condition)
		{
			_condition = condition ?? throw new ArgumentNullException(nameof(condition));
		}

		/// <summary>
		/// Get parameter value.
		/// </summary>
		/// <typeparam name="T">Value type.</typeparam>
		/// <param name="name">Parameter name.</param>
		/// <returns>The parameter value.</returns>
		protected T GetValue<T>(string name)
		{
			if (!_condition.Parameters.ContainsKey(name))
				throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);

			return (T)_condition.Parameters[name];
		}

		/// <summary>
		/// To get the parameter value. If the value does not exist the <see langword="null" /> will be returned.
		/// </summary>
		/// <typeparam name="T">Value type.</typeparam>
		/// <param name="name">Parameter name.</param>
		/// <returns>The parameter value.</returns>
		protected T TryGetValue<T>(string name)
		{
			return (T)_condition.Parameters.TryGetValue(name);
		}

		/// <summary>
		/// To set a new parameter value.
		/// </summary>
		/// <typeparam name="T">Value type.</typeparam>
		/// <param name="name">Parameter name.</param>
		/// <param name="value">The parameter value.</param>
		protected void SetValue<T>(string name, T value)
		{
			_condition.Parameters[name] = value;
		}
	}

	/// <summary>
	/// Extended orders types which are specific to <see cref="InteractiveBrokersMessageAdapter"/>.
	/// </summary>
	public enum ExtendedOrderTypes
	{
		/// <summary>
		/// To match at the market price, if the closing price is higher than the expected price.
		/// </summary>
		/// <remarks>
		/// Not valid for US <see cref="SecurityTypes.Future"/>, US <see cref="SecurityTypes.Option"/>, <see cref="SecurityTypes.Stock"/>.
		/// </remarks>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.MarketOnCloseKey)]
		MarketOnClose,

		/// <summary>
		/// To match at the specified price, if the closing price is higher than the expected price.
		/// </summary>
		/// <remarks>
		/// Not valid for US <see cref="SecurityTypes.Future"/>, <see cref="SecurityTypes.Stock"/>.
		/// </remarks>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.LimitOnCloseKey)]
		LimitOnClose,

		/// <summary>
		/// At best price.
		/// </summary>
		/// <remarks>
		/// Valid until <see cref="SecurityTypes.Stock"/>.
		/// </remarks>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.AtBestPriceKey)]
		PeggedToMarket,

		/// <summary>
		/// The stop with the market activation price.
		/// </summary>
		/// <remarks>
		/// Valid for <see cref="SecurityTypes.Currency"/>, <see cref="SecurityTypes.Future"/>, <see cref="SecurityTypes.Option"/>, <see cref="SecurityTypes.Stock"/>, <see cref="SecurityTypes.Warrant"/>.
		/// </remarks>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.StopOrderTypeKey)]
		Stop,

		/// <summary>
		/// Stop with the specified activation price.
		/// </summary>
		/// <remarks>
		/// Valid for <see cref="SecurityTypes.Currency"/>, <see cref="SecurityTypes.Future"/>, <see cref="SecurityTypes.Option"/>, <see cref="SecurityTypes.Stock"/>.
		/// </remarks>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.StopLimitKey)]
		StopLimit,

		/// <summary>
		/// Trailing stop-loss.
		/// </summary>
		/// <remarks>
		/// Valid for <see cref="SecurityTypes.Currency"/>, <see cref="SecurityTypes.Future"/>, <see cref="SecurityTypes.Option"/>, <see cref="SecurityTypes.Stock"/>, <see cref="SecurityTypes.Warrant"/>.
		/// </remarks>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.TrailingKey)]
		TrailingStop,

		/// <summary>
		/// With offset.
		/// </summary>
		/// <remarks>
		/// Valid for <see cref="SecurityTypes.Option"/>, <see cref="SecurityTypes.Stock"/>.
		/// </remarks>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.WithOffsetKey)]
		Relative,

		/// <summary>
		/// VWAP.
		/// </summary>
		/// <remarks>
		/// Valid until <see cref="SecurityTypes.Stock"/>.
		/// </remarks>
		[Display(
			Name = "VWAP")]
		VolumeWeightedAveragePrice,

		/// <summary>
		/// Limit trailing stop.
		/// </summary>
		/// <remarks>
		/// Valid for <see cref="SecurityTypes.Currency"/>, <see cref="SecurityTypes.Future"/>, <see cref="SecurityTypes.Option"/>, <see cref="SecurityTypes.Stock"/>, <see cref="SecurityTypes.Warrant"/>.
		/// </remarks>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.TrailingStopLimitKey)]
		TrailingStopLimit,

		/// <summary>
		/// Volatility.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.VolatilityKey)]
		Volatility,

		/// <summary>
		/// It used for delta orders.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.NoneKey)]
		None,

		/// <summary>
		/// It used for delta neutral orders types.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.ByDefaultKey)]
		Default,

		/// <summary>
		/// To be changed on price increment.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.VariableKey)]
		Scale,

		/// <summary>
		/// With the market price when the condition is fulfilled.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.MarketOnTouchKey)]
		MarketIfTouched,

		/// <summary>
		/// With the specified price when the condition is fulfilled.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.LimitOnTouchKey)]
		LimitIfTouched,

		/// <summary>
		/// 
		/// </summary>
		PeggedBench,

		/// <summary>
		/// 
		/// </summary>
		PeggedMid
	}

	/// <summary>
	/// Orders modes such as OCA (One-Cancels All).
	/// </summary>
	public enum OcaTypes
	{
		/// <summary>
		/// To cancel all remaining blocks.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.CancelAllKey)]
		CancelAll = 1,

		/// <summary>
		/// The remaining orders proportionally to decrease by the size of the block.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.ProportionKey)]
		ReduceWithBlock = 2,

		/// <summary>
		/// The remaining orders proportionally to decrease by the size out of the block.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.Proportion2Key)]
		ReduceWithNoBlock = 3
	}

	/// <summary>
	/// OCA (One-Cancels All) settings.
	/// </summary>
	public class OcaCondition : BaseCondition
	{
		private const string _prefix = "Oca";

		internal OcaCondition(InteractiveBrokersOrderCondition condition)
			: base(condition)
		{
		}

		/// <summary>
		/// Group ID.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.GroupIdKey,
			Description = LocalizedStrings.GroupIdKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public string Group
		{
			get => TryGetValue<string>(_prefix + nameof(Group));
			set => SetValue(_prefix + nameof(Group), value);
		}

		/// <summary>
		/// Group type.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.GroupTypeKey,
			Description = LocalizedStrings.GroupTypeKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public OcaTypes? Type
		{
			get => TryGetValue<OcaTypes?>(_prefix + nameof(Type));
			set => SetValue(_prefix + nameof(Type), value);
		}
	}

	/// <summary>
	/// Conditions for stop orders activation.
	/// </summary>
	public enum TriggerMethods
	{
		/// <summary>
		/// For NASDAQ <see cref="SecurityTypes.Stock"/> and US <see cref="SecurityTypes.Option"/> the <see cref="TriggerMethods.DoubleBidAsk"/> condition is used. Otherwise, the <see cref="TriggerMethods.BidAsk"/> condition is used.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.ByDefaultKey)]
		Default = 0,

		/// <summary>
		/// Double increase or decrease of the current best price before the stop price.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.DoubleBidAskKey)]
		DoubleBidAsk = 1,

		/// <summary>
		/// Increase or decrease of the last trade price before the stop price.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.LastKey)]
		Last = 2,

		/// <summary>
		/// Double increase or decrease of the last trade price before the stop price.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.DoubleLastKey)]
		DoubleLast = 3,

		/// <summary>
		/// Increase or decrease of the current best price before the stop price.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.QuoteKey)]
		BidAsk = 4,

		/// <summary>
		/// Increase or decrease of the current best price or the last trade price before the stop price.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.AnyKey)]
		LastOrBidAsk = 7,

		/// <summary>
		/// Increase or decrease of the mid-spread before the stop price.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.SpreadKey)]
		MidpointMethod = 8
	}

	/// <summary>
	/// Descriptions of trader type by the 80A rule.
	/// </summary>
	public enum AgentDescriptions
	{
		/// <summary>
		/// Private trader.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.PrivateKey)]
		[NativeValue("I")]
		Individual,

		/// <summary>
		/// Agency.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.AgencyKey)]
		[NativeValue("A")]
		Agency,

		/// <summary>
		/// Agency of other type.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.AgentOtherMemberKey)]
		[NativeValue("W")]
		AgentOtherMember,

		/// <summary>
		/// Individual PTIA.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.IndividualPTIAKey)]
		[NativeValue("J")]
		IndividualPTIA,

		/// <summary>
		/// Agency PTIA.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.AgencyPTIAKey)]
		[NativeValue("U")]
		AgencyPTIA,

		/// <summary>
		/// Agency of other type PTIA.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.AgentOtherMemberPTIAKey)]
		[NativeValue("M")]
		AgentOtherMemberPTIA,

		/// <summary>
		/// Individual PT.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.IndividualPTKey)]
		[NativeValue("K")]
		IndividualPT,

		/// <summary>
		/// Agency PT.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.AgencyPTKey)]
		[NativeValue("Y")]
		AgencyPT,

		/// <summary>
		/// Agency of other type PT.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.AgentOtherMemberPTKey)]
		[NativeValue("N")]
		AgentOtherMemberPT,
	}

	/// <summary>
	/// Methods for volumes automatic calculation for the accounts group.
	/// </summary>
	public enum FinancialAdvisorAllocations
	{
		/// <summary>
		/// Percentage change.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.PercentKey)]
		[NativeValue("PctChange")]
		PercentChange,

		/// <summary>
		/// Using free cash plus borrowed.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.EquityKey)]
		[NativeValue("AvailableEquity")]
		AvailableEquity,

		/// <summary>
		/// Using free cash.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.LiquidityKey)]
		[NativeValue("NetLiq")]
		NetLiquidity,

		/// <summary>
		/// An equal volume.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.VolumeKey)]
		[NativeValue("EqualQuantity")]
		EqualQuantity,
	}

	/// <summary>
	/// Settings for automatic order volume calculation.
	/// </summary>
	public class FinancialAdvisorCondition : BaseCondition
	{
		private const string _prefix = "FA";

		internal FinancialAdvisorCondition(InteractiveBrokersOrderCondition condition)
			: base(condition)
		{
		}

		/// <summary>
		/// Group.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.GroupKey,
			Description = LocalizedStrings.GroupKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public string Group
		{
			get => TryGetValue<string>(_prefix + nameof(Group));
			set => SetValue(_prefix + nameof(Group), value);
		}

		/// <summary>
		/// Calculation method.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.CalcMethodKey,
			Description = LocalizedStrings.CalcMethodKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public FinancialAdvisorAllocations? Allocation
		{
			get => TryGetValue<FinancialAdvisorAllocations?>(_prefix + nameof(Allocation));
			set => SetValue(_prefix + nameof(Allocation), value);
		}

		/// <summary>
		/// Ration percentage to filled volume.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.RationPercentageKey,
			Description = LocalizedStrings.RationPercentageKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public string Percentage
		{
			get => TryGetValue<string>(_prefix + nameof(Percentage));
			set => SetValue(_prefix + nameof(Percentage), value);
		}
	}

	/// <summary>
	/// Senders.
	/// </summary>
	public enum OrderOrigins
	{
		/// <summary>
		/// Client.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.ClientKey)]
		Customer,

		/// <summary>
		/// Firm.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.FirmKey)]
		Firm
	}

	/// <summary>
	/// Trading.
	/// </summary>
	public enum AuctionStrategies
	{
		/// <summary>
		/// Match.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.MatchKey)]
		AuctionMatch,

		/// <summary>
		/// Better.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.BetterKey)]
		AuctionImprovement,

		/// <summary>
		/// Transparent.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.TransparentKey)]
		AuctionTransparent
	}

	/// <summary>
	/// Volatility timeframes.
	/// </summary>
	public enum VolatilityTimeFrames
	{
		/// <summary>
		/// Daily.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.DailyKey)]
		Daily = 1,

		/// <summary>
		/// Average annual.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.AnnualKey)]
		Annual = 2
	}

	/// <summary>
	/// The settings for the orders type <see cref="ExtendedOrderTypes.Volatility"/>.
	/// </summary>
	public class VolatilityCondition : BaseCondition
	{
		private const string _prefix = "DeltaNeutral";

		internal VolatilityCondition(InteractiveBrokersOrderCondition condition)
			: base(condition)
		{
			ShortSale = new ShortSaleCondition(condition, _prefix + "ShortSale");
		}

		/// <summary>
		/// Refresh limit price if underlying asset price has changed.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.RefreshLimitPriceKey,
			Description = LocalizedStrings.RefreshLimitPriceDescKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public bool? ContinuousUpdate
		{
			get => TryGetValue<bool?>(_prefix + nameof(ContinuousUpdate));
			set => SetValue(_prefix + nameof(ContinuousUpdate), value);
		}

		/// <summary>
		/// Average best price or best price.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.AverageBestPriceKey,
			Description = LocalizedStrings.AverageBestPriceDescKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public bool? IsAverageBestPrice
		{
			get => TryGetValue<bool?>(_prefix + nameof(IsAverageBestPrice));
			set => SetValue(_prefix + nameof(IsAverageBestPrice), value);
		}

		/// <summary>
		/// Volatility.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.VolatilityKey,
			Description = LocalizedStrings.VolatilityKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public decimal? Volatility
		{
			get => TryGetValue<decimal?>(_prefix + nameof(Volatility));
			set => SetValue(_prefix + nameof(Volatility), value);
		}

		/// <summary>
		/// Volatility time-frame.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.VolatilityTimeFrameKey,
			Description = LocalizedStrings.VolatilityTimeFrameDescKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public VolatilityTimeFrames? VolatilityTimeFrame
		{
			get => TryGetValue<VolatilityTimeFrames?>(_prefix + nameof(VolatilityTimeFrame));
			set => SetValue(_prefix + nameof(VolatilityTimeFrame), value);
		}

		/// <summary>
		/// Order type.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.OrderTypeKey,
			Description = LocalizedStrings.OrderTypeKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public OrderTypes? OrderType
		{
			get => TryGetValue<OrderTypes?>(_prefix + nameof(OrderType));
			set => SetValue(_prefix + nameof(OrderType), value);
		}

		/// <summary>
		/// Extended type of order.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.ExtendedOrderTypeKey,
			Description = LocalizedStrings.ExtendedOrderTypeDescKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public ExtendedOrderTypes? ExtendedOrderType
		{
			get => TryGetValue<ExtendedOrderTypes?>(_prefix + nameof(ExtendedOrderType));
			set => SetValue(_prefix + nameof(ExtendedOrderType), value);
		}

		/// <summary>
		/// Stop-price.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.StopPriceKey,
			Description = LocalizedStrings.StopPriceKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public decimal? StopPrice
		{
			get => TryGetValue<decimal?>(_prefix + nameof(StopPrice));
			set => SetValue(_prefix + nameof(StopPrice), value);
		}

		/// <summary>
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.ContractKey,
			Description = LocalizedStrings.ContractKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public int? ContractId
		{
			get => TryGetValue<int?>(_prefix + nameof(ContractId));
			set => SetValue(_prefix + nameof(ContractId), value);
		}

		/// <summary>
		/// Firm.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.FirmKey,
			Description = LocalizedStrings.FirmKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public string SettlingFirm
		{
			get => TryGetValue<string>(_prefix + nameof(SettlingFirm));
			set => SetValue(_prefix + nameof(SettlingFirm), value);
		}

		/// <summary>
		/// Clearing account.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.ClearingAccKey,
			Description = LocalizedStrings.ClearingAccKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public string ClearingPortfolio
		{
			get => TryGetValue<string>(_prefix + nameof(ClearingPortfolio));
			set => SetValue(_prefix + nameof(ClearingPortfolio), value);
		}

		/// <summary>
		/// Clearing chain.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.ClearingChainKey,
			Description = LocalizedStrings.ClearingChainKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public string ClearingIntent
		{
			get => TryGetValue<string>(_prefix + nameof(ClearingIntent));
			set => SetValue(_prefix + nameof(ClearingIntent), value);
		}

		/// <summary>
		/// Is the order a short sell.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.EnabledKey,
			Description = LocalizedStrings.ShortSaleDescKey,
			GroupName = LocalizedStrings.ShortSaleKey)]
		public bool? IsShortSale
		{
			get => TryGetValue<bool?>(_prefix + nameof(IsShortSale));
			set => SetValue(_prefix + nameof(IsShortSale), value);
		}

		/// <summary>
		/// Condition for short sales of combined legs.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.ExtraConditionsKey,
			Description = LocalizedStrings.ShortSaleConditionsKey,
			GroupName = LocalizedStrings.ShortSaleKey)]
		public ShortSaleCondition ShortSale { get; }
	}

	/// <summary>
	/// Short sales types of combined legs.
	/// </summary>
	public enum ShortSaleSlots
	{
		/// <summary>
		/// Private trader or not short leg.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.NoneKey)]
		Unapplicable,

		/// <summary>
		/// Clearing broker.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.ClearingKey)]
		ClearingBroker,

		/// <summary>
		/// Other.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.OtherKey)]
		ThirdParty
	}

	/// <summary>
	/// Condition for short sales of combined legs.
	/// </summary>
	public class ShortSaleCondition : BaseCondition
	{
		private readonly string _prefix;

		internal ShortSaleCondition(InteractiveBrokersOrderCondition condition, string prefix)
			: base(condition)
		{
			_prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));

			Slot = ShortSaleSlots.Unapplicable;
			ExemptCode = 0;
		}

		/// <summary>
		/// Short sale type of combined legs.
		/// </summary>
		public ShortSaleSlots Slot
		{
			get => GetValue<ShortSaleSlots>(_prefix + nameof(Slot));
			set => SetValue(_prefix + nameof(Slot), value);
		}

		/// <summary>
		/// Clarification of the short sale type of combined legs.
		/// </summary>
		/// <remarks>
		/// Used when <see cref="Slot"/> equals to <see cref="ShortSaleSlots.ThirdParty"/>.
		/// </remarks>
		public string Location
		{
			get => TryGetValue<string>(_prefix + nameof(Location));
			set => SetValue(_prefix + nameof(Location), value);
		}

		/// <summary>
		/// Exempt Code for Short Sale Exemption Orders.
		/// </summary>
		public int ExemptCode
		{
			get => GetValue<int>(_prefix + nameof(ExemptCode));
			set => SetValue(_prefix + nameof(ExemptCode), value);
		}

		/// <summary>
		/// Is the order opening or closing.
		/// </summary>
		public bool? IsOpenOrClose
		{
			get => TryGetValue<bool?>(_prefix + nameof(IsOpenOrClose));
			set => SetValue(_prefix + nameof(IsOpenOrClose), value);
		}
	}

	/// <summary>
	/// EFP orders settings.
	/// </summary>
	public class ComboCondition : BaseCondition
	{
		private const string _prefix = "Combo";

		internal ComboCondition(InteractiveBrokersOrderCondition condition)
			: base(condition)
		{
		}

		/// <summary>
		/// Basic points.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.BasisPointsKey,
			Description = LocalizedStrings.BasisPointsKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public decimal? BasisPoints
		{
			get => TryGetValue<decimal?>(_prefix + nameof(BasisPoints));
			set => SetValue(_prefix + nameof(BasisPoints), value);
		}

		/// <summary>
		/// Base points type.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.BasisPointsKey,
			Description = LocalizedStrings.BasisPointsKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public int? BasisPointsType
		{
			get => TryGetValue<int?>(_prefix + nameof(BasisPointsType));
			set => SetValue(_prefix + nameof(BasisPointsType), value);
		}

		/// <summary>
		/// Legs description.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.LegsKey,
			Description = LocalizedStrings.LegsDescKey,
			GroupName = LocalizedStrings.LegsKey)]
		public string LegsDescription
		{
			get => TryGetValue<string>(_prefix + nameof(LegsDescription));
			set => SetValue(_prefix + nameof(LegsDescription), value);
		}

		/// <summary>
		/// Legs prices.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.LegsPricesKey,
			Description = LocalizedStrings.LegsPricesKey,
			GroupName = LocalizedStrings.LegsKey)]
		public IEnumerable<decimal?> Legs
		{
			get => TryGetValue<IEnumerable<decimal?>>(_prefix + nameof(Legs));
			set => SetValue(_prefix + nameof(Legs), value);
		}

		/// <summary>
		/// Condition for short sales.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.ExtraConditionsKey,
			Description = LocalizedStrings.ShortSaleConditionsKey,
			GroupName = LocalizedStrings.ShortSaleKey)]
		public IDictionary<SecurityId, ShortSaleCondition> ShortSales
		{
			get => TryGetValue<IDictionary<SecurityId, ShortSaleCondition>>(_prefix + nameof(ShortSales));
			set => SetValue(_prefix + nameof(ShortSales), value);
		}
	}

	/// <summary>
	/// Settings for orders that are sent to the Smart exchange.
	/// </summary>
	public class SmartRoutingCondition : BaseCondition
	{
		private const string _prefix = "SmartRouting";

		internal SmartRoutingCondition(InteractiveBrokersOrderCondition condition)
			: base(condition)
		{
		}

		/// <summary>
		/// Order price shift range.
		/// </summary>
		public decimal? DiscretionaryAmount
		{
			get => TryGetValue<decimal?>(_prefix + nameof(DiscretionaryAmount));
			set => SetValue(_prefix + nameof(DiscretionaryAmount), value);
		}

		/// <summary>
		/// Keep in market depth.
		/// </summary>
		/// <remarks>
		/// Only for the IBDARK exchange.
		/// </remarks>
		public bool? NotHeld
		{
			get => TryGetValue<bool?>(_prefix + nameof(NotHeld));
			set => SetValue(_prefix + nameof(NotHeld), value);
		}

		/// <summary>
		/// Direct sending of ASX orders.
		/// </summary>
		public bool? OptOutSmartRouting
		{
			get => TryGetValue<bool?>(_prefix + nameof(OptOutSmartRouting));
			set => SetValue(_prefix + nameof(OptOutSmartRouting), value);
		}

		/// <summary>
		/// Parameters.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.ParametersKey,
			Description = LocalizedStrings.ParametersKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public IEnumerable<Tuple<string, string>> ComboParams
		{
			get => TryGetValue<IEnumerable<Tuple<string, string>>>(_prefix + nameof(ComboParams)) ?? [];
			set => SetValue(_prefix + nameof(ComboParams), value);
		}
	}

	/// <summary>
	/// Condition for order being changed.
	/// </summary>
	public class ScaleCondition : BaseCondition
	{
		private const string _prefix = "Scale";

		internal ScaleCondition(InteractiveBrokersOrderCondition condition)
			: base(condition)
		{
		}

		/// <summary>
		/// Split order into X buckets.
		/// </summary>
		public int? InitLevelSize
		{
			get => TryGetValue<int?>(_prefix + nameof(InitLevelSize));
			set => SetValue(_prefix + nameof(InitLevelSize), value);
		}

		/// <summary>
		/// Split order so each bucket is of the size X.
		/// </summary>
		public int? SubsLevelSize
		{
			get => TryGetValue<int?>(_prefix + nameof(SubsLevelSize));
			set => SetValue(_prefix + nameof(SubsLevelSize), value);
		}

		/// <summary>
		/// Price increment per bucket.
		/// </summary>
		public decimal? PriceIncrement
		{
			get => TryGetValue<decimal?>(_prefix + nameof(PriceIncrement));
			set => SetValue(_prefix + nameof(PriceIncrement), value);
		}

		/// <summary>
		/// </summary>
		public decimal? PriceAdjustValue
		{
			get => TryGetValue<decimal?>(_prefix + nameof(PriceAdjustValue));
			set => SetValue(_prefix + nameof(PriceAdjustValue), value);
		}

		/// <summary>
		/// </summary>
		public int? PriceAdjustInterval
		{
			get => TryGetValue<int>(_prefix + nameof(PriceAdjustInterval));
			set => SetValue(_prefix + nameof(PriceAdjustInterval), value);
		}

		/// <summary>
		/// </summary>
		public decimal? ProfitOffset
		{
			get => TryGetValue<decimal?>(_prefix + nameof(ProfitOffset));
			set => SetValue(_prefix + nameof(ProfitOffset), value);
		}

		/// <summary>
		/// </summary>
		public bool? AutoReset
		{
			get => TryGetValue<bool?>(_prefix + nameof(AutoReset));
			set => SetValue(_prefix + nameof(AutoReset), value);
		}

		/// <summary>
		/// </summary>
		public int? InitPosition
		{
			get => TryGetValue<int?>(_prefix + nameof(InitPosition));
			set => SetValue(_prefix + nameof(InitPosition), value);
		}

		/// <summary>
		/// </summary>
		public int? InitFillQty
		{
			get => TryGetValue<int?>(_prefix + nameof(InitFillQty));
			set => SetValue(_prefix + nameof(InitFillQty), value);
		}

		/// <summary>
		/// </summary>
		public bool? RandomPercent
		{
			get => TryGetValue<bool?>(_prefix + nameof(RandomPercent));
			set => SetValue(_prefix + nameof(RandomPercent), value);
		}

		/// <summary>
		/// </summary>
		public string Table
		{
			get => TryGetValue<string>(_prefix + nameof(Table));
			set => SetValue(_prefix + nameof(Table), value);
		}
	}

	/// <summary>
	/// Parameters types for hedging.
	/// </summary>
	public enum HedgeTypes
	{
		/// <summary>
		/// Delta.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.DeltaKey)]
		Delta,

		/// <summary>
		/// Beta.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.BetaKey)]
		Beta,

		/// <summary>
		/// Currency.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.CurrencyKey)]
		FX,

		/// <summary>
		/// Pair.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.PairKey)]
		Pair
	}

	/// <summary>
	/// Condition for hedge-orders.
	/// </summary>
	public class HedgeCondition : BaseCondition
	{
		private const string _prefix = "Hedge";

		internal HedgeCondition(InteractiveBrokersOrderCondition condition)
			: base(condition)
		{
		}

		/// <summary>
		/// Parameter type for hedging.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.ParameterTypeKey,
			Description = LocalizedStrings.ParameterTypeKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public HedgeTypes? Type
		{
			get => TryGetValue<HedgeTypes?>(_prefix + nameof(Type));
			set => SetValue(_prefix + nameof(Type), value);
		}

		/// <summary>
		/// Parameter.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.ParameterKey,
			Description = LocalizedStrings.ParameterKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public string Param
		{
			get => TryGetValue<string>(_prefix + nameof(Param));
			set => SetValue(_prefix + nameof(Param), value);
		} 
	}

	/// <summary>
	/// Condition for algo-orders.
	/// </summary>
	public class AlgoCondition : BaseCondition
	{
		private const string _prefix = "Algo";

		internal AlgoCondition(InteractiveBrokersOrderCondition condition)
			: base(condition)
		{
		}

		/// <summary>
		/// Strategy.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.StrategyKey,
			Description = LocalizedStrings.StrategyKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public string Strategy
		{
			get => TryGetValue<string>(_prefix + nameof(Strategy));
			set => SetValue(_prefix + nameof(Strategy), value);
		}

		/// <summary>
		/// Parameters.
		/// </summary>
		public IEnumerable<Tuple<string, string>> Params
		{
			get => TryGetValue<IEnumerable<Tuple<string, string>>>(_prefix + nameof(Params)) ?? [];
			set => SetValue(_prefix + nameof(Params), value);
		}
	}

	/// <summary>
	/// Clearing objectives.
	/// </summary>
	public enum ClearingIntents
	{
		/// <summary>
		/// Broker.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.BrokerKey)]
		[NativeValue("IB")]
		Broker,

		/// <summary>
		/// Other.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.OtherKey)]
		[NativeValue("AWAY")]
		Away,

		/// <summary>
		/// Post-trading placement.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.PostTradeKey)]
		[NativeValue("PTA")]
		PostTradeAllocation
	}

	/// <summary>
	/// Condition for clearing information.
	/// </summary>
	/// <remarks>
	/// Only for institutional clients.
	/// </remarks>
	public class ClearingCondition : BaseCondition
	{
		private const string _prefix = "Clearing";

		internal ClearingCondition(InteractiveBrokersOrderCondition condition)
			: base(condition)
		{
		}

		/// <summary>
		/// Account.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.AccountKey,
			Description = LocalizedStrings.AccountKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public string Portfolio
		{
			get => TryGetValue<string>(_prefix + nameof(Portfolio));
			set => SetValue(_prefix + nameof(Portfolio), value);
		}

		/// <summary>
		/// Firm.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.FirmKey,
			Description = LocalizedStrings.FirmKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public string SettlingFirm
		{
			get => TryGetValue<string>(_prefix + nameof(SettlingFirm));
			set => SetValue(_prefix + nameof(SettlingFirm), value);
		}

		/// <summary>
		/// Clearing account.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.ClearingAccKey,
			Description = LocalizedStrings.ClearingAccKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public string ClearingPortfolio
		{
			get => TryGetValue<string>(_prefix + nameof(ClearingPortfolio));
			set => SetValue(_prefix + nameof(ClearingPortfolio), value);
		}

		/// <summary>
		/// Aim of clearing.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.IntentKey,
			Description = LocalizedStrings.IntentKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public ClearingIntents? Intent
		{
			get => TryGetValue<ClearingIntents?>(_prefix + nameof(Intent));
			set => SetValue(_prefix + nameof(Intent), value);
		}
	}

	/// <summary>
	/// The condition for GTC orders.
	/// </summary>
	public class ActiveCondition : BaseCondition
	{
		private const string _prefix = "Active";

		internal ActiveCondition(InteractiveBrokersOrderCondition condition)
			: base(condition)
		{
		}

		/// <summary>
		/// Start time.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.StartKey,
			Description = LocalizedStrings.StartTimeKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public DateTime? Start
		{
			get => TryGetValue<DateTime?>(_prefix + nameof(Start));
			set => SetValue(_prefix + nameof(Start), value);
		}

		/// <summary>
		/// Ending time.
		/// </summary>
		[Display(
			ResourceType = typeof(LocalizedStrings),
			Name = LocalizedStrings.StopKey,
			Description = LocalizedStrings.WorkEndTimeKey,
			GroupName = LocalizedStrings.ParameterKey)]
		public DateTime? Stop
		{
			get => TryGetValue<DateTime?>(_prefix + nameof(Stop));
			set => SetValue(_prefix + nameof(Stop), value);
		}
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="InteractiveBrokersOrderCondition"/>.
	/// </summary>
	public InteractiveBrokersOrderCondition()
	{
		Oca = new(this);
		FinancialAdvisor = new(this);
		Volatility = new(this);
		SmartRouting = new(this);
		Combo = new(this);
		Scale = new(this);
		Hedge = new(this);
		Algo = new(this);
		Clearing = new(this);
		ShortSale = new(this, string.Empty);
		Active = new(this);
	}

	/// <summary>
	/// Extended condition.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExtraConditionsKey,
		Description = LocalizedStrings.ExtraConditionsDescKey,
		GroupName = LocalizedStrings.ParameterKey)]
	public ExtendedOrderTypes? ExtendedType
	{
		get => (ExtendedOrderTypes?)Parameters.TryGetValue(nameof(ExtendedType));
		set => Parameters[nameof(ExtendedType)] = value;
	}

	/// <summary>
	/// Stop-price.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceValueKey,
		GroupName = LocalizedStrings.ParameterKey)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>
	/// At trading opening.
	/// </summary>
	public bool? IsMarketOnOpen
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsMarketOnOpen));
		set => Parameters[nameof(IsMarketOnOpen)] = value;
	}

	/// <summary>
	/// OCA (One-Cancels All) settings.
	/// </summary>
	[TypeConverter(typeof(ExpandableObjectConverter))]
	public OcaCondition Oca { get; }

	/// <summary>
	/// Send order in TWS.
	/// </summary>
	public bool? Transmit
	{
		get => (bool?)Parameters.TryGetValue(nameof(Transmit));
		set => Parameters[nameof(Transmit)] = value;
	}

	/// <summary>
	/// Parent order ID.
	/// </summary>
	public int? ParentId
	{
		get => (int?)Parameters.TryGetValue(nameof(ParentId));
		set => Parameters[nameof(ParentId)] = value;
	}

	/// <summary>
	/// Split order volume.
	/// </summary>
	public bool? SplitVolume
	{
		get => (bool?)Parameters.TryGetValue(nameof(SplitVolume));
		set => Parameters[nameof(SplitVolume)] = value;
	}

	/// <summary>
	/// At best price.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AtBestPriceKey,
		Description = LocalizedStrings.AtBestPriceKey,
		GroupName = LocalizedStrings.ParameterKey)]
	public bool? SweepToFill
	{
		get => (bool?)Parameters.TryGetValue(nameof(SweepToFill));
		set => Parameters[nameof(SweepToFill)] = value;
	}

	/// <summary>
	/// Stop-order activation condition.
	/// </summary>
	public TriggerMethods? TriggerMethod
	{
		get => (TriggerMethods?)Parameters.TryGetValue(nameof(TriggerMethod));
		set => Parameters[nameof(TriggerMethod)] = value;
	}

	/// <summary>
	/// Allow to activate a stop-order outside of trading time.
	/// </summary>
	public bool? OutsideRth
	{
		get => (bool?)Parameters.TryGetValue(nameof(OutsideRth));
		set => Parameters[nameof(OutsideRth)] = value;
	}

	/// <summary>
	/// Hide order in market depth.
	/// </summary>
	/// <remarks>
	/// It is possible only when the order is sending to the ISLAND exchange.
	/// </remarks>
	public bool? Hidden
	{
		get => (bool?)Parameters.TryGetValue(nameof(Hidden));
		set => Parameters[nameof(Hidden)] = value;
	}

	/// <summary>
	/// Activate after given time.
	/// </summary>
	public DateTime? GoodAfterTime
	{
		get => (DateTime?)Parameters.TryGetValue(nameof(GoodAfterTime));
		set => Parameters[nameof(GoodAfterTime)] = value;
	}

	/// <summary>
	/// Cancel orders with wrong price.
	/// </summary>
	public bool? OverridePercentageConstraints
	{
		get => (bool?)Parameters.TryGetValue(nameof(OverridePercentageConstraints));
		set => Parameters[nameof(OverridePercentageConstraints)] = value;
	}

	/// <summary>
	/// Trader ID.
	/// </summary>
	public AgentDescriptions? Agent
	{
		get => (AgentDescriptions?)Parameters.TryGetValue(nameof(Agent));
		set => Parameters[nameof(Agent)] = value;
	}

	/// <summary>
	/// Wait for required volume to appear.
	/// </summary>
	public bool? AllOrNone
	{
		get => (bool?)Parameters.TryGetValue(nameof(AllOrNone));
		set => Parameters[nameof(AllOrNone)] = value;
	}

	/// <summary>
	/// The shift in the price for the order type <see cref="ExtendedOrderTypes.Relative"/>.
	/// </summary>
	public decimal? PercentOffset
	{
		get => (decimal?)Parameters.TryGetValue(nameof(PercentOffset));
		set => Parameters[nameof(PercentOffset)] = value;
	}

	/// <summary>
	/// Moving stop activation price.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceKey,
		GroupName = LocalizedStrings.ParameterKey)]
	public decimal? TrailStopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TrailStopPrice));
		set => Parameters[nameof(TrailStopPrice)] = value;
	}

	/// <summary>
	/// Trailing stop volume as percentage.
	/// </summary>
	public decimal? TrailStopVolumePercentage
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TrailStopVolumePercentage));
		set => Parameters[nameof(TrailStopVolumePercentage)] = value;
	}

	/// <summary>
	/// Settings for automatic order volume calculation.
	/// </summary>
	[TypeConverter(typeof(ExpandableObjectConverter))]
	public FinancialAdvisorCondition FinancialAdvisor { get; }

	/// <summary>
	/// Is the order opening or closing.
	/// </summary>
	/// <remarks>
	/// Only for institutional clients.
	/// </remarks>
	public bool? IsOpenOrClose
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsOpenOrClose));
		set => Parameters[nameof(IsOpenOrClose)] = value;
	}

	/// <summary>
	/// Sender.
	/// </summary>
	/// <remarks>
	/// Only for institutional clients.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SenderKey,
		Description = LocalizedStrings.SenderKey,
		GroupName = LocalizedStrings.ParameterKey)]
	public OrderOrigins? Origin
	{
		get => (OrderOrigins?)Parameters.TryGetValue(nameof(Origin));
		set => Parameters[nameof(Origin)] = value;
	}

	/// <summary>
	/// Condition for short sales of combined legs.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ShortSaleConditionsKey,
		Description = LocalizedStrings.ShortSaleConditionsKey,
		GroupName = LocalizedStrings.ParameterKey)]
	[TypeConverter(typeof(ExpandableObjectConverter))]
	public ShortSaleCondition ShortSale { get; }

	/// <summary>
	/// Trading.
	/// </summary>
	/// <remarks>
	/// Only BOX board.
	/// </remarks>
	public AuctionStrategies? AuctionStrategy
	{
		get => (AuctionStrategies?)Parameters.TryGetValue(nameof(AuctionStrategy));
		set => Parameters[nameof(AuctionStrategy)] = value;
	}

	/// <summary>
	/// Starting price.
	/// </summary>
	/// <remarks>
	/// Only BOX board.
	/// </remarks>
	public decimal? StartingPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StartingPrice));
		set => Parameters[nameof(StartingPrice)] = value;
	}

	/// <summary>
	/// Underlying asset price.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UnderlyingAssetPriceKey,
		Description = LocalizedStrings.UnderlyingAssetPriceKey,
		GroupName = LocalizedStrings.ParameterKey)]
	public decimal? StockRefPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StockRefPrice));
		set => Parameters[nameof(StockRefPrice)] = value;
	}

	/// <summary>
	/// Underlying asset delta.
	/// </summary>
	/// <remarks>
	/// Only BOX board.
	/// </remarks>
	public decimal? Delta
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Delta));
		set => Parameters[nameof(Delta)] = value;
	}

	/// <summary>
	/// Minimum price of underlying asset.
	/// </summary>
	public decimal? StockRangeLower
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StockRangeLower));
		set => Parameters[nameof(StockRangeLower)] = value;
	}

	/// <summary>
	/// Maximum price of underlying asset.
	/// </summary>
	public decimal? StockRangeUpper
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StockRangeUpper));
		set => Parameters[nameof(StockRangeUpper)] = value;
	}

	/// <summary>
	/// The settings for the orders type <see cref="ExtendedOrderTypes.Volatility"/>.
	/// </summary>
	[TypeConverter(typeof(ExpandableObjectConverter))]
	public VolatilityCondition Volatility { get; }

	/// <summary>
	/// Settings for orders that are sent to the Smart exchange.
	/// </summary>
	[TypeConverter(typeof(ExpandableObjectConverter))]
	public SmartRoutingCondition SmartRouting { get; }

	/// <summary>
	/// EFP orders settings.
	/// </summary>
	[TypeConverter(typeof(ExpandableObjectConverter))]
	public ComboCondition Combo { get; }

	/// <summary>
	/// Condition for order being changed.
	/// </summary>
	[TypeConverter(typeof(ExpandableObjectConverter))]
	public ScaleCondition Scale { get; }

	/// <summary>
	/// Condition for clearing information.
	/// </summary>
	[TypeConverter(typeof(ExpandableObjectConverter))]
	public ClearingCondition Clearing { get; }

	/// <summary>
	/// Condition for algo-orders.
	/// </summary>
	[TypeConverter(typeof(ExpandableObjectConverter))]
	public AlgoCondition Algo { get; }

	/// <summary>
	/// For order return information about commission and margin.
	/// </summary>
	public bool? WhatIf
	{
		get => (bool?)Parameters.TryGetValue(nameof(WhatIf));
		set => Parameters[nameof(WhatIf)] = value;
	}

	/// <summary>
	/// Algorithm ID.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AlgoKey,
		Description = LocalizedStrings.AlgoKey,
		GroupName = LocalizedStrings.ParameterKey)]
	public string AlgoId
	{
		get => (string)Parameters.TryGetValue(nameof(AlgoId));
		set => Parameters[nameof(AlgoId)] = value;
	}

	/// <summary>
	/// Additional parameters.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ParametersKey,
		Description = LocalizedStrings.ParametersKey,
		GroupName = LocalizedStrings.ParameterKey)]
	public IEnumerable<Tuple<string, string>> MiscOptions
	{
		get => (IEnumerable<Tuple<string, string>>)Parameters.TryGetValue(nameof(MiscOptions)) ?? [];
		set => Parameters[nameof(MiscOptions)] = value;
	}

	/// <summary>
	/// Solicited.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SolicitedKey,
		Description = LocalizedStrings.SolicitedKey,
		GroupName = LocalizedStrings.ParameterKey)]
	public bool? Solicited
	{
		get => (bool?)Parameters.TryGetValue(nameof(Solicited));
		set => Parameters[nameof(Solicited)] = value;
	}

	/// <summary>
	/// Randomize size.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RandomizeSizeKey,
		Description = LocalizedStrings.RandomizeSizeKey,
		GroupName = LocalizedStrings.ParameterKey)]
	public bool? RandomizeSize
	{
		get => (bool?)Parameters.TryGetValue(nameof(RandomizeSize));
		set => Parameters[nameof(RandomizeSize)] = value;
	}

	/// <summary>
	/// Randomize price books.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RandomizePriceKey,
		Description = LocalizedStrings.RandomizePriceKey,
		GroupName = LocalizedStrings.ParameterKey)]
	public bool? RandomizePrice
	{
		get => (bool?)Parameters.TryGetValue(nameof(RandomizePrice));
		set => Parameters[nameof(RandomizePrice)] = value;
	}

	/// <summary>
	/// Condition for hedge-orders.
	/// </summary>
	[TypeConverter(typeof(ExpandableObjectConverter))]
	public HedgeCondition Hedge { get; }

	/// <summary>
	/// Exercise the option.
	/// </summary>
	public bool IsOptionsExercise
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsOptionsExercise)) ?? false;
		set => Parameters[nameof(IsOptionsExercise)] = value;
	}

	/// <summary>
	/// Replace action.
	/// </summary>
	public bool IsOptionsOverride
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsOptionsOverride)) ?? false;
		set => Parameters[nameof(IsOptionsOverride)] = value;
	} 

	/// <summary>
	/// Condition for GTC orders.
	/// </summary>
	[TypeConverter(typeof(ExpandableObjectConverter))]
	public ActiveCondition Active { get; }

	/// <summary>
	/// Regulatory attribute that applies to all US Commodity (Futures) Exchanges, 
	/// provided to allow client to comply with CFTC Tag 50 Rules.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OperatorKey,
		Description = LocalizedStrings.OperatorKey,
		GroupName = LocalizedStrings.ParameterKey)]
	public string ExtOperator
	{
		get => (string)Parameters.TryGetValue(nameof(ExtOperator));
		set => Parameters[nameof(ExtOperator)] = value;
	}

	/// <summary>
	/// Define the Soft Dollar Tier used for the order. Only provided for registered professional advisors and hedge and mutual funds.
	/// </summary>
	public SoftDollarTier Tier
	{
		get => (SoftDollarTier)Parameters.TryGetValue(nameof(Tier)) ?? new SoftDollarTier();
		set => Parameters[nameof(Tier)] = value;
	}

	/// <summary>
	/// Pegged-to-benchmark orders: this attribute will contain the conId of the contract against which the order will be pegged.
	/// </summary>
	public int? ReferenceContractId
	{
		get => (int?)Parameters.TryGetValue(nameof(ReferenceContractId));
		set => Parameters[nameof(ReferenceContractId)] = value;
	}

	/// <summary>
	/// Pegged-to-benchmark orders: indicates whether the order's pegged price should increase or decreases.
	/// </summary>
	public bool? IsPeggedChangeAmountDecrease
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsPeggedChangeAmountDecrease));
		set => Parameters[nameof(IsPeggedChangeAmountDecrease)] = value;
	}

	/// <summary>
	/// Pegged-to-benchmark orders: amount by which the order's pegged price should move.
	/// </summary>
	public decimal? PeggedChangeAmount
	{
		get => (decimal?)Parameters.TryGetValue(nameof(PeggedChangeAmount));
		set => Parameters[nameof(PeggedChangeAmount)] = value;
	}

	/// <summary>
	/// Pegged-to-benchmark orders: the amount the reference contract needs to move to adjust the pegged order.
	/// </summary>
	public decimal? ReferenceChangeAmount
	{
		get => (decimal?)Parameters.TryGetValue(nameof(ReferenceChangeAmount));
		set => Parameters[nameof(ReferenceChangeAmount)] = value;
	}

	/// <summary>
	/// Pegged-to-benchmark orders: the exchange against which we want to observe the reference contract.
	/// </summary>
	public string ReferenceExchange
	{
		get => (string)Parameters.TryGetValue(nameof(ReferenceExchange));
		set => Parameters[nameof(ReferenceExchange)] = value;
	}

	/// <summary>
	/// Adjusted Stop orders: the parent order will be adjusted to the given type when the adjusted trigger price is penetrated.
	/// </summary>
	public string AdjustedOrderType
	{
		get => (string)Parameters.TryGetValue(nameof(AdjustedOrderType));
		set => Parameters[nameof(AdjustedOrderType)] = value;
	}

	/// <summary>
	/// 
	/// </summary>
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>
	/// 
	/// </summary>
	public decimal? LimitPriceOffset
	{
		get => (decimal?)Parameters.TryGetValue(nameof(LimitPriceOffset));
		set => Parameters[nameof(LimitPriceOffset)] = value;
	}

	/// <summary>
	/// Adjusted Stop orders: specifies the stop price of the adjusted (STP) parent.
	/// </summary>
	public decimal? AdjustedStopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(AdjustedStopPrice));
		set => Parameters[nameof(AdjustedStopPrice)] = value;
	}

	/// <summary>
	/// Adjusted Stop orders: specifies the stop limit price of the adjusted (STPL LMT) parent.
	/// </summary>
	public decimal? AdjustedStopLimitPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(AdjustedStopLimitPrice));
		set => Parameters[nameof(AdjustedStopLimitPrice)] = value;
	}

	/// <summary>
	/// Adjusted Stop orders: specifies the trailing amount of the adjusted (TRAIL) parent.
	/// </summary>
	public decimal? AdjustedTrailingAmount
	{
		get => (decimal?)Parameters.TryGetValue(nameof(AdjustedTrailingAmount));
		set => Parameters[nameof(AdjustedTrailingAmount)] = value;
	}

	/// <summary>
	/// Adjusted Stop orders: specifies where the trailing unit is an amount (set to 0) or a percentage (set to 1).
	/// </summary>
	public int? AdjustableTrailingUnit
	{
		get => (int?)Parameters.TryGetValue(nameof(AdjustableTrailingUnit));
		set => Parameters[nameof(AdjustableTrailingUnit)] = value;
	}

	/// <summary>
	/// Indicates whether or not conditions will also be valid outside Regular Trading Hours.
	/// </summary>
	public bool ConditionsIgnoreRth
	{
		get => (bool?)Parameters.TryGetValue(nameof(ConditionsIgnoreRth)) ?? false;
		set => Parameters[nameof(ConditionsIgnoreRth)] = value;
	}

	/// <summary>
	/// Conditions can determine if an order should become active or canceled.
	/// </summary>
	public bool ConditionsCancelOrder
	{
		get => (bool?)Parameters.TryGetValue(nameof(ConditionsCancelOrder)) ?? false;
		set => Parameters[nameof(ConditionsCancelOrder)] = value;
	}

	/// <summary>
	/// The native cash quantity.
	/// </summary>
	public decimal? CashQty
	{
		get => (decimal?)Parameters.TryGetValue(nameof(CashQty));
		set => Parameters[nameof(CashQty)] = value;
	}

	/// <summary>
	/// Extra conditions.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ParametersKey,
		Description = LocalizedStrings.ParametersKey,
		GroupName = LocalizedStrings.ParameterKey)]
	public IEnumerable<ExtraOrderCondition> ExtraConditions
	{
		get => (IEnumerable<ExtraOrderCondition>)Parameters.TryGetValue(nameof(ExtraConditions)) ?? [];
		set => Parameters[nameof(ExtraConditions)] = value;
	}

	/// <summary>
	/// Identifies a person as the responsible party for investment decisions within the firm. Orders covered by MiFID 2 (Markets in Financial Instruments Directive 2) must include either <see cref="Mifid2DecisionMaker"/> or <see cref="Mifid2DecisionAlgo"/> field (but not both).
	/// </summary>
	public string Mifid2DecisionMaker
	{
		get => (string)Parameters.TryGetValue(nameof(Mifid2DecisionMaker));
		set => Parameters[nameof(Mifid2DecisionMaker)] = value;
	}
	
	/// <summary>
	/// Identifies the algorithm responsible for investment decisions within the firm. Orders covered under MiFID 2 must include either <see cref="Mifid2DecisionMaker"/> or <see cref="Mifid2DecisionAlgo"/>, but cannot have both.
	/// </summary>
	public string Mifid2DecisionAlgo
	{
		get => (string)Parameters.TryGetValue(nameof(Mifid2DecisionAlgo));
		set => Parameters[nameof(Mifid2DecisionAlgo)] = value;
	}
	
	/// <summary>
	/// For MiFID 2 reporting: identifies a person as the responsible party for the execution of a transaction within the firm.
	/// </summary>
	public string Mifid2ExecutionTrader
	{
		get => (string)Parameters.TryGetValue(nameof(Mifid2ExecutionTrader));
		set => Parameters[nameof(Mifid2ExecutionTrader)] = value;
	}
			 
	/// <summary>
	/// For MiFID 2 reporting: identifies the algorithm responsible for the execution of a transaction within the firm.
	/// </summary>
	public string Mifid2ExecutionAlgo
	{
		get => (string)Parameters.TryGetValue(nameof(Mifid2ExecutionAlgo));
		set => Parameters[nameof(Mifid2ExecutionAlgo)] = value;
	}

	/// <summary>
	/// Don't use auto price for hedge
	/// </summary>
	public bool DontUseAutoPriceForHedge
	{
		get => (bool?)Parameters.TryGetValue(nameof(DontUseAutoPriceForHedge)) ?? false;
		set => Parameters[nameof(DontUseAutoPriceForHedge)] = value;
	}

	/// <summary>
	/// Create tickets from API orders when TWS is used as an OMS.
	/// </summary>
	public bool IsOmsContainer
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsOmsContainer)) ?? false;
		set => Parameters[nameof(IsOmsContainer)] = value;
	}

	/// <summary>
	/// Convert order of type 'Primary Peg' to 'D-Peg'.
	/// </summary>
	public bool DiscretionaryUpToLimitPrice
	{
		get => (bool?)Parameters.TryGetValue(nameof(DiscretionaryUpToLimitPrice)) ?? false;
		set => Parameters[nameof(DiscretionaryUpToLimitPrice)] = value;
	}

	/// <summary>
	/// Use price management algorithm.
	/// </summary>
	public bool? UsePriceManagementAlgo
	{
		get => (bool?)Parameters.TryGetValue(nameof(UsePriceManagementAlgo));
		set => Parameters[nameof(UsePriceManagementAlgo)] = value;
	}

	/// <summary>
	/// Auto cancel date.
	/// </summary>
	public string AutoCancelDate 
	{
		get => (string)Parameters.TryGetValue(nameof(AutoCancelDate));
		set => Parameters[nameof(AutoCancelDate)] = value;
	}

	//public decimal? FilledQuantity 
	//{
	//	get => (decimal?)Parameters.TryGetValue(nameof(FilledQuantity));
	//	set => Parameters[nameof(FilledQuantity)] = value;
	//}

	/// <summary>
	/// Futures contract id.
	/// </summary>
	public int? RefFuturesContractId 
	{
		get => (int?)Parameters.TryGetValue(nameof(RefFuturesContractId));
		set => Parameters[nameof(RefFuturesContractId)] = value;
	}

	/// <summary>
	/// Auto cancel parent.
	/// </summary>
	public bool? AutoCancelParent 
	{
		get => (bool?)Parameters.TryGetValue(nameof(AutoCancelParent));
		set => Parameters[nameof(AutoCancelParent)] = value;
	}

	/// <summary>
	/// Duration.
	/// </summary>
	public int? Duration
	{
		get => (int?)Parameters.TryGetValue(nameof(Duration));
		set => Parameters[nameof(Duration)] = value;
	}

	/// <summary>
	/// Reroute to SMART for IBKR ATS orders.
	/// </summary>
	public int? PostToAts
	{
		get => (int?)Parameters.TryGetValue(nameof(PostToAts));
		set => Parameters[nameof(PostToAts)] = value;
	}

	/// <summary>
	/// Shareholder.
	/// </summary>
	public string Shareholder 
	{
		get => (string)Parameters.TryGetValue(nameof(Shareholder));
		set => Parameters[nameof(Shareholder)] = value;
	}

	/// <summary>
	/// Imbalance only.
	/// </summary>
	public bool? ImbalanceOnly 
	{
		get => (bool?)Parameters.TryGetValue(nameof(ImbalanceOnly));
		set => Parameters[nameof(ImbalanceOnly)] = value;
	}

	/// <summary>
	/// Route marketable to bbo.
	/// </summary>
	public bool? RouteMarketableToBbo 
	{
		get => (bool?)Parameters.TryGetValue(nameof(RouteMarketableToBbo));
		set => Parameters[nameof(RouteMarketableToBbo)] = value;
	}

	/// <summary>
	/// Parent perm id.
	/// </summary>
	public long? ParentPermId 
	{
		get => (long?)Parameters.TryGetValue(nameof(ParentPermId));
		set => Parameters[nameof(ParentPermId)] = value;
	}

	/// <summary>
	/// Defines the minimum trade quantity to fill.
	/// </summary>
	public int? MinTradeQty
	{
		get => (int?)Parameters.TryGetValue(nameof(MinTradeQty));
		set => Parameters[nameof(MinTradeQty)] = value;
	}

	/// <summary>
	/// Defines the minimum size to compete.
	/// </summary>
	public int? MinCompeteSize
	{
		get => (int?)Parameters.TryGetValue(nameof(MinCompeteSize));
		set => Parameters[nameof(MinCompeteSize)] = value;
	}

	/// <summary>
	/// Specifies the offset Off The Midpoint that will be applied to the order.
	/// </summary>
	public decimal? CompeteAgainstBestOffset
	{
		get => (decimal?)Parameters.TryGetValue(nameof(CompeteAgainstBestOffset));
		set => Parameters[nameof(CompeteAgainstBestOffset)] = value;
	}

	/// <summary>
	/// This offset is applied when the spread is an even number of cents wide. This offset must be in whole-penny increments or zero.
	/// </summary>
	public decimal? MidOffsetAtWhole
	{
		get => (decimal?)Parameters.TryGetValue(nameof(MidOffsetAtWhole));
		set => Parameters[nameof(MidOffsetAtWhole)] = value;
	}

	/// <summary>
	/// This offset is applied when the spread is an odd number of cents wide. This offset must be in half-penny increments.
	/// </summary>
	public decimal? MidOffsetAtHalf
	{
		get => (int?)Parameters.TryGetValue(nameof(MidOffsetAtHalf));
		set => Parameters[nameof(MidOffsetAtHalf)] = value;
	}

	/// <summary>
	/// Accepts a list with parameters obtained from advancedOrderRejectJson.
	/// </summary>
	public string AdvancedErrorOverride
	{
		get => (string)Parameters.TryGetValue(nameof(AdvancedErrorOverride));
		set => Parameters[nameof(AdvancedErrorOverride)] = value;
	}

	/// <summary>
	/// Used by brokers and advisors when manually entering, modifying or cancelling orders at the direction of a client.
	/// </summary>
	public DateTime? ManualOrderTime
	{
		get => (DateTime?)Parameters.TryGetValue(nameof(ManualOrderTime));
		set => Parameters[nameof(ManualOrderTime)] = value;
	}

	/// <summary>
	/// Customer account.
	/// </summary>
	public string CustomerAccount
	{
		get => (string)Parameters.TryGetValue(nameof(CustomerAccount));
		set => Parameters[nameof(CustomerAccount)] = value;
	}

	/// <summary>
	/// Professional customer.
	/// </summary>
	public bool? ProfessionalCustomer
	{
		get => (bool?)Parameters.TryGetValue(nameof(ProfessionalCustomer));
		set => Parameters[nameof(ProfessionalCustomer)] = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice { get; set; }

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopPrice;
		set => StopPrice = value;
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set {  }
	}

	/// <summary>
	/// External User Id.
	/// </summary>
	public string ExternalUserId
	{
		get => (string)Parameters.TryGetValue(nameof(ExternalUserId));
		set => Parameters[nameof(ExternalUserId)] = value;
	}

	/// <summary>
	/// Manual Order Indicator.
	/// </summary>
	public int? ManualOrderIndicator
	{
		get => (int?)Parameters.TryGetValue(nameof(ManualOrderIndicator));
		set => Parameters[nameof(ManualOrderIndicator)] = value;
	}

	/// <summary>
	/// Bond accrued interest.
	/// </summary>
	public string BondAccruedInterest
	{
		get => (string)Parameters.TryGetValue(nameof(BondAccruedInterest));
		set => Parameters[nameof(BondAccruedInterest)] = value;
	}
}