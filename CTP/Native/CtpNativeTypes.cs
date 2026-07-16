namespace StockSharp.Ctp.Native;

using System.Runtime.InteropServices;
using System.Text;

internal static class CtpNativeText
{
	public static string Decode(byte[] value)
	{
		if (value == null || value.Length == 0)
			return string.Empty;
		var length = Array.IndexOf(value, (byte)0);
		if (length < 0)
			length = value.Length;
		return Encoding.UTF8.GetString(value, 0, length);
	}
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct CtpNativeError
{
	public int Id;
	public int RequestId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)]
	public string InstrumentId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
	public string OrderRef;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
	public byte[] MessageBytes;

	public readonly string Message => CtpNativeText.Decode(MessageBytes);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct CtpNativeInstrument
{
	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)]
	public string InstrumentId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string ExchangeId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)]
	public string ExchangeInstrumentId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)]
	public string ProductId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)]
	public string UnderlyingInstrumentId;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
	public byte[] NameBytes;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string OpenDate;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string ExpireDate;

	public int ProductClass;
	public int DeliveryYear;
	public int DeliveryMonth;
	public int MaxMarketOrderVolume;
	public int MinMarketOrderVolume;
	public int MaxLimitOrderVolume;
	public int MinLimitOrderVolume;
	public int VolumeMultiple;
	public double PriceTick;
	public double StrikePrice;
	public int OptionsType;
	public int IsTrading;
	public int LifePhase;

	public readonly string Name => CtpNativeText.Decode(NameBytes);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct CtpNativeDepth
{
	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)]
	public string InstrumentId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string ExchangeId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string TradingDay;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string ActionDay;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string UpdateTime;

	public int UpdateMillisec;
	public double LastPrice;
	public double PreSettlementPrice;
	public double PreClosePrice;
	public double PreOpenInterest;
	public double OpenPrice;
	public double HighPrice;
	public double LowPrice;
	public int Volume;
	public double Turnover;
	public double OpenInterest;
	public double ClosePrice;
	public double SettlementPrice;
	public double UpperLimitPrice;
	public double LowerLimitPrice;
	public double AveragePrice;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
	public double[] BidPrices;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
	public double[] AskPrices;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
	public int[] BidVolumes;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
	public int[] AskVolumes;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct CtpNativeOrderRequest
{
	public int RequestId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)]
	public string InstrumentId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string ExchangeId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
	public string OrderRef;

	public int PriceType;
	public int Direction;
	public int OffsetFlag;
	public int HedgeFlag;
	public double LimitPrice;
	public int Volume;
	public int TimeCondition;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string GoodTillDate;

	public int VolumeCondition;
	public int MinimumVolume;
	public int ContingentCondition;
	public double StopPrice;
	public int ForceCloseReason;
	public int AutoSuspend;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct CtpNativeCancelRequest
{
	public int RequestId;
	public int ActionRef;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)]
	public string InstrumentId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string ExchangeId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
	public string OrderRef;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
	public string OrderSystemId;

	public int FrontId;
	public int SessionId;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct CtpNativeOrder
{
	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)]
	public string InstrumentId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string ExchangeId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)]
	public string ExchangeInstrumentId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
	public string OrderRef;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
	public string OrderSystemId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
	public string OrderLocalId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string TradingDay;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string InsertDate;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string InsertTime;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string UpdateTime;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string CancelTime;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
	public byte[] StatusMessageBytes;

	public int RequestId;
	public int FrontId;
	public int SessionId;
	public int PriceType;
	public int Direction;
	public int OffsetFlag;
	public int HedgeFlag;
	public double LimitPrice;
	public double StopPrice;
	public int VolumeOriginal;
	public int VolumeTraded;
	public int VolumeLeft;
	public int TimeCondition;
	public int VolumeCondition;
	public int ContingentCondition;
	public int SubmitStatus;
	public int OrderStatus;

	public readonly string StatusMessage => CtpNativeText.Decode(StatusMessageBytes);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct CtpNativeTrade
{
	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)]
	public string InstrumentId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string ExchangeId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)]
	public string ExchangeInstrumentId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
	public string OrderRef;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
	public string OrderSystemId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
	public string TradeId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string TradingDay;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string TradeDate;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string TradeTime;

	public int Direction;
	public int OffsetFlag;
	public int HedgeFlag;
	public double Price;
	public int Volume;
	public int SequenceNumber;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct CtpNativePosition
{
	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)]
	public string InstrumentId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string ExchangeId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string TradingDay;

	public int Direction;
	public int HedgeFlag;
	public int PositionDate;
	public int Position;
	public int TodayPosition;
	public int YesterdayPosition;
	public int LongFrozen;
	public int ShortFrozen;
	public double PositionCost;
	public double OpenCost;
	public double UseMargin;
	public double Commission;
	public double CloseProfit;
	public double PositionProfit;
	public double SettlementPrice;
	public double OptionValue;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct CtpNativeAccount
{
	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
	public string AccountId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
	public string CurrencyId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
	public string TradingDay;

	public double PreBalance;
	public double Balance;
	public double Available;
	public double WithdrawQuota;
	public double Deposit;
	public double Withdraw;
	public double FrozenMargin;
	public double FrozenCash;
	public double FrozenCommission;
	public double CurrentMargin;
	public double Commission;
	public double CloseProfit;
	public double PositionProfit;
	public double OptionValue;
}
