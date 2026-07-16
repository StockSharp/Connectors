namespace StockSharp.TigerBrokers.Native;

sealed class TigerPushCallback(Action<TigerPushEvent> publish) : IApiComposeCallback
{
	private readonly Action<TigerPushEvent> _publish = publish ?? throw new ArgumentNullException(nameof(publish));

	void IApiComposeCallback.ConnectionAck() => _publish(new TigerConnectedEvent());
	void IApiComposeCallback.ConnectionAck(int serverSendInterval, int serverReceiveInterval) => _publish(new TigerConnectedEvent());
	void IApiComposeCallback.ConnectionClosed() => _publish(new TigerDisconnectedEvent());
	void IApiComposeCallback.ConnectionKickout(int errorCode, string errorMsg)
		=> _publish(new TigerErrorEvent(new InvalidOperationException($"Tiger push connection was kicked out ({errorCode}): {errorMsg}")));
	void IApiComposeCallback.Error(string errorMsg) => _publish(new TigerErrorEvent(new InvalidOperationException(errorMsg)));
	void IApiComposeCallback.Error(int id, int errorCode, string errorMsg)
		=> _publish(new TigerErrorEvent(new InvalidOperationException($"Tiger push request {id} failed ({errorCode}): {errorMsg}")));
	void IApiComposeCallback.HearBeat(string heartBeatContent) { }
	void IApiComposeCallback.ServerHeartBeatTimeOut(string channelId)
		=> _publish(new TigerErrorEvent(new TimeoutException($"Tiger push heartbeat timed out on channel '{channelId}'.")));

	void ISubscribeApiCallback.QuoteChange(QuoteBasicData data) => _publish(new TigerQuoteEvent(data));
	void ISubscribeApiCallback.OptionChange(QuoteBasicData data) => _publish(new TigerQuoteEvent(data));
	void ISubscribeApiCallback.FutureChange(QuoteBasicData data) => _publish(new TigerQuoteEvent(data));
	void ISubscribeApiCallback.QuoteAskBidChange(QuoteBBOData data) => _publish(new TigerBboEvent(data));
	void ISubscribeApiCallback.OptionAskBidChange(QuoteBBOData data) => _publish(new TigerBboEvent(data));
	void ISubscribeApiCallback.FutureAskBidChange(QuoteBBOData data) => _publish(new TigerBboEvent(data));
	void ISubscribeApiCallback.DepthQuoteChange(QuoteDepthData data) => _publish(new TigerDepthEvent(data));
	void ISubscribeApiCallback.TradeTickChange(TradeTick data) => _publish(new TigerTradeTickEvent(data));
	void ISubscribeApiCallback.FullTickChange(TickData data) => _publish(new TigerFullTickEvent(data));
	void ISubscribeApiCallback.KlineChange(KlineData data) => _publish(new TigerKlineEvent(data));
	void ISubscribeApiCallback.OrderStatusChange(OrderStatusData data) => _publish(new TigerOrderEvent(data));
	void ISubscribeApiCallback.OrderTransactionChange(OrderTransactionData data) => _publish(new TigerOrderTransactionEvent(data));
	void ISubscribeApiCallback.PositionChange(PositionData data) => _publish(new TigerPositionEvent(data));
	void ISubscribeApiCallback.AssetChange(AssetData data) => _publish(new TigerAssetEvent(data));
	void ISubscribeApiCallback.SubscribeEnd(int id, string subject, string result) { }
	void ISubscribeApiCallback.CancelSubscribeEnd(int id, string subject, string result) { }
	void ISubscribeApiCallback.GetSubscribedSymbolEnd(SubscribedSymbol subscribedSymbol) { }
	void ISubscribeApiCallback.StockTopPush(StockTopData data) { }
	void ISubscribeApiCallback.OptionTopPush(OptionTopData data) { }
}
