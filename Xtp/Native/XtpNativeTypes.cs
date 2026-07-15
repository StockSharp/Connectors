namespace StockSharp.Xtp.Native;

using System.Runtime.InteropServices;

internal enum XtpChannel
{
	Quote = 1,
	Trader = 2,
}

internal enum XtpExchange
{
	Shanghai = 1,
	Shenzhen = 2,
	Beijing = 3,
}

internal enum XtpMarket
{
	Unknown = 0,
	Shenzhen = 1,
	Shanghai = 2,
	Beijing = 3,
	HongKong = 4,
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct XtpNativeError
{
	public int Id;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 124)]
	public string Message;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct XtpNativeSecurity
{
	public int Exchange;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
	public string Ticker;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
	public string Name;

	public int SecurityType;
	public double PreviousClose;
	public double UpperLimit;
	public double LowerLimit;
	public double PriceTick;
	public int BuyQuantityUnit;
	public int SellQuantityUnit;
	public int Status;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct XtpNativeDepth
{
	public int Exchange;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
	public string Ticker;

	public double LastPrice;
	public double PreviousClose;
	public double OpenPrice;
	public double HighPrice;
	public double LowPrice;
	public double ClosePrice;
	public double UpperLimit;
	public double LowerLimit;
	public long Time;
	public long Volume;
	public double Turnover;
	public double AveragePrice;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
	public double[] Bids;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
	public double[] Asks;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
	public long[] BidVolumes;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
	public long[] AskVolumes;

	public long TradesCount;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
	public string Status;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct XtpNativeTick
{
	public int Exchange;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
	public string Ticker;

	public long Sequence;
	public long Time;
	public int Type;
	public int Channel;
	public long SourceSequence;
	public double Price;
	public long Volume;
	public double Turnover;
	public long BidOrderId;
	public long AskOrderId;
	public int Flag;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct XtpNativeOrderRequest
{
	public uint ClientOrderId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
	public string Ticker;

	public int Market;
	public double Price;
	public double StopPrice;
	public long Volume;
	public int PriceType;
	public int Side;
	public int PositionEffect;
	public int BusinessType;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct XtpNativeOrder
{
	public ulong OrderId;
	public uint ClientOrderId;
	public ulong CancelOrderId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
	public string Ticker;

	public int Market;
	public double Price;
	public long Volume;
	public int PriceType;
	public int Side;
	public int PositionEffect;
	public int BusinessType;
	public long TradedVolume;
	public long Balance;
	public long InsertTime;
	public long UpdateTime;
	public long CancelTime;
	public double TradeAmount;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
	public string LocalOrderId;

	public int Status;
	public int SubmitStatus;
	public int OrderType;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct XtpNativeTrade
{
	public ulong OrderId;
	public uint ClientOrderId;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
	public string Ticker;

	public int Market;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
	public string TradeId;

	public double Price;
	public long Volume;
	public long Time;
	public double Amount;
	public ulong ReportIndex;
	public int Side;
	public int PositionEffect;
	public int BusinessType;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 8)]
internal struct XtpNativePosition
{
	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
	public string Ticker;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
	public string Name;

	public int Market;
	public long Volume;
	public long SellableVolume;
	public double AveragePrice;
	public double UnrealizedPnl;
	public long YesterdayVolume;
	public int Direction;
	public double MarketValue;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct XtpNativeAsset
{
	public double TotalAsset;
	public double BuyingPower;
	public double SecurityAsset;
	public double FrozenCash;
	public double Balance;
	public double DepositWithdraw;
	public double RealizedPnl;
	public int AccountType;
}
