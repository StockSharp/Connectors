namespace StockSharp.TigerBrokers.Native.Model;

abstract class TigerPushEvent
{
}

sealed class TigerConnectedEvent : TigerPushEvent
{
}
sealed class TigerDisconnectedEvent : TigerPushEvent
{
}
sealed class TigerQuoteEvent(QuoteBasicData data) : TigerPushEvent
{
	public QuoteBasicData Data { get; } = data;
}
sealed class TigerBboEvent(QuoteBBOData data) : TigerPushEvent
{
	public QuoteBBOData Data { get; } = data;
}
sealed class TigerDepthEvent(QuoteDepthData data) : TigerPushEvent
{
	public QuoteDepthData Data { get; } = data;
}
sealed class TigerTradeTickEvent(TradeTick data) : TigerPushEvent
{
	public TradeTick Data { get; } = data;
}
sealed class TigerFullTickEvent(TickData data) : TigerPushEvent
{
	public TickData Data { get; } = data;
}
sealed class TigerKlineEvent(KlineData data) : TigerPushEvent
{
	public KlineData Data { get; } = data;
}
sealed class TigerOrderEvent(OrderStatusData data) : TigerPushEvent
{
	public OrderStatusData Data { get; } = data;
}
sealed class TigerOrderTransactionEvent(OrderTransactionData data) : TigerPushEvent
{
	public OrderTransactionData Data { get; } = data;
}
sealed class TigerPositionEvent(PositionData data) : TigerPushEvent
{
	public PositionData Data { get; } = data;
}
sealed class TigerAssetEvent(AssetData data) : TigerPushEvent
{
	public AssetData Data { get; } = data;
}
sealed class TigerErrorEvent(Exception error) : TigerPushEvent
{
	public Exception Error { get; } = error;
}
