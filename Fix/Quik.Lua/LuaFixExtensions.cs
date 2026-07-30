namespace StockSharp.Fix.Quik.Lua;

using System.Net;

/// <summary>
/// <see cref="Lua"/> extensions.
/// </summary>
public static class LuaFixExtensions
{
	private static readonly HashSet<TimeSpan> _timeFrames = new(
	[
		TimeSpan.FromTicks(1),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(2),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(4),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(6),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(20),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(2),
		TimeSpan.FromDays(4),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	]);

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <summary>
	/// Default LUA FIX server address, localhost:5001
	/// </summary>
	public static readonly EndPoint DefaultLuaAddress = "localhost:5001".To<EndPoint>();

	/// <summary>
	/// Write <see cref="QuikOrderCondition"/> into <paramref name="writer"/>.
	/// </summary>
	/// <param name="writer"><see cref="IFixWriter"/></param>
	/// <param name="condition"><see cref="QuikOrderCondition"/></param>
	/// <param name="dateTimeParser"><see cref="FastDateTimeParser"/></param>
	/// <param name="cancellationToken">Cancellation token.</param>
	public static async ValueTask WriteOrderConditionAsync(this IFixWriter writer, QuikOrderCondition condition, FastDateTimeParser dateTimeParser, CancellationToken cancellationToken)
	{
		if (writer == null)
			throw new ArgumentNullException(nameof(writer));

		if (condition == null)
			throw new ArgumentNullException(nameof(condition));

		if (condition.Type != null)
		{
			await writer.WriteAsync((FixTags)LuaFixTags.Type, cancellationToken);
			await writer.WriteAsync((int)condition.Type.Value, cancellationToken);
		}

		if (condition.StopPriceCondition != null)
		{
			await writer.WriteAsync((FixTags)LuaFixTags.StopPriceCondition, cancellationToken);
			await writer.WriteAsync((int)condition.StopPriceCondition.Value, cancellationToken);
		}

		if (condition.ConditionOrderSide != null)
		{
			await writer.WriteAsync((FixTags)LuaFixTags.ConditionOrderSide, cancellationToken);
			await writer.WriteAsync((int)condition.ConditionOrderSide.Value, cancellationToken);
		}

		if (condition.LinkedOrderCancel != null)
		{
			await writer.WriteAsync((FixTags)LuaFixTags.LinkedOrderCancel, cancellationToken);
			await writer.WriteAsync(condition.LinkedOrderCancel.Value, cancellationToken);
		}

		//if (condition.Result != null)
		//{
		//	await writer.WriteAsync((FixTags)LuaFixTags.Result, cancellationToken);
		//	await writer.WriteAsync((int)condition.Result.Value, cancellationToken);
		//}

		if (condition.OtherSecurityId != null)
		{
			await writer.WriteAsync((FixTags)LuaFixTags.OtherSecurityCode, cancellationToken);
			await writer.WriteAsync(condition.OtherSecurityId.Value.SecurityCode, cancellationToken);
		}

		if (condition.StopPrice != null)
		{
			await writer.WriteAsync((FixTags)LuaFixTags.StopPrice, cancellationToken);
			await writer.WriteAsync(condition.StopPrice.Value, cancellationToken);
		}

		if (condition.StopLimitPrice != null)
		{
			await writer.WriteAsync((FixTags)LuaFixTags.StopLimitPrice, cancellationToken);
			await writer.WriteAsync(condition.StopLimitPrice.Value, cancellationToken);
		}

		if (condition.IsMarketStopLimit != null)
		{
			await writer.WriteAsync((FixTags)LuaFixTags.IsMarketStopLimit, cancellationToken);
			await writer.WriteAsync(condition.IsMarketStopLimit.Value, cancellationToken);
		}

		if (condition.ActiveTime != default)
		{
			await writer.WriteAsync((FixTags)LuaFixTags.ActiveTimeFrom, cancellationToken);
			await writer.WriteAsync(condition.ActiveTime.from, dateTimeParser, cancellationToken);

			await writer.WriteAsync((FixTags)LuaFixTags.ActiveTimeTo, cancellationToken);
			await writer.WriteAsync(condition.ActiveTime.to, dateTimeParser, cancellationToken);
		}

		if (condition.ConditionOrderId != null)
		{
			await writer.WriteAsync((FixTags)LuaFixTags.ConditionOrderId, cancellationToken);
			await writer.WriteAsync(condition.ConditionOrderId.Value, cancellationToken);
		}

		if (condition.ConditionOrderPartiallyMatched != null)
		{
			await writer.WriteAsync((FixTags)LuaFixTags.ConditionOrderPartiallyMatched, cancellationToken);
			await writer.WriteAsync(condition.ConditionOrderPartiallyMatched.Value, cancellationToken);
		}

		if (condition.ConditionOrderUseMatchedBalance != null)
		{
			await writer.WriteAsync((FixTags)LuaFixTags.ConditionOrderUseMatchedBalance, cancellationToken);
			await writer.WriteAsync(condition.ConditionOrderUseMatchedBalance.Value, cancellationToken);
		}

		if (condition.LinkedOrderPrice != null)
		{
			await writer.WriteAsync((FixTags)LuaFixTags.LinkedOrderPrice, cancellationToken);
			await writer.WriteAsync(condition.LinkedOrderPrice.Value, cancellationToken);
		}

		if (condition.Offset != null)
		{
			await writer.WriteAsync((FixTags)LuaFixTags.Offset, cancellationToken);
			await writer.WriteAsync(condition.Offset.ToString(), cancellationToken);
		}

		if (condition.Spread != null)
		{
			await writer.WriteAsync((FixTags)LuaFixTags.StopSpread, cancellationToken);
			await writer.WriteAsync(condition.Spread.ToString(), cancellationToken);
		}

		if (condition.IsMarketTakeProfit != null)
		{
			await writer.WriteAsync((FixTags)LuaFixTags.IsMarketTakeProfit, cancellationToken);
			await writer.WriteAsync(condition.IsMarketTakeProfit.Value, cancellationToken);
		}
	}

	/// <summary>
	/// Read <see cref="QuikOrderCondition"/> from <paramref name="reader"/>.
	/// </summary>
	/// <param name="reader"><see cref="IFixReader"/></param>
	/// <param name="tag"><see cref="FixTags"/></param>
	/// <param name="dateTimeParser"><see cref="FastDateTimeParser"/></param>
	/// <param name="getCondition"><see cref="QuikOrderCondition"/> provider.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>Operation result.</returns>
	public static async ValueTask<bool> ReadOrderConditionAsync(this IFixReader reader, FixTags tag, FastDateTimeParser dateTimeParser, Func<QuikOrderCondition> getCondition, CancellationToken cancellationToken)
	{
		if (getCondition == null)
			throw new ArgumentNullException(nameof(getCondition));

		var condition = getCondition();

		switch ((LuaFixTags)tag)
		{
			case LuaFixTags.Type:
				condition.Type = (QuikOrderConditionTypes)await reader.ReadIntAsync(cancellationToken);
				return true;
			case LuaFixTags.StopPriceCondition:
				condition.StopPriceCondition = (QuikStopPriceConditions)await reader.ReadIntAsync(cancellationToken);
				return true;
			case LuaFixTags.ConditionOrderSide:
				condition.ConditionOrderSide = (Sides)await reader.ReadIntAsync(cancellationToken);
				return true;
			case LuaFixTags.LinkedOrderCancel:
				condition.LinkedOrderCancel = await reader.ReadBoolAsync(cancellationToken);
				return true;
			//case LuaFixTags.Result:
			//	condition.Result = (QuikOrderConditionResults)await reader.ReadIntAsync(cancellationToken);
			//	return true;
			case LuaFixTags.OtherSecurityCode:
				condition.OtherSecurityId = new SecurityId { SecurityCode = await reader.ReadStringAsync(cancellationToken) };
				return true;
			case LuaFixTags.StopPrice:
				condition.StopPrice = await reader.ReadDecimalAsync(cancellationToken);
				return true;
			case LuaFixTags.StopLimitPrice:
				condition.StopLimitPrice = await reader.ReadDecimalAsync(cancellationToken);
				return true;
			case LuaFixTags.IsMarketStopLimit:
				condition.IsMarketStopLimit = await reader.ReadBoolAsync(cancellationToken);
				return true;
			case LuaFixTags.ActiveTimeFrom:
				var from = (await reader.ReadDateTimeAsync(dateTimeParser, cancellationToken)).UtcKind();
				condition.ActiveTime = new(from, condition.ActiveTime.to);
				return true;
			case LuaFixTags.ActiveTimeTo:
				var to = (await reader.ReadDateTimeAsync(dateTimeParser, cancellationToken)).UtcKind();
				condition.ActiveTime = new(condition.ActiveTime.from, to);
				return true;
			case LuaFixTags.ConditionOrderId:
				condition.ConditionOrderId = await reader.ReadLongAsync(cancellationToken);
				return true;
			case LuaFixTags.ConditionOrderPartiallyMatched:
				condition.ConditionOrderPartiallyMatched = await reader.ReadBoolAsync(cancellationToken);
				return true;
			case LuaFixTags.ConditionOrderUseMatchedBalance:
				condition.ConditionOrderUseMatchedBalance = await reader.ReadBoolAsync(cancellationToken);
				return true;
			case LuaFixTags.LinkedOrderPrice:
				condition.LinkedOrderPrice = await reader.ReadDecimalAsync(cancellationToken);
				return true;
			case LuaFixTags.Offset:
				condition.Offset = (await reader.ReadStringAsync(cancellationToken)).ToUnit();
				return true;
			case LuaFixTags.StopSpread:
				condition.Spread = (await reader.ReadStringAsync(cancellationToken)).ToUnit();
				return true;
			case LuaFixTags.IsMarketTakeProfit:
				condition.IsMarketTakeProfit = await reader.ReadBoolAsync(cancellationToken);
				return true;
			default:
				return false;
		}
	}
}