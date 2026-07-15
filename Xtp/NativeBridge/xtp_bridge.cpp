#include "xtp_bridge.h"

#include <algorithm>
#include <cstring>
#include <new>

#if defined(_MSC_VER)
#pragma warning(push, 0)
#endif
#include "xtp_quote_api.h"
#include "xtp_trader_api.h"
#if defined(_MSC_VER)
#pragma warning(pop)
#endif

namespace
{
	enum Channel { quote_channel = 1, trader_channel = 2 };

	template <size_t N>
	void copy_text(char (&destination)[N], const char* source)
	{
		if (source == nullptr)
		{
			destination[0] = '\0';
			return;
		}

		const auto length = (std::min)(std::strlen(source), N - 1);
		std::memcpy(destination, source, length);
		destination[length] = '\0';
	}

	XtpBridgeError copy_error(const XTPRI* source)
	{
		XtpBridgeError result{};
		if (source != nullptr)
		{
			result.id = source->error_id;
			copy_text(result.message, source->error_msg);
		}
		return result;
	}

	struct Context;

	class QuoteSpi final : public XTP::API::QuoteSpi
	{
	public:
		explicit QuoteSpi(Context& context) : context_(context) {}
		void OnDisconnected(int reason) override;
		void OnError(XTPRI* error) override;
		void OnDepthMarketData(XTPMD* data, int64_t[], int32_t, int32_t, int64_t[], int32_t, int32_t) override;
		void OnTickByTick(XTPTBT* data) override;
		void OnQueryAllTickersFullInfo(XTPQFI* info, XTPRI* error, bool is_last) override;

	private:
		Context& context_;
	};

	class TraderSpi final : public XTP::API::TraderSpi
	{
	public:
		explicit TraderSpi(Context& context) : context_(context) {}
		void OnDisconnected(uint64_t session_id, int reason) override;
		void OnError(XTPRI* error) override;
		void OnOrderEvent(XTPOrderInfo* order, XTPRI* error, uint64_t session_id) override;
		void OnTradeEvent(XTPTradeReport* trade, uint64_t session_id) override;
		void OnCancelOrderError(XTPOrderCancelInfo* cancel, XTPRI* error, uint64_t session_id) override;
		void OnQueryOrder(XTPQueryOrderRsp* order, XTPRI* error, int request_id, bool is_last, uint64_t session_id) override;
		void OnQueryTrade(XTPQueryTradeRsp* trade, XTPRI* error, int request_id, bool is_last, uint64_t session_id) override;
		void OnQueryPosition(XTPQueryStkPositionRsp* position, XTPRI* error, int request_id, bool is_last, uint64_t session_id) override;
		void OnQueryAsset(XTPQueryAssetRsp* asset, XTPRI* error, int request_id, bool is_last, uint64_t session_id) override;

	private:
		Context& context_;
	};

	struct Context
	{
		XtpBridgeCallbacks callbacks{};
		void* user_data{};
		XTP::API::QuoteApi* quote{};
		XTP::API::TraderApi* trader{};
		uint64_t session{};
		QuoteSpi quote_spi;
		TraderSpi trader_spi;

		Context() : quote_spi(*this), trader_spi(*this) {}
	};

	XtpBridgeOrder copy_order(const XTPOrderInfo* source)
	{
		XtpBridgeOrder result{};
		if (source == nullptr)
			return result;

		result.order_id = source->order_xtp_id;
		result.client_order_id = source->order_client_id;
		result.cancel_order_id = source->order_cancel_xtp_id;
		copy_text(result.ticker, source->ticker);
		result.market = source->market;
		result.price = source->price;
		result.volume = source->quantity;
		result.price_type = source->price_type;
		result.side = source->side;
		result.position_effect = source->position_effect;
		result.business_type = source->business_type;
		result.traded_volume = source->qty_traded;
		result.balance = source->qty_left;
		result.insert_time = source->insert_time;
		result.update_time = source->update_time;
		result.cancel_time = source->cancel_time;
		result.trade_amount = source->trade_amount;
		copy_text(result.local_order_id, source->order_local_id);
		result.status = source->order_status;
		result.submit_status = source->order_submit_status;
		result.order_type = source->order_type;
		return result;
	}

	XtpBridgeTrade copy_trade(const XTPTradeReport* source)
	{
		XtpBridgeTrade result{};
		if (source == nullptr)
			return result;

		result.order_id = source->order_xtp_id;
		result.client_order_id = source->order_client_id;
		copy_text(result.ticker, source->ticker);
		result.market = source->market;
		copy_text(result.trade_id, source->exec_id);
		result.price = source->price;
		result.volume = source->quantity;
		result.time = source->trade_time;
		result.amount = source->trade_amount;
		result.report_index = source->report_index;
		result.side = source->side;
		result.position_effect = source->position_effect;
		result.business_type = source->business_type;
		return result;
	}

	void QuoteSpi::OnDisconnected(int reason)
	{
		if (context_.callbacks.disconnected != nullptr)
			context_.callbacks.disconnected(quote_channel, reason, context_.user_data);
	}

	void QuoteSpi::OnError(XTPRI* error)
	{
		if (context_.callbacks.error == nullptr)
			return;
		const auto value = copy_error(error);
		context_.callbacks.error(quote_channel, &value, context_.user_data);
	}

	void QuoteSpi::OnDepthMarketData(XTPMD* data, int64_t[], int32_t, int32_t, int64_t[], int32_t, int32_t)
	{
		if (data == nullptr || context_.callbacks.depth == nullptr)
			return;

		XtpBridgeDepth value{};
		value.exchange = data->exchange_id;
		copy_text(value.ticker, data->ticker);
		value.last_price = data->last_price;
		value.previous_close = data->pre_close_price;
		value.open_price = data->open_price;
		value.high_price = data->high_price;
		value.low_price = data->low_price;
		value.close_price = data->close_price;
		value.upper_limit = data->upper_limit_price;
		value.lower_limit = data->lower_limit_price;
		value.time = data->data_time;
		value.volume = data->qty;
		value.turnover = data->turnover;
		value.average_price = data->avg_price;
		value.trades_count = data->trades_count;
		copy_text(value.status, data->ticker_status);
		std::copy_n(data->bid, 10, value.bids);
		std::copy_n(data->ask, 10, value.asks);
		std::copy_n(data->bid_qty, 10, value.bid_volumes);
		std::copy_n(data->ask_qty, 10, value.ask_volumes);
		context_.callbacks.depth(&value, context_.user_data);
	}

	void QuoteSpi::OnTickByTick(XTPTBT* data)
	{
		if (data == nullptr || context_.callbacks.tick == nullptr)
			return;

		XtpBridgeTick value{};
		value.exchange = data->exchange_id;
		copy_text(value.ticker, data->ticker);
		value.sequence = data->seq;
		value.time = data->data_time;
		value.type = data->type;

		if (data->type == XTP_TBT_TRADE)
		{
			value.channel = data->trade.channel_no;
			value.source_sequence = data->trade.seq;
			value.price = data->trade.price;
			value.volume = data->trade.qty;
			value.turnover = data->trade.money;
			value.bid_order_id = data->trade.bid_no;
			value.ask_order_id = data->trade.ask_no;
			value.flag = data->trade.trade_flag;
		}

		context_.callbacks.tick(&value, context_.user_data);
	}

	void QuoteSpi::OnQueryAllTickersFullInfo(XTPQFI* info, XTPRI* error, bool is_last)
	{
		if (context_.callbacks.security == nullptr)
			return;

		XtpBridgeSecurity value{};
		if (info != nullptr)
		{
			value.exchange = info->exchange_id;
			copy_text(value.ticker, info->ticker);
			copy_text(value.name, info->ticker_name);
			value.security_type = info->security_type;
			value.previous_close = info->pre_close_price;
			value.upper_limit = info->upper_limit_price;
			value.lower_limit = info->lower_limit_price;
			value.price_tick = info->price_tick;
			value.buy_quantity_unit = info->bid_qty_unit;
			value.sell_quantity_unit = info->ask_qty_unit;
			value.status = info->security_status;
		}
		const auto error_value = copy_error(error);
		context_.callbacks.security(info == nullptr ? nullptr : &value, &error_value, is_last ? 1 : 0, context_.user_data);
	}

	void TraderSpi::OnDisconnected(uint64_t, int reason)
	{
		context_.session = 0;
		if (context_.callbacks.disconnected != nullptr)
			context_.callbacks.disconnected(trader_channel, reason, context_.user_data);
	}

	void TraderSpi::OnError(XTPRI* error)
	{
		if (context_.callbacks.error == nullptr)
			return;
		const auto value = copy_error(error);
		context_.callbacks.error(trader_channel, &value, context_.user_data);
	}

	void TraderSpi::OnOrderEvent(XTPOrderInfo* order, XTPRI* error, uint64_t)
	{
		if (context_.callbacks.order == nullptr)
			return;
		const auto value = copy_order(order);
		const auto error_value = copy_error(error);
		context_.callbacks.order(order == nullptr ? nullptr : &value, &error_value, 0, 1, 0, context_.user_data);
	}

	void TraderSpi::OnTradeEvent(XTPTradeReport* trade, uint64_t)
	{
		if (context_.callbacks.trade == nullptr)
			return;
		const auto value = copy_trade(trade);
		const XtpBridgeError error{};
		context_.callbacks.trade(trade == nullptr ? nullptr : &value, &error, 0, 1, 0, context_.user_data);
	}

	void TraderSpi::OnCancelOrderError(XTPOrderCancelInfo* cancel, XTPRI* error, uint64_t)
	{
		if (context_.callbacks.cancel == nullptr)
			return;
		const auto error_value = copy_error(error);
		context_.callbacks.cancel(cancel == nullptr ? 0 : cancel->order_xtp_id, cancel == nullptr ? 0 : cancel->order_cancel_xtp_id, &error_value, context_.user_data);
	}

	void TraderSpi::OnQueryOrder(XTPQueryOrderRsp* order, XTPRI* error, int request_id, bool is_last, uint64_t)
	{
		if (context_.callbacks.order == nullptr)
			return;
		const auto value = copy_order(order);
		const auto error_value = copy_error(error);
		context_.callbacks.order(order == nullptr ? nullptr : &value, &error_value, request_id, is_last ? 1 : 0, 1, context_.user_data);
	}

	void TraderSpi::OnQueryTrade(XTPQueryTradeRsp* trade, XTPRI* error, int request_id, bool is_last, uint64_t)
	{
		if (context_.callbacks.trade == nullptr)
			return;
		const auto value = copy_trade(trade);
		const auto error_value = copy_error(error);
		context_.callbacks.trade(trade == nullptr ? nullptr : &value, &error_value, request_id, is_last ? 1 : 0, 1, context_.user_data);
	}

	void TraderSpi::OnQueryPosition(XTPQueryStkPositionRsp* position, XTPRI* error, int request_id, bool is_last, uint64_t)
	{
		if (context_.callbacks.position == nullptr)
			return;

		XtpBridgePosition value{};
		if (position != nullptr)
		{
			copy_text(value.ticker, position->ticker);
			copy_text(value.name, position->ticker_name);
			value.market = position->market;
			value.volume = position->total_qty;
			value.sellable_volume = position->sellable_qty;
			value.average_price = position->avg_price;
			value.unrealized_pnl = position->unrealized_pnl;
			value.yesterday_volume = position->yesterday_position;
			value.direction = position->position_direction;
			value.market_value = position->market_value;
		}
		const auto error_value = copy_error(error);
		context_.callbacks.position(position == nullptr ? nullptr : &value, &error_value, request_id, is_last ? 1 : 0, context_.user_data);
	}

	void TraderSpi::OnQueryAsset(XTPQueryAssetRsp* asset, XTPRI* error, int request_id, bool is_last, uint64_t)
	{
		if (context_.callbacks.asset == nullptr)
			return;

		XtpBridgeAsset value{};
		if (asset != nullptr)
		{
			value.total_asset = asset->total_asset;
			value.buying_power = asset->buying_power;
			value.security_asset = asset->security_asset;
			value.frozen_cash = asset->withholding_amount;
			value.balance = asset->banlance;
			value.deposit_withdraw = asset->deposit_withdraw;
			value.realized_pnl = asset->trade_netting;
			value.account_type = asset->account_type;
		}
		const auto error_value = copy_error(error);
		context_.callbacks.asset(asset == nullptr ? nullptr : &value, &error_value, request_id, is_last ? 1 : 0, context_.user_data);
	}
}

extern "C"
{
	void* XTP_BRIDGE_CALL xtp_create(uint8_t client_id, const char* data_path, const char* software_version, const char* software_key, const XtpBridgeCallbacks* callbacks, void* user_data)
	{
		if (callbacks == nullptr || data_path == nullptr)
			return nullptr;

		auto* context = new (std::nothrow) Context();
		if (context == nullptr)
			return nullptr;

		context->callbacks = *callbacks;
		context->user_data = user_data;
		context->quote = XTP::API::QuoteApi::CreateQuoteApi(client_id, data_path, XTP_LOG_LEVEL_INFO);
		context->trader = XTP::API::TraderApi::CreateTraderApi(client_id, data_path, XTP_LOG_LEVEL_INFO);

		if (context->quote == nullptr || context->trader == nullptr)
		{
			if (context->quote != nullptr) context->quote->Release();
			if (context->trader != nullptr) context->trader->Release();
			delete context;
			return nullptr;
		}

		context->quote->RegisterSpi(&context->quote_spi);
		context->trader->RegisterSpi(&context->trader_spi);
		context->trader->SubscribePublicTopic(XTP_TERT_RESTART);
		if (software_version != nullptr && software_version[0] != '\0') context->trader->SetSoftwareVersion(software_version);
		if (software_key != nullptr && software_key[0] != '\0') context->trader->SetSoftwareKey(software_key);
		return context;
	}

	void XTP_BRIDGE_CALL xtp_destroy(void* value)
	{
		auto* context = static_cast<Context*>(value);
		if (context == nullptr) return;
		if (context->quote != nullptr) { context->quote->RegisterSpi(nullptr); context->quote->Release(); }
		if (context->trader != nullptr) { context->trader->RegisterSpi(nullptr); context->trader->Release(); }
		delete context;
	}

	int32_t XTP_BRIDGE_CALL xtp_quote_login(void* value, const char* host, int32_t port, const char* user, const char* password, int32_t protocol, const char* local_ip)
	{
		auto* context = static_cast<Context*>(value);
		return context == nullptr ? -1 : context->quote->Login(host, port, user, password, static_cast<XTP_PROTOCOL_TYPE>(protocol), local_ip != nullptr && local_ip[0] != '\0' ? local_ip : nullptr);
	}

	uint64_t XTP_BRIDGE_CALL xtp_trader_login(void* value, const char* host, int32_t port, const char* user, const char* password, int32_t protocol, const char* local_ip)
	{
		auto* context = static_cast<Context*>(value);
		if (context == nullptr) return 0;
		context->session = context->trader->Login(host, port, user, password, static_cast<XTP_PROTOCOL_TYPE>(protocol), local_ip != nullptr && local_ip[0] != '\0' ? local_ip : nullptr);
		return context->session;
	}

	int32_t XTP_BRIDGE_CALL xtp_quote_logout(void* value)
	{
		auto* context = static_cast<Context*>(value);
		return context == nullptr ? -1 : context->quote->Logout();
	}

	int32_t XTP_BRIDGE_CALL xtp_trader_logout(void* value)
	{
		auto* context = static_cast<Context*>(value);
		if (context == nullptr || context->session == 0) return 0;
		const auto result = context->trader->Logout(context->session);
		context->session = 0;
		return result;
	}

	int32_t XTP_BRIDGE_CALL xtp_subscribe_market_data(void* value, const char* ticker, int32_t exchange, int32_t subscribe)
	{
		auto* context = static_cast<Context*>(value);
		if (context == nullptr || ticker == nullptr) return -1;
		char* tickers[] = { const_cast<char*>(ticker) };
		return subscribe != 0 ? context->quote->SubscribeMarketData(tickers, 1, static_cast<XTP_EXCHANGE_TYPE>(exchange)) : context->quote->UnSubscribeMarketData(tickers, 1, static_cast<XTP_EXCHANGE_TYPE>(exchange));
	}

	int32_t XTP_BRIDGE_CALL xtp_subscribe_ticks(void* value, const char* ticker, int32_t exchange, int32_t subscribe)
	{
		auto* context = static_cast<Context*>(value);
		if (context == nullptr || ticker == nullptr) return -1;
		char* tickers[] = { const_cast<char*>(ticker) };
		return subscribe != 0 ? context->quote->SubscribeTickByTick(tickers, 1, static_cast<XTP_EXCHANGE_TYPE>(exchange)) : context->quote->UnSubscribeTickByTick(tickers, 1, static_cast<XTP_EXCHANGE_TYPE>(exchange));
	}

	int32_t XTP_BRIDGE_CALL xtp_query_securities(void* value, int32_t exchange)
	{
		auto* context = static_cast<Context*>(value);
		return context == nullptr ? -1 : context->quote->QueryAllTickersFullInfo(static_cast<XTP_EXCHANGE_TYPE>(exchange));
	}

	uint64_t XTP_BRIDGE_CALL xtp_insert_order(void* value, const XtpBridgeOrderRequest* request)
	{
		auto* context = static_cast<Context*>(value);
		if (context == nullptr || request == nullptr || context->session == 0) return 0;
		XTPOrderInsertInfo order{};
		order.order_client_id = request->client_order_id;
		copy_text(order.ticker, request->ticker);
		order.market = static_cast<XTP_MARKET_TYPE>(request->market);
		order.price = request->price;
		order.stop_price = request->stop_price;
		order.quantity = request->volume;
		order.price_type = static_cast<XTP_PRICE_TYPE>(request->price_type);
		order.side = static_cast<XTP_SIDE_TYPE>(request->side);
		order.position_effect = static_cast<XTP_POSITION_EFFECT_TYPE>(request->position_effect);
		order.business_type = static_cast<XTP_BUSINESS_TYPE>(request->business_type);
		return context->trader->InsertOrder(&order, context->session);
	}

	uint64_t XTP_BRIDGE_CALL xtp_cancel_order(void* value, uint64_t order_id)
	{
		auto* context = static_cast<Context*>(value);
		return context == nullptr || context->session == 0 ? 0 : context->trader->CancelOrder(order_id, context->session);
	}

	int32_t XTP_BRIDGE_CALL xtp_query_orders(void* value, int32_t request_id)
	{
		auto* context = static_cast<Context*>(value);
		if (context == nullptr || context->session == 0) return -1;
		XTPQueryOrderReq request{};
		return context->trader->QueryOrders(&request, context->session, request_id);
	}

	int32_t XTP_BRIDGE_CALL xtp_query_trades(void* value, int32_t request_id)
	{
		auto* context = static_cast<Context*>(value);
		if (context == nullptr || context->session == 0) return -1;
		XTPQueryTraderReq request{};
		return context->trader->QueryTrades(&request, context->session, request_id);
	}

	int32_t XTP_BRIDGE_CALL xtp_query_positions(void* value, int32_t request_id)
	{
		auto* context = static_cast<Context*>(value);
		return context == nullptr || context->session == 0 ? -1 : context->trader->QueryPosition(nullptr, context->session, request_id);
	}

	int32_t XTP_BRIDGE_CALL xtp_query_assets(void* value, int32_t request_id)
	{
		auto* context = static_cast<Context*>(value);
		return context == nullptr || context->session == 0 ? -1 : context->trader->QueryAsset(context->session, request_id);
	}

	int32_t XTP_BRIDGE_CALL xtp_get_last_error(void* value, int32_t channel, XtpBridgeError* error)
	{
		auto* context = static_cast<Context*>(value);
		if (context == nullptr || error == nullptr) return -1;
		const auto* source = channel == quote_channel ? context->quote->GetApiLastError() : context->trader->GetApiLastError();
		*error = copy_error(source);
		return error->id;
	}

	const char* XTP_BRIDGE_CALL xtp_get_version(void* value, int32_t channel)
	{
		auto* context = static_cast<Context*>(value);
		if (context == nullptr) return nullptr;
		return channel == quote_channel ? context->quote->GetApiVersion() : context->trader->GetApiVersion();
	}
}
