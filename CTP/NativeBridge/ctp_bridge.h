#pragma once

#include <stdint.h>

#if defined(_WIN32)
#define CTP_BRIDGE_API __declspec(dllexport)
#define CTP_BRIDGE_CALL __cdecl
#else
#define CTP_BRIDGE_API __attribute__((visibility("default")))
#define CTP_BRIDGE_CALL
#endif

#pragma pack(push, 8)

typedef struct CtpBridgeError
{
	int32_t id;
	int32_t request_id;
	char instrument_id[81];
	char order_ref[14];
	char message[512];
} CtpBridgeError;

typedef struct CtpBridgeInstrument
{
	char instrument_id[81];
	char exchange_id[9];
	char exchange_instrument_id[81];
	char product_id[81];
	char underlying_instrument_id[81];
	char name[128];
	char open_date[9];
	char expire_date[9];
	int32_t product_class;
	int32_t delivery_year;
	int32_t delivery_month;
	int32_t max_market_order_volume;
	int32_t min_market_order_volume;
	int32_t max_limit_order_volume;
	int32_t min_limit_order_volume;
	int32_t volume_multiple;
	double price_tick;
	double strike_price;
	int32_t options_type;
	int32_t is_trading;
	int32_t life_phase;
} CtpBridgeInstrument;

typedef struct CtpBridgeDepth
{
	char instrument_id[81];
	char exchange_id[9];
	char trading_day[9];
	char action_day[9];
	char update_time[9];
	int32_t update_millisec;
	double last_price;
	double pre_settlement_price;
	double pre_close_price;
	double pre_open_interest;
	double open_price;
	double high_price;
	double low_price;
	int32_t volume;
	double turnover;
	double open_interest;
	double close_price;
	double settlement_price;
	double upper_limit_price;
	double lower_limit_price;
	double average_price;
	double bid_prices[5];
	double ask_prices[5];
	int32_t bid_volumes[5];
	int32_t ask_volumes[5];
} CtpBridgeDepth;

typedef struct CtpBridgeOrderRequest
{
	int32_t request_id;
	char instrument_id[81];
	char exchange_id[9];
	char order_ref[14];
	int32_t price_type;
	int32_t direction;
	int32_t offset_flag;
	int32_t hedge_flag;
	double limit_price;
	int32_t volume;
	int32_t time_condition;
	char gtd_date[9];
	int32_t volume_condition;
	int32_t min_volume;
	int32_t contingent_condition;
	double stop_price;
	int32_t force_close_reason;
	int32_t auto_suspend;
} CtpBridgeOrderRequest;

typedef struct CtpBridgeCancelRequest
{
	int32_t request_id;
	int32_t action_ref;
	char instrument_id[81];
	char exchange_id[9];
	char order_ref[14];
	char order_system_id[21];
	int32_t front_id;
	int32_t session_id;
} CtpBridgeCancelRequest;

typedef struct CtpBridgeOrder
{
	char instrument_id[81];
	char exchange_id[9];
	char exchange_instrument_id[81];
	char order_ref[14];
	char order_system_id[21];
	char order_local_id[13];
	char trading_day[9];
	char insert_date[9];
	char insert_time[9];
	char update_time[9];
	char cancel_time[9];
	char status_message[512];
	int32_t request_id;
	int32_t front_id;
	int32_t session_id;
	int32_t price_type;
	int32_t direction;
	int32_t offset_flag;
	int32_t hedge_flag;
	double limit_price;
	double stop_price;
	int32_t volume_original;
	int32_t volume_traded;
	int32_t volume_left;
	int32_t time_condition;
	int32_t volume_condition;
	int32_t contingent_condition;
	int32_t submit_status;
	int32_t order_status;
} CtpBridgeOrder;

typedef struct CtpBridgeTrade
{
	char instrument_id[81];
	char exchange_id[9];
	char exchange_instrument_id[81];
	char order_ref[14];
	char order_system_id[21];
	char trade_id[21];
	char trading_day[9];
	char trade_date[9];
	char trade_time[9];
	int32_t direction;
	int32_t offset_flag;
	int32_t hedge_flag;
	double price;
	int32_t volume;
	int32_t sequence_number;
} CtpBridgeTrade;

typedef struct CtpBridgePosition
{
	char instrument_id[81];
	char exchange_id[9];
	char trading_day[9];
	int32_t direction;
	int32_t hedge_flag;
	int32_t position_date;
	int32_t position;
	int32_t today_position;
	int32_t yesterday_position;
	int32_t long_frozen;
	int32_t short_frozen;
	double position_cost;
	double open_cost;
	double use_margin;
	double commission;
	double close_profit;
	double position_profit;
	double settlement_price;
	double option_value;
} CtpBridgePosition;

typedef struct CtpBridgeAccount
{
	char account_id[13];
	char currency_id[4];
	char trading_day[9];
	double pre_balance;
	double balance;
	double available;
	double withdraw_quota;
	double deposit;
	double withdraw;
	double frozen_margin;
	double frozen_cash;
	double frozen_commission;
	double current_margin;
	double commission;
	double close_profit;
	double position_profit;
	double option_value;
} CtpBridgeAccount;

typedef void (CTP_BRIDGE_CALL *CtpStateCallback)(int32_t channel, int32_t state, int32_t reason, const CtpBridgeError* error, void* user_data);
typedef void (CTP_BRIDGE_CALL *CtpErrorCallback)(int32_t channel, const CtpBridgeError* error, void* user_data);
typedef void (CTP_BRIDGE_CALL *CtpInstrumentCallback)(const CtpBridgeInstrument* instrument, const CtpBridgeError* error, int32_t request_id, int32_t is_last, void* user_data);
typedef void (CTP_BRIDGE_CALL *CtpDepthCallback)(const CtpBridgeDepth* depth, void* user_data);
typedef void (CTP_BRIDGE_CALL *CtpOrderCallback)(const CtpBridgeOrder* order, const CtpBridgeError* error, int32_t request_id, int32_t is_last, int32_t is_query, void* user_data);
typedef void (CTP_BRIDGE_CALL *CtpTradeCallback)(const CtpBridgeTrade* trade, const CtpBridgeError* error, int32_t request_id, int32_t is_last, int32_t is_query, void* user_data);
typedef void (CTP_BRIDGE_CALL *CtpPositionCallback)(const CtpBridgePosition* position, const CtpBridgeError* error, int32_t request_id, int32_t is_last, void* user_data);
typedef void (CTP_BRIDGE_CALL *CtpAccountCallback)(const CtpBridgeAccount* account, const CtpBridgeError* error, int32_t request_id, int32_t is_last, void* user_data);

typedef struct CtpBridgeCallbacks
{
	CtpStateCallback state;
	CtpErrorCallback error;
	CtpInstrumentCallback instrument;
	CtpDepthCallback depth;
	CtpOrderCallback order;
	CtpTradeCallback trade;
	CtpPositionCallback position;
	CtpAccountCallback account;
} CtpBridgeCallbacks;

#pragma pack(pop)

#ifdef __cplusplus
extern "C" {
#endif

CTP_BRIDGE_API void* CTP_BRIDGE_CALL ctp_create(const CtpBridgeCallbacks* callbacks, void* user_data);
CTP_BRIDGE_API void CTP_BRIDGE_CALL ctp_destroy(void* context);
CTP_BRIDGE_API int32_t CTP_BRIDGE_CALL ctp_connect_market(void* context, const char* front, const char* flow_path, const char* broker_id, const char* user_id, const char* password, int32_t production_mode);
CTP_BRIDGE_API int32_t CTP_BRIDGE_CALL ctp_connect_trader(void* context, const char* front, const char* flow_path, const char* broker_id, const char* user_id, const char* investor_id, const char* password, const char* app_id, const char* auth_code, const char* product_info, int32_t resume_type, int32_t production_mode);
CTP_BRIDGE_API int32_t CTP_BRIDGE_CALL ctp_disconnect_market(void* context);
CTP_BRIDGE_API int32_t CTP_BRIDGE_CALL ctp_disconnect_trader(void* context);
CTP_BRIDGE_API int32_t CTP_BRIDGE_CALL ctp_subscribe_market_data(void* context, const char* instrument_id, int32_t subscribe);
CTP_BRIDGE_API int32_t CTP_BRIDGE_CALL ctp_next_order_ref(void* context, char* order_ref, int32_t capacity);
CTP_BRIDGE_API int32_t CTP_BRIDGE_CALL ctp_query_instruments(void* context, int32_t request_id, const char* exchange_id, const char* instrument_id, const char* product_id);
CTP_BRIDGE_API int32_t CTP_BRIDGE_CALL ctp_insert_order(void* context, const CtpBridgeOrderRequest* request);
CTP_BRIDGE_API int32_t CTP_BRIDGE_CALL ctp_cancel_order(void* context, const CtpBridgeCancelRequest* request);
CTP_BRIDGE_API int32_t CTP_BRIDGE_CALL ctp_query_orders(void* context, int32_t request_id);
CTP_BRIDGE_API int32_t CTP_BRIDGE_CALL ctp_query_trades(void* context, int32_t request_id);
CTP_BRIDGE_API int32_t CTP_BRIDGE_CALL ctp_query_positions(void* context, int32_t request_id, const char* instrument_id);
CTP_BRIDGE_API int32_t CTP_BRIDGE_CALL ctp_query_account(void* context, int32_t request_id, const char* currency_id);
CTP_BRIDGE_API const char* CTP_BRIDGE_CALL ctp_get_version(int32_t channel);
CTP_BRIDGE_API int32_t CTP_BRIDGE_CALL ctp_get_struct_size(int32_t type);

#ifdef __cplusplus
}
#endif
