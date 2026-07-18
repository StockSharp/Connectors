"use strict";

process.env.TZ = "Etc/UTC";

const readline = require("node:readline");
const oneApi = require("@activfinancial/one-api");

const PROTOCOL_VERSION = 1;
const GATEWAY_VERSION = "1.0.0";
const ONE_API_VERSION = "1.1.4";
const MAX_MESSAGE_LENGTH = 16 * 1024 * 1024;

const Commands = Object.freeze({
    connect: 1,
    disconnect: 2,
    lookup: 3,
    snapshot: 4,
    historyTicks: 5,
    historyBars: 6,
    subscribe: 7,
    unsubscribe: 8
});

const MessageKinds = Object.freeze({
    response: 1,
    record: 2,
    subscriptionFinished: 3,
    error: 4,
    log: 5
});

const DataKinds = Object.freeze({ level1: 1, ticks: 2 });

let session = null;
let isDisconnecting = false;
let writeQueue = Promise.resolve();
let commandQueue = Promise.resolve();

const subscriptions = new Map();
const symbolTimeZones = new Map();

function send(message) {
    const line = JSON.stringify({ version: PROTOCOL_VERSION, ...message }) + "\n";
    if (line.length > MAX_MESSAGE_LENGTH)
        return Promise.reject(new Error("Gateway response exceeds the 16 MiB protocol limit."));

    writeQueue = writeQueue.catch(() => undefined).then(() => new Promise((resolve, reject) => {
        if (process.stdout.write(line, "utf8")) {
            resolve();
            return;
        }

        const onDrain = () => {
            process.stdout.off("error", onError);
            resolve();
        };
        const onError = (error) => {
            process.stdout.off("drain", onDrain);
            reject(error);
        };
        process.stdout.once("drain", onDrain);
        process.stdout.once("error", onError);
    }));
    return writeQueue;
}

function requiredString(request, name) {
    const value = request[name];
    if (typeof value !== "string" || value.trim().length === 0)
        throw gatewayError("invalid_request", `${name} must be a non-empty string.`);
    return value.trim();
}

function optionalString(request, name) {
    const value = request[name];
    if (value == null)
        return undefined;
    if (typeof value !== "string")
        throw gatewayError("invalid_request", `${name} must be a string.`);
    return value;
}

function requiredSecret(request, name) {
    const value = request[name];
    if (typeof value !== "string" || value.length === 0)
        throw gatewayError("invalid_request", `${name} must be a non-empty string.`);
    return value;
}

function requiredInteger(request, name, minimum, maximum) {
    const value = request[name];
    if (!Number.isSafeInteger(value) || value < minimum || value > maximum)
        throw gatewayError("invalid_request", `${name} must be an integer between ${minimum} and ${maximum}.`);
    return value;
}

function optionalInteger(request, name, minimum, maximum) {
    const value = request[name];
    if (value == null)
        return undefined;
    if (!Number.isSafeInteger(value) || value < minimum || value > maximum)
        throw gatewayError("invalid_request", `${name} must be an integer between ${minimum} and ${maximum}.`);
    return value;
}

function gatewayError(code, message) {
    const error = new Error(message);
    error.gatewayCode = code;
    return error;
}

function statusName(statusCode) {
    return oneApi.StatusCode[statusCode] || `status_${statusCode}`;
}

function statusError(statusCode, operation) {
    return gatewayError(statusName(statusCode), `${operation} failed with ACTIV status ${statusName(statusCode)} (${statusCode}).`);
}

function normalizeError(error) {
    if (error && typeof error === "object") {
        const statusCode = Number.isSafeInteger(error.statusCode) ? error.statusCode :
            Number.isSafeInteger(error.code) ? error.code : undefined;
        return {
            code: error.gatewayCode || (statusCode == null ? "gateway_error" : statusName(statusCode)),
            message: typeof error.message === "string" && error.message.length > 0
                ? error.message
                : statusCode == null ? String(error) : `ACTIV status ${statusName(statusCode)} (${statusCode}).`
        };
    }
    if (Number.isSafeInteger(error))
        return { code: statusName(error), message: `ACTIV status ${statusName(error)} (${error}).` };
    return { code: "gateway_error", message: String(error || "Unknown ACTIV gateway error.") };
}

function requireSession() {
    if (session == null)
        throw gatewayError("not_connected", "Connect to an ACTIV One API gateway first.");
    return session;
}

function topicKey(dataSourceId, symbol) {
    return `${dataSourceId}|${String(symbol).toUpperCase()}`;
}

function getField(fieldData, fieldId) {
    if (fieldData == null || !fieldData.isFieldPresent(fieldId))
        return undefined;
    const field = fieldData.getField(fieldId);
    return field.isDefined ? field.value : undefined;
}

function numberField(fieldData, fieldId) {
    const value = getField(fieldData, fieldId);
    if (value == null)
        return undefined;
    const number = typeof value === "number" ? value : Number(value.valueOf());
    return Number.isFinite(number) ? number : undefined;
}

function integerField(fieldData, fieldId) {
    const value = numberField(fieldData, fieldId);
    return value == null ? undefined : Math.trunc(value);
}

function textField(fieldData, fieldId) {
    const value = getField(fieldData, fieldId);
    if (value == null)
        return undefined;
    if (typeof value === "string")
        return value.replace(/\0+$/u, "");
    if (value instanceof Uint8Array)
        return Buffer.from(value).toString("utf8").replace(/\0+$/u, "");
    return String(value);
}

function timestampField(fieldData, fieldId, isDateOnly = false, isTimeOnly = false) {
    const value = getField(fieldData, fieldId);
    if (value == null || typeof value.getFullYear !== "function")
        return undefined;

    const nanoseconds = typeof value.getNanoseconds === "function" ? value.getNanoseconds() : undefined;
    const microseconds = typeof value.getMicroseconds === "function" ? value.getMicroseconds() : undefined;
    const fractionTicks = nanoseconds != null
        ? Math.trunc(nanoseconds / 100)
        : microseconds != null
            ? Math.trunc(microseconds * 10)
            : Math.trunc(value.getMilliseconds() * 10000);

    return {
        year: isTimeOnly ? undefined : value.getFullYear(),
        month: isTimeOnly ? undefined : value.getMonth() + 1,
        day: isTimeOnly ? undefined : value.getDate(),
        hour: isDateOnly ? undefined : value.getHours(),
        minute: isDateOnly ? undefined : value.getMinutes(),
        second: isDateOnly ? undefined : value.getSeconds(),
        fraction_ticks: Math.max(0, Math.min(9999999, fractionTicks)),
        is_date_only: isDateOnly,
        is_time_only: isTimeOnly
    };
}

function normalizeRecord(message, isRefresh = false, eventType = undefined, fallbackTimeZone = "UTC") {
    const fieldData = message.fieldData;
    const dataSourceId = Number(message.dataSourceId);
    const symbol = String(message.symbol || "");
    const key = topicKey(dataSourceId, symbol);
    const suppliedTimeZone = textField(fieldData, oneApi.FieldId.FID_OLSON_TIME_ZONE);
    if (suppliedTimeZone)
        symbolTimeZones.set(key, suppliedTimeZone);
    const timeZone = suppliedTimeZone || symbolTimeZones.get(key) || fallbackTimeZone || "UTC";

    return {
        symbol,
        data_source_id: dataSourceId,
        symbology_id: Number(message.symbologyId),
        permission_id: message.permissionId == null ? undefined : Number(message.permissionId),
        update_id: message.updateId == null ? integerField(fieldData, oneApi.FieldId.FID_UPDATE_ID) : Number(message.updateId),
        event_type: eventType == null ? undefined : Number(eventType),
        tick_type: integerField(fieldData, oneApi.FieldId.FID_TICK_TYPE),
        is_refresh: Boolean(isRefresh),
        time_zone: timeZone,
        date_time: timestampField(fieldData, oneApi.FieldId.FID_DATE_TIME),
        last_update_date_time: timestampField(fieldData, oneApi.FieldId.FID_LAST_UPDATE_DATE_TIME),
        trade_date: timestampField(fieldData, oneApi.FieldId.FID_TRADE_DATE, true, false),
        trade_time: timestampField(fieldData, oneApi.FieldId.FID_TRADE_TIME, false, true),
        bid_time: timestampField(fieldData, oneApi.FieldId.FID_BID_TIME, false, true),
        ask_time: timestampField(fieldData, oneApi.FieldId.FID_ASK_TIME, false, true),
        name: textField(fieldData, oneApi.FieldId.FID_NAME),
        description: textField(fieldData, oneApi.FieldId.FID_DESCRIPTION),
        entity_type: integerField(fieldData, oneApi.FieldId.FID_ENTITY_TYPE),
        currency: textField(fieldData, oneApi.FieldId.FID_CURRENCY),
        exchange: textField(fieldData, oneApi.FieldId.FID_EXCHANGE),
        mic: textField(fieldData, oneApi.FieldId.FID_MIC),
        country_code: textField(fieldData, oneApi.FieldId.FID_COUNTRY_CODE),
        isin: textField(fieldData, oneApi.FieldId.FID_ISIN),
        cusip: textField(fieldData, oneApi.FieldId.FID_CUSIP),
        sedol: textField(fieldData, oneApi.FieldId.FID_SEDOL_CODE),
        expiration: timestampField(fieldData, oneApi.FieldId.FID_EXPIRATION_DATE, true, false),
        strike_price: numberField(fieldData, oneApi.FieldId.FID_STRIKE_PRICE),
        option_type: textField(fieldData, oneApi.FieldId.FID_OPTION_TYPE),
        underlying_symbol: textField(fieldData, oneApi.FieldId.FID_UNDERLYING_SYMBOL),
        minimum_tick: numberField(fieldData, oneApi.FieldId.FID_MINIMUM_TICK),
        contract_size: numberField(fieldData, oneApi.FieldId.FID_CONTRACT_SIZE),
        lot_size: numberField(fieldData, oneApi.FieldId.FID_LOT_SIZE),
        bid_price: numberField(fieldData, oneApi.FieldId.FID_BID),
        bid_size: numberField(fieldData, oneApi.FieldId.FID_BID_SIZE),
        ask_price: numberField(fieldData, oneApi.FieldId.FID_ASK),
        ask_size: numberField(fieldData, oneApi.FieldId.FID_ASK_SIZE),
        trade_price: numberField(fieldData, oneApi.FieldId.FID_TRADE),
        trade_size: numberField(fieldData, oneApi.FieldId.FID_TRADE_SIZE),
        trade_id: textField(fieldData, oneApi.FieldId.FID_TRADE_ID),
        tick_price: numberField(fieldData, oneApi.FieldId.FID_TICK_PRICE),
        tick_size: numberField(fieldData, oneApi.FieldId.FID_TICK_SIZE),
        tick_condition: textField(fieldData, oneApi.FieldId.FID_TICK_CONDITION),
        open_price: numberField(fieldData, oneApi.FieldId.FID_OPEN),
        high_price: numberField(fieldData, oneApi.FieldId.FID_TRADE_HIGH),
        low_price: numberField(fieldData, oneApi.FieldId.FID_TRADE_LOW),
        close_price: numberField(fieldData, oneApi.FieldId.FID_CLOSE),
        previous_close: numberField(fieldData, oneApi.FieldId.FID_PREVIOUS_CLOSE),
        settlement_price: numberField(fieldData, oneApi.FieldId.FID_SETTLEMENT),
        cumulative_volume: numberField(fieldData, oneApi.FieldId.FID_CUMULATIVE_VOLUME),
        open_interest: numberField(fieldData, oneApi.FieldId.FID_OPEN_INTEREST),
        tick_count: integerField(fieldData, oneApi.FieldId.FID_TICK_COUNT),
        trading_status: textField(fieldData, oneApi.FieldId.FID_TRADING_STATUS),
        net_change: numberField(fieldData, oneApi.FieldId.FID_NET_CHANGE),
        percent_change: numberField(fieldData, oneApi.FieldId.FID_PERCENT_CHANGE)
    };
}

function isLevel1Record(record) {
    return record.bid_price != null || record.bid_size != null ||
        record.ask_price != null || record.ask_size != null ||
        record.trade_price != null || record.trade_size != null ||
        record.open_price != null || record.high_price != null ||
        record.low_price != null || record.close_price != null ||
        record.previous_close != null || record.settlement_price != null ||
        record.cumulative_volume != null || record.open_interest != null ||
        record.trading_status != null || record.net_change != null ||
        record.percent_change != null;
}

function isTradeRecord(record) {
    const tickType = record.tick_type;
    const eventType = record.event_type;
    return (tickType >= 1 && tickType <= 5 ||
        eventType === oneApi.EventType.trade ||
        eventType === oneApi.EventType.tradeCorrection ||
        eventType === oneApi.EventType.tradeCancel ||
        eventType === oneApi.EventType.tradeNonRegular) &&
        (record.tick_price != null || record.trade_price != null);
}

function timestampSortKey(timestamp) {
    if (timestamp == null)
        return "";
    const part = (value, length) => String(value == null ? 0 : value).padStart(length, "0");
    return part(timestamp.year, 6) + part(timestamp.month, 2) + part(timestamp.day, 2) +
        part(timestamp.hour, 2) + part(timestamp.minute, 2) + part(timestamp.second, 2) +
        part(timestamp.fraction_ticks, 7);
}

async function collectSnapshot(handle, limit, skip, fallbackTimeZone, isLookup) {
    const records = [];
    let skipped = 0;
    try {
        for await (const result of handle) {
            if (result.statusCode !== oneApi.StatusCode.success) {
                if (!isLookup || result.isComplete)
                    throw statusError(result.statusCode, isLookup ? "ACTIV query snapshot" : "ACTIV snapshot");
                continue;
            }
            if (result.message == null || result.message.fieldData == null)
                continue;
            if (skipped < skip) {
                skipped++;
                continue;
            }
            records.push(normalizeRecord(result.message, true, undefined, fallbackTimeZone));
            if (records.length >= limit)
                break;
        }
    } finally {
        if (!handle.isDeleted())
            handle.delete();
    }
    return records;
}

async function canonicalSnapshot(request) {
    const currentSession = requireSession();
    const dataSourceId = requiredInteger(request, "data_source_id", 0, 65535);
    const symbologyId = requiredInteger(request, "symbology_id", 0, 65535);
    const symbol = requiredString(request, "symbol");
    const fallbackTimeZone = optionalString(request, "fallback_time_zone") || "UTC";
    const handle = currentSession.snapshot({ dataSourceId, symbologyId, symbol });
    return collectSnapshot(handle, 1, 0, fallbackTimeZone, false);
}

async function ensureTopicTimeZone(request) {
    const key = topicKey(request.data_source_id, request.symbol);
    if (symbolTimeZones.has(key))
        return;
    try {
        await canonicalSnapshot(request);
    } catch {
        // TSS access can exist without a current snapshot entitlement. The explicit fallback remains authoritative.
    }
}

function makeTimeSeriesOptions(request, isHistory) {
    const fromUtc = optionalInteger(request, "from_utc", -8640000000000000, 8640000000000000);
    const toUtc = optionalInteger(request, "to_utc", -8640000000000000, 8640000000000000);
    const limit = requiredInteger(request, "limit", 1, 100000);
    if (fromUtc != null && toUtc != null && fromUtc > toUtc)
        throw gatewayError("invalid_range", "from_utc must not be later than to_utc.");

    let start;
    let end;
    let count;
    if (fromUtc != null && toUtc != null) {
        start = new Date(fromUtc);
        end = new Date(toUtc);
    } else if (fromUtc != null) {
        start = new Date(fromUtc);
        count = limit;
    } else if (toUtc != null) {
        start = new Date(toUtc);
        count = -limit;
    } else {
        start = isHistory ? oneApi.Date.MAX : oneApi.DateTime.MAX;
        count = -limit;
    }

    return { start, end, count, isUtc: !isHistory, shouldReverse: false };
}

async function collectTimeSeries(handle, limit, fallbackTimeZone) {
    const records = [];
    try {
        for await (const result of handle) {
            if (result.statusCode !== oneApi.StatusCode.success)
                throw statusError(result.statusCode, "ACTIV time series request");
            if (result.message != null && result.message.fieldData != null)
                records.push(normalizeRecord(result.message, false, undefined, fallbackTimeZone));
            if (records.length >= limit)
                break;
        }
    } finally {
        if (!handle.isDeleted())
            handle.delete();
    }
    records.sort((left, right) => timestampSortKey(left.date_time).localeCompare(timestampSortKey(right.date_time)));
    return records;
}

async function historyTicks(request) {
    const currentSession = requireSession();
    const dataSourceId = requiredInteger(request, "data_source_id", 0, 65535);
    const symbologyId = requiredInteger(request, "symbology_id", 0, 65535);
    const symbol = requiredString(request, "symbol");
    const limit = requiredInteger(request, "limit", 1, 100000);
    const fallbackTimeZone = optionalString(request, "fallback_time_zone") || "UTC";
    await ensureTopicTimeZone(request);
    const options = makeTimeSeriesOptions(request, false);
    options.filter = oneApi.TickFilter.allTrades;
    const handle = currentSession.timeSeriesTicks({ dataSourceId, symbologyId, symbol }, options);
    return collectTimeSeries(handle, limit, symbolTimeZones.get(topicKey(dataSourceId, symbol)) || fallbackTimeZone);
}

async function historyBars(request) {
    const currentSession = requireSession();
    const dataSourceId = requiredInteger(request, "data_source_id", 0, 65535);
    const symbologyId = requiredInteger(request, "symbology_id", 0, 65535);
    const symbol = requiredString(request, "symbol");
    const limit = requiredInteger(request, "limit", 1, 100000);
    const timeFrameMinutes = requiredInteger(request, "time_frame_minutes", 1, 10080);
    const fallbackTimeZone = optionalString(request, "fallback_time_zone") || "UTC";
    await ensureTopicTimeZone(request);
    const topic = { dataSourceId, symbologyId, symbol };
    let handle;

    if ([1, 2, 5, 10, 15, 30, 60].includes(timeFrameMinutes)) {
        const options = makeTimeSeriesOptions(request, false);
        options.filter = oneApi.IntradayFilter.ohlcBars;
        options.barInterval = timeFrameMinutes;
        handle = currentSession.timeSeriesIntradayBars(topic, options);
    } else if (timeFrameMinutes === 1440 || timeFrameMinutes === 10080) {
        const options = makeTimeSeriesOptions(request, true);
        delete options.isUtc;
        options.barInterval = timeFrameMinutes === 1440
            ? oneApi.HistoryInterval.daily
            : oneApi.HistoryInterval.weekly;
        handle = currentSession.timeSeriesHistoryBars(topic, options);
    } else {
        throw gatewayError("unsupported_time_frame", `ACTIV does not support ${timeFrameMinutes}-minute bars.`);
    }

    return collectTimeSeries(handle, limit, symbolTimeZones.get(topicKey(dataSourceId, symbol)) || fallbackTimeZone);
}

function deleteSubscription(entry) {
    if (entry == null)
        return;
    if (entry.handle != null && !entry.handle.isDeleted())
        entry.handle.delete();
    if (entry.parameters != null && !entry.parameters.isDeleted())
        entry.parameters.delete();
}

async function pumpSubscription(entry) {
    try {
        for await (const result of entry.handle) {
            if (result.statusCode !== oneApi.StatusCode.success) {
                await send({
                    kind: MessageKinds.error,
                    subscription_id: entry.subscriptionId,
                    error: normalizeError(statusError(result.statusCode, "ACTIV subscription"))
                });
                if (result.isComplete) {
                    break;
                }
                continue;
            }
            if (result.message == null)
                continue;
            const record = normalizeRecord(result.message, result.message.isRefresh,
                result.message.eventType, entry.fallbackTimeZone);
            const isRelevant = entry.dataKind === DataKinds.level1
                ? isLevel1Record(record)
                : isTradeRecord(record);
            if (!isRelevant)
                continue;
            await send({
                kind: MessageKinds.record,
                subscription_id: entry.subscriptionId,
                record
            });
            if (entry.remaining != null && --entry.remaining <= 0) {
                break;
            }
        }
    } catch (error) {
        if (!entry.isExplicitlyClosed) {
            await send({
                kind: MessageKinds.error,
                subscription_id: entry.subscriptionId,
                error: normalizeError(error)
            });
        }
    } finally {
        subscriptions.delete(entry.subscriptionId);
        deleteSubscription(entry);
        if (!entry.isExplicitlyClosed) {
            await send({
                kind: MessageKinds.subscriptionFinished,
                subscription_id: entry.subscriptionId
            });
        }
    }
}

function createSubscription(request) {
    const currentSession = requireSession();
    const subscriptionId = requiredInteger(request, "subscription_id", 1, Number.MAX_SAFE_INTEGER);
    if (subscriptions.has(subscriptionId))
        throw gatewayError("duplicate_subscription", `Subscription ${subscriptionId} already exists.`);
    const dataSourceId = requiredInteger(request, "data_source_id", 0, 65535);
    const symbologyId = requiredInteger(request, "symbology_id", 0, 65535);
    const symbol = requiredString(request, "symbol");
    const dataKind = requiredInteger(request, "data_kind", DataKinds.level1, DataKinds.ticks);
    const count = optionalInteger(request, "count", 1, 2147483647);
    const fallbackTimeZone = optionalString(request, "fallback_time_zone") || "UTC";

    let parameters;
    if (dataKind === DataKinds.ticks) {
        parameters = currentSession.registerSubscriptionParameters({
            eventTypeIncludeFilter: [
                oneApi.EventType.trade,
                oneApi.EventType.tradeCorrection,
                oneApi.EventType.tradeCancel,
                oneApi.EventType.tradeNonRegular
            ]
        });
    }
    const handle = currentSession.subscribe({ dataSourceId, symbologyId, symbol }, undefined, parameters);
    const entry = {
        subscriptionId,
        dataKind,
        remaining: count,
        fallbackTimeZone,
        parameters,
        handle,
        isExplicitlyClosed: false
    };
    subscriptions.set(subscriptionId, entry);
    return () => pumpSubscription(entry);
}

function removeSubscription(request) {
    const subscriptionId = requiredInteger(request, "subscription_id", 1, Number.MAX_SAFE_INTEGER);
    const entry = subscriptions.get(subscriptionId);
    if (entry == null)
        return;
    entry.isExplicitlyClosed = true;
    subscriptions.delete(subscriptionId);
    deleteSubscription(entry);
}

function closeAllSubscriptions() {
    for (const entry of subscriptions.values()) {
        entry.isExplicitlyClosed = true;
        deleteSubscription(entry);
    }
    subscriptions.clear();
}

async function execute(request) {
    switch (request.command) {
        case Commands.connect: {
            if (session != null)
                throw gatewayError("already_connected", "Disconnect the current ACTIV session first.");
            const host = requiredString(request, "host");
            const user = requiredString(request, "user");
            const password = requiredSecret(request, "password");
            isDisconnecting = false;
            session = await oneApi.connect({
                host,
                user,
                password,
                onLogMessage(logType, message) {
                    if (logType >= oneApi.LogType.warning) {
                        void send({
                            kind: MessageKinds.log,
                            log_level: Number(logType),
                            log_message: String(message)
                        });
                    }
                }
            });
            session.disconnected.catch((error) => {
                if (!isDisconnecting) {
                    void send({ kind: MessageKinds.error, error: normalizeError(error) });
                }
                closeAllSubscriptions();
                session = null;
            });
            return { gatewayVersion: GATEWAY_VERSION, oneApiVersion: ONE_API_VERSION };
        }
        case Commands.disconnect:
            if (session != null) {
                isDisconnecting = true;
                closeAllSubscriptions();
                session.disconnect();
                session = null;
            }
            return {};
        case Commands.lookup: {
            const currentSession = requireSession();
            const dataSourceId = requiredInteger(request, "data_source_id", 0, 65535);
            const query = requiredString(request, "query");
            const skip = optionalInteger(request, "skip", 0, 1000000) || 0;
            const limit = requiredInteger(request, "limit", 1, 100000);
            const fallbackTimeZone = optionalString(request, "fallback_time_zone") || "UTC";
            const handle = currentSession.querySnapshot({ dataSourceId, tagExpression: query });
            return { records: await collectSnapshot(handle, limit, skip, fallbackTimeZone, true) };
        }
        case Commands.snapshot:
            return { records: await canonicalSnapshot(request) };
        case Commands.historyTicks:
            return { records: await historyTicks(request) };
        case Commands.historyBars:
            return { records: await historyBars(request) };
        case Commands.subscribe:
            return { afterResponse: createSubscription(request) };
        case Commands.unsubscribe:
            removeSubscription(request);
            return {};
        default:
            throw gatewayError("unsupported_command", `Unsupported gateway command ${request.command}.`);
    }
}

async function handleLine(line) {
    let requestId = 0;
    try {
        if (line.length > MAX_MESSAGE_LENGTH)
            throw gatewayError("frame_too_large", "Gateway request exceeds the 16 MiB protocol limit.");
        const request = JSON.parse(line);
        if (request == null || Array.isArray(request) || typeof request !== "object")
            throw gatewayError("invalid_request", "Gateway request must be a JSON object.");
        requestId = requiredInteger(request, "request_id", 1, Number.MAX_SAFE_INTEGER);
        if (request.version !== PROTOCOL_VERSION)
            throw gatewayError("unsupported_version", `Unsupported protocol version ${request.version}.`);
        requiredInteger(request, "command", Commands.connect, Commands.unsubscribe);

        const result = await execute(request);
        await send({
            kind: MessageKinds.response,
            request_id: requestId,
            gateway_version: result.gatewayVersion,
            one_api_version: result.oneApiVersion,
            records: result.records
        });
        if (result.afterResponse != null)
            setImmediate(() => void result.afterResponse());
    } catch (error) {
        await send({
            kind: MessageKinds.response,
            request_id: requestId,
            error: normalizeError(error)
        });
    }
}

const input = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
input.on("line", (line) => {
    if (line.charCodeAt(0) === 0xFEFF)
        line = line.slice(1);
    if (line.length === 0)
        return;
    commandQueue = commandQueue.catch(() => undefined).then(() => handleLine(line));
});

input.on("close", () => {
    closeAllSubscriptions();
    if (session != null) {
        isDisconnecting = true;
        session.disconnect();
        session = null;
    }
});

process.on("uncaughtException", (error) => {
    void send({ kind: MessageKinds.error, error: normalizeError(error) });
});

process.on("unhandledRejection", (error) => {
    void send({ kind: MessageKinds.error, error: normalizeError(error) });
});
