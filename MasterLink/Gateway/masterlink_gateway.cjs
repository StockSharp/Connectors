"use strict";

process.env.TZ = "Asia/Taipei";

const readline = require("node:readline");
const taishin = require("taishin-sdk");
const sdkPackage = require("taishin-sdk/package.json");

const PROTOCOL_VERSION = 1;
const GATEWAY_VERSION = "1.0.0";
const MAX_MESSAGE_LENGTH = 16 * 1024 * 1024;

const MessageKinds = Object.freeze({
    response: 1,
    marketData: 2,
    order: 3,
    fill: 4,
    error: 5,
    disconnected: 6,
    log: 7
});

let sdk = null;
let account = null;
let accounts = [];
let stockSocket = null;
let isDisconnecting = false;
let writeQueue = Promise.resolve();
let commandQueue = Promise.resolve();

const subscriptions = new Map();
const nativeSubscriptions = new Map();

function stderr(level, values) {
    const text = values.map(value => {
        if (value instanceof Error)
            return value.stack || value.message;
        if (typeof value === "string")
            return value;
        try {
            return JSON.stringify(normalize(value));
        } catch {
            return String(value);
        }
    }).join(" ");
    process.stderr.write(`[${level}] ${text}\n`);
}

console.log = (...values) => stderr("info", values);
console.warn = (...values) => stderr("warn", values);
console.error = (...values) => stderr("error", values);

function send(message) {
    let line;
    try {
        line = JSON.stringify({ version: PROTOCOL_VERSION, ...normalize(message) }) + "\n";
    } catch (error) {
        return Promise.reject(error);
    }
    if (line.length > MAX_MESSAGE_LENGTH)
        return Promise.reject(new Error("Gateway response exceeds the 16 MiB protocol limit."));

    writeQueue = writeQueue.catch(() => undefined).then(() =>
        new Promise((resolve, reject) => {
            if (process.stdout.write(line, "utf8")) {
                resolve();
                return;
            }
            const onDrain = () => {
                process.stdout.off("error", onError);
                resolve();
            };
            const onError = error => {
                process.stdout.off("drain", onDrain);
                reject(error);
            };
            process.stdout.once("drain", onDrain);
            process.stdout.once("error", onError);
        }));
    return writeQueue;
}

function normalize(value, seen = new WeakSet(), depth = 0) {
    if (value == null || typeof value === "string" ||
        typeof value === "number" || typeof value === "boolean")
        return value;
    if (typeof value === "bigint")
        return value.toString();
    if (typeof value === "function" || typeof value === "symbol")
        return undefined;
    if (value instanceof Date)
        return value.toISOString();
    if (value instanceof Error) {
        return {
            name: value.name,
            code: value.code == null ? undefined : String(value.code),
            message: value.message || String(value)
        };
    }
    if (Buffer.isBuffer(value) || value instanceof Uint8Array)
        return Buffer.from(value).toString("base64");
    if (depth > 24)
        return "[maximum depth]";
    if (typeof value !== "object")
        return String(value);
    if (seen.has(value))
        return "[circular]";
    seen.add(value);
    try {
        if (Array.isArray(value))
            return value.map(item => normalize(item, seen, depth + 1));
        if (typeof value.toJSON === "function") {
            const json = value.toJSON();
            if (json !== value)
                return normalize(json, seen, depth + 1);
        }
        const output = {};
        for (const key of Object.keys(value)) {
            const item = normalize(value[key], seen, depth + 1);
            if (item !== undefined)
                output[key] = item;
        }
        return output;
    } finally {
        seen.delete(value);
    }
}

function gatewayError(code, message) {
    const error = new Error(message);
    error.gatewayCode = code;
    return error;
}

function normalizeError(error) {
    if (error && typeof error === "object") {
        const status = Number(error.statusCode ?? error.status ??
            error.response?.status ?? error.code);
        const code = error.gatewayCode ||
            (Number.isFinite(status) ? `http_${status}` :
                error.code == null ? "gateway_error" : String(error.code));
        const message = typeof error.message === "string" && error.message.length > 0
            ? error.message
            : String(error);
        return { code, message };
    }
    return { code: "gateway_error", message: String(error || "Unknown gateway error.") };
}

function requireSession() {
    if (sdk == null || account == null)
        throw gatewayError("not_connected", "Connect to Taishin Nova API first.");
    return sdk;
}

function dataObject(request) {
    return request && typeof request.data === "object" && request.data != null
        ? request.data
        : {};
}

function requiredString(data, name) {
    const value = data[name];
    if (typeof value !== "string" || value.trim().length === 0)
        throw gatewayError("invalid_request", `${name} must be a non-empty string.`);
    return value.trim();
}

function optionalString(data, name) {
    const value = data[name];
    if (value == null)
        return undefined;
    if (typeof value !== "string")
        throw gatewayError("invalid_request", `${name} must be a string.`);
    return value.trim();
}

function requiredInteger(data, name, minimum, maximum) {
    const value = Number(data[name]);
    if (!Number.isSafeInteger(value) || value < minimum || value > maximum)
        throw gatewayError("invalid_request",
            `${name} must be an integer between ${minimum} and ${maximum}.`);
    return value;
}

function enumValue(values, name, field) {
    const value = values[name];
    if (value == null)
        throw gatewayError("invalid_request", `${field} has unsupported value '${name}'.`);
    return value;
}

function sleep(milliseconds) {
    return new Promise(resolve => setTimeout(resolve, milliseconds));
}

async function withRetry(action) {
    for (let attempt = 0; ; attempt++) {
        try {
            return await action();
        } catch (error) {
            const status = Number(error?.statusCode ?? error?.status ??
                error?.response?.status ?? error?.code);
            if (attempt >= 2 || (status !== 429 && !(status >= 500 && status <= 599)))
                throw error;
            await sleep(250 * (attempt + 1));
        }
    }
}

function callbackValue(args) {
    if (args.length >= 2) {
        if (args[0] != null)
            throw args[0] instanceof Error ? args[0] : new Error(String(args[0]));
        return args[1];
    }
    return args[0];
}

function emitSdkCallback(kind, args) {
    try {
        const value = callbackValue(args);
        if (value != null)
            void send({ kind, data: value });
    } catch (error) {
        void send({ kind: MessageKinds.error, error: normalizeError(error) });
    }
}

async function connect(data) {
    if (sdk != null)
        throw gatewayError("already_connected", "Taishin Nova API is already connected.");

    const personalId = requiredString(data, "personal_id");
    const password = requiredString(data, "password");
    const certificatePath = requiredString(data, "certificate_path");
    const certificatePassword = optionalString(data, "certificate_password");
    const selectedAccount = optionalString(data, "account");

    const instance = new taishin.TaishinSDK();
    let loginAccounts;
    try {
        loginAccounts = instance.login(
            personalId, password, certificatePath, certificatePassword);
    } catch (error) {
        throw gatewayError("login_failed",
            `Taishin Nova login failed: ${normalizeError(error).message}`);
    }
    if (!Array.isArray(loginAccounts) || loginAccounts.length === 0)
        throw gatewayError("account_not_found", "Taishin Nova login returned no accounts.");

    const stockAccounts = loginAccounts.filter(item =>
        String(item?.accountType || "").toLowerCase() === "stk");
    const candidates = stockAccounts.length > 0 ? stockAccounts : loginAccounts;
    const selected = selectedAccount
        ? candidates.find(item =>
            String(item?.account || "").toLowerCase() === selectedAccount.toLowerCase())
        : candidates[0];
    if (selected == null)
        throw gatewayError("account_not_found",
            `Taishin Nova account '${selectedAccount}' was not returned by login.`);

    if (Boolean(data.register_api_auth)) {
        const registered = instance.registerApiAuth(selected);
        if (registered !== true)
            throw gatewayError("registration_failed",
                "Taishin Nova registerApiAuth did not report success.");
    }

    instance.setOnOrder((...args) =>
        emitSdkCallback(MessageKinds.order, args));
    instance.setOnFilled((...args) =>
        emitSdkCallback(MessageKinds.fill, args));
    instance.setOnError((...args) => {
        try {
            const value = callbackValue(args);
            void send({
                kind: MessageKinds.error,
                error: normalizeError(value instanceof Error ? value : new Error(String(value)))
            });
        } catch (error) {
            void send({ kind: MessageKinds.error, error: normalizeError(error) });
        }
    });
    instance.setOnDisconnected((...args) => {
        if (isDisconnecting)
            return;
        let value;
        try {
            value = callbackValue(args);
        } catch (error) {
            value = error;
        }
        void send({
            kind: MessageKinds.disconnected,
            error: normalizeError(value instanceof Error ? value :
                new Error(String(value || "Trading report connection was lost.")))
        });
    });

    instance.connectWebsocket();
    const mode = data.mode === "Speed" ? taishin.Mode.Speed : taishin.Mode.Normal;
    instance.initRealtime(selected, mode);
    const marketSocket = instance.marketdata?.webSocketClient?.stock;
    if (marketSocket == null)
        throw gatewayError("market_data_unavailable",
            "Taishin Nova SDK did not initialize the stock market-data client.");

    marketSocket.on("message", onMarketMessage);
    marketSocket.on("error", onMarketError);
    marketSocket.on("disconnect", onMarketDisconnect);
    await marketSocket.connect();

    sdk = instance;
    account = selected;
    accounts = loginAccounts;
    stockSocket = marketSocket;
    isDisconnecting = false;

    return {
        gatewayVersion: GATEWAY_VERSION,
        sdkVersion: sdkPackage.version || "unknown",
        account: selected,
        accounts: loginAccounts
    };
}

async function disconnect() {
    isDisconnecting = true;
    const socket = stockSocket;
    stockSocket = null;
    if (socket != null) {
        socket.off?.("message", onMarketMessage);
        socket.off?.("error", onMarketError);
        socket.off?.("disconnect", onMarketDisconnect);
        try {
            socket.disconnect();
        } catch {
        }
    }
    subscriptions.clear();
    nativeSubscriptions.clear();
    sdk = null;
    account = null;
    accounts = [];
    return { disconnected: true };
}

function onMarketError(value) {
    let error = value;
    if (typeof value === "string") {
        try {
            const parsed = JSON.parse(value);
            error = parsed?.data || parsed;
        } catch {
        }
    }
    void send({
        kind: MessageKinds.error,
        error: normalizeError(error instanceof Error ? error :
            new Error(error?.message || String(error)))
    });
}

function onMarketDisconnect(value) {
    if (isDisconnecting)
        return;
    void send({
        kind: MessageKinds.disconnected,
        error: normalizeError(new Error(
            value?.message || String(value || "Market-data connection was lost.")))
    });
}

function subscriptionKey(channel, symbol, oddLot) {
    return `${channel}|${String(symbol).toUpperCase()}|${oddLot ? "1" : "0"}`;
}

function findPendingSubscription(channel, symbol) {
    const normalizedSymbol = String(symbol || "").toUpperCase();
    for (const entry of subscriptions.values()) {
        if (entry.pending && entry.channel === channel &&
            entry.symbol.toUpperCase() === normalizedSymbol)
            return entry;
    }
    return null;
}

function registerNativeSubscription(item) {
    if (item == null || typeof item !== "object")
        return;
    const entry = findPendingSubscription(item.channel, item.symbol);
    if (entry == null)
        return;
    entry.pending = false;
    entry.nativeId = String(item.id || "");
    if (entry.nativeId)
        nativeSubscriptions.set(entry.nativeId, entry);
    if (entry.removeAfterAck || entry.requests.size === 0)
        removeNativeSubscription(entry);
}

function removeNativeSubscription(entry) {
    if (entry.nativeId) {
        nativeSubscriptions.delete(entry.nativeId);
        try {
            stockSocket?.unsubscribe({ id: entry.nativeId });
        } catch {
        }
    }
    subscriptions.delete(entry.key);
}

function onMarketMessage(raw) {
    let message;
    try {
        message = typeof raw === "string" ? JSON.parse(raw) : raw;
    } catch (error) {
        void send({ kind: MessageKinds.error, error: normalizeError(error) });
        return;
    }
    if (message?.event === "subscribed") {
        const items = Array.isArray(message.data) ? message.data : [message.data];
        for (const item of items)
            registerNativeSubscription(item);
        return;
    }
    if (message?.event === "error") {
        void send({
            kind: MessageKinds.error,
            error: {
                code: String(message.data?.code || "market_data_error"),
                message: message.data?.message || "Taishin market-data WebSocket error."
            }
        });
        return;
    }
    if (message?.event !== "data" || message.data == null)
        return;

    let entries = [];
    const nativeId = message.id == null ? "" : String(message.id);
    const mapped = nativeSubscriptions.get(nativeId);
    if (mapped != null) {
        entries = [mapped];
    } else {
        const channel = String(message.channel || "");
        const symbol = String(message.data.symbol || "").toUpperCase();
        entries = [...subscriptions.values()].filter(entry =>
            entry.channel === channel &&
            entry.symbol.toUpperCase() === symbol);
    }

    for (const entry of entries) {
        for (const subscriptionId of entry.requests) {
            void send({
                kind: MessageKinds.marketData,
                subscription_id: subscriptionId,
                channel: message.channel || entry.channel,
                data: message.data
            });
        }
    }
}

async function lookup(data) {
    const instance = requireSession();
    const rest = instance.marketdata?.restClient?.stock;
    if (rest == null)
        throw gatewayError("market_data_unavailable", "Stock REST client is unavailable.");

    const query = optionalString(data, "query") || "";
    const board = (optionalString(data, "board") || "").toUpperCase();
    const limit = requiredInteger(data, "limit", 1, 100000);
    const exactSymbols = query.split(/[,;\s]+/u)
        .map(value => value.trim())
        .filter(value => /^[0-9A-Z][0-9A-Z.-]{1,15}$/iu.test(value));

    if (exactSymbols.length > 0 && exactSymbols.length <= 50 &&
        exactSymbols.join("").length === query.replace(/[,;\s]+/gu, "").length) {
        const result = [];
        for (const symbol of exactSymbols) {
            try {
                result.push(await withRetry(() =>
                    rest.intraday.ticker({ symbol })));
            } catch (error) {
                const status = Number(error?.statusCode ?? error?.status ??
                    error?.response?.status);
                if (status !== 404)
                    throw error;
            }
            if (result.length >= limit)
                break;
        }
        return result;
    }

    const requests = [];
    if (board === "TWSE" || board === "TWSEODD") {
        requests.push({ exchange: "TWSE" });
    } else if (board === "TPEX" || board === "TPEXODD") {
        requests.push({ exchange: "TPEx", market: "OTC" });
    } else if (board === "TWEMERGING") {
        requests.push({ exchange: "TPEx", market: "ESB" });
        requests.push({ exchange: "TPEx", market: "PSB" });
    } else {
        requests.push({ exchange: "TWSE" });
        requests.push({ exchange: "TPEx" });
    }

    const output = [];
    const emitted = new Set();
    const normalizedQuery = query.toUpperCase();
    for (const request of requests) {
        const response = await withRetry(() =>
            rest.intraday.tickers({ type: "EQUITY", ...request }));
        for (const item of response?.data || []) {
            if (normalizedQuery &&
                !String(item.symbol || "").toUpperCase().includes(normalizedQuery) &&
                !String(item.name || "").toUpperCase().includes(normalizedQuery))
                continue;
            const key = `${response.exchange}|${response.market || request.market || ""}|${item.symbol}`;
            if (emitted.has(key))
                continue;
            emitted.add(key);
            output.push({
                date: response.date,
                type: response.type,
                exchange: response.exchange,
                market: response.market || request.market,
                symbol: item.symbol,
                name: item.name,
                securityType: "EQUITY",
                boardLot: 1000,
                tradingCurrency: "TWD"
            });
            if (output.length >= limit)
                return output;
        }
    }
    return output;
}

async function ticker(data) {
    const rest = requireSession().marketdata?.restClient?.stock;
    const symbol = requiredString(data, "symbol");
    return withRetry(() => rest.intraday.ticker({
        symbol,
        type: data.odd_lot ? "oddlot" : undefined
    }));
}

async function quote(data) {
    const rest = requireSession().marketdata?.restClient?.stock;
    const symbol = requiredString(data, "symbol");
    return withRetry(() => rest.intraday.quote({
        symbol,
        type: data.odd_lot ? "oddlot" : undefined
    }));
}

async function trades(data) {
    const rest = requireSession().marketdata?.restClient?.stock;
    const symbol = requiredString(data, "symbol");
    const limit = requiredInteger(data, "limit", 1, 500);
    const response = await withRetry(() => rest.intraday.trades({
        symbol,
        type: data.odd_lot ? "oddlot" : undefined,
        offset: 0,
        limit,
        isTrial: false
    }));
    return response?.data || [];
}

async function candles(data) {
    const rest = requireSession().marketdata?.restClient?.stock;
    const symbol = requiredString(data, "symbol");
    const timeframe = requiredString(data, "timeframe");
    return withRetry(() => rest.historical.candles({
        symbol,
        from: optionalString(data, "from"),
        to: optionalString(data, "to"),
        timeframe,
        sort: "asc",
        adjusted: Boolean(data.adjusted)
    }));
}

async function subscribe(data) {
    requireSession();
    if (stockSocket == null)
        throw gatewayError("market_data_unavailable", "Stock WebSocket is unavailable.");
    const subscriptionId = requiredInteger(
        data, "subscription_id", 1, Number.MAX_SAFE_INTEGER);
    const dataKind = requiredString(data, "data_kind");
    const symbol = requiredString(data, "symbol");
    const oddLot = Boolean(data.odd_lot);
    const channels = {
        level1: ["trades", "books", "aggregates"],
        ticks: ["trades"],
        depth: ["books"],
        candles: ["candles"]
    }[dataKind];
    if (channels == null)
        throw gatewayError("invalid_request", `Unsupported market-data kind '${dataKind}'.`);

    for (const channel of channels) {
        const key = subscriptionKey(channel, symbol, oddLot);
        let entry = subscriptions.get(key);
        if (entry == null) {
            entry = {
                key,
                channel,
                symbol,
                oddLot,
                nativeId: null,
                pending: true,
                removeAfterAck: false,
                requests: new Set()
            };
            subscriptions.set(key, entry);
            stockSocket.subscribe({
                channel,
                symbol,
                intradayOddLot: oddLot || undefined
            });
        }
        entry.requests.add(subscriptionId);
    }
    return { subscriptionId };
}

async function unsubscribe(data) {
    const subscriptionId = requiredInteger(
        data, "subscription_id", 1, Number.MAX_SAFE_INTEGER);
    for (const entry of [...subscriptions.values()]) {
        if (!entry.requests.delete(subscriptionId))
            continue;
        if (entry.requests.size > 0)
            continue;
        if (entry.pending)
            entry.removeAfterAck = true;
        else
            removeNativeSubscription(entry);
    }
    return { subscriptionId };
}

function resolveQueryType(value) {
    const name = value || "All";
    return enumValue(taishin.QueryType, name, "query_type");
}

function getOrders(data = {}) {
    const instance = requireSession();
    return instance.stock.getOrderResults(
        account,
        resolveQueryType(data.query_type),
        optionalString(data, "symbol"));
}

function findOrder(data) {
    const orderNo = requiredString(data, "order_no");
    const sequenceNo = optionalString(data, "sequence_no");
    const orders = getOrders({});
    const order = orders.find(item =>
        String(item.orderNo || "").toLowerCase() === orderNo.toLowerCase() &&
        (!sequenceNo || String(item.seqNo || "") === sequenceNo));
    if (order == null)
        throw gatewayError("order_not_found", `Order '${orderNo}' was not found.`);
    return order;
}

function placeOrder(data) {
    const instance = requireSession();
    const priceType = enumValue(
        taishin.PriceType, requiredString(data, "price_type"), "price_type");
    const order = {
        buySell: enumValue(
            taishin.BSAction, requiredString(data, "buy_sell"), "buy_sell"),
        symbol: requiredString(data, "symbol"),
        quantity: requiredInteger(data, "quantity", 1, 499000),
        marketType: enumValue(
            taishin.MarketType, requiredString(data, "market_type"), "market_type"),
        priceType,
        timeInForce: enumValue(
            taishin.TimeInForce,
            requiredString(data, "time_in_force"),
            "time_in_force"),
        orderType: enumValue(
            taishin.OrderType, requiredString(data, "order_type"), "order_type")
    };
    const price = optionalString(data, "price");
    if (price != null && price.length > 0)
        order.price = price;
    return instance.stock.placeOrder(account, order);
}

function modifyPrice(data) {
    const instance = requireSession();
    const order = findOrder(data);
    return instance.stock.modifyPrice(
        account,
        order,
        requiredString(data, "price"),
        enumValue(
            taishin.PriceType,
            requiredString(data, "price_type"),
            "price_type"));
}

function modifyVolume(data) {
    const instance = requireSession();
    const order = findOrder(data);
    const quantity = requiredInteger(
        data, "decrease_quantity", 1, Math.max(1, order.orgQty));
    return instance.stock.modifyVolume(account, order, quantity);
}

function cancelOrder(data) {
    const instance = requireSession();
    const order = findOrder(data);
    return instance.stock.modifyVolume(account, order, 0);
}

function fills(data = {}) {
    return requireSession().stock.getFilled(
        account, optionalString(data, "symbol"));
}

function optionalCall(action, fallback, operation) {
    try {
        return action();
    } catch (error) {
        void send({
            kind: MessageKinds.log,
            log_level: 2,
            log_message: `${operation}: ${normalizeError(error).message}`
        });
        return fallback;
    }
}

function portfolio() {
    const instance = requireSession();
    return {
        inventory: instance.accounting.inventories(account),
        pnl: optionalCall(
            () => instance.accounting.accountTotalPnl(account),
            null,
            "Account PnL query failed"),
        bankBalances: optionalCall(
            () => instance.accounting.bankBalance(account),
            [],
            "Bank balance query failed")
    };
}

function ping() {
    requireSession();
    stockSocket?.ping({ state: Date.now() });
    return { ok: true };
}

async function dispatch(command, data) {
    switch (command) {
        case "connect":
            return connect(data);
        case "disconnect":
            return disconnect();
        case "lookup":
            return lookup(data);
        case "ticker":
            return ticker(data);
        case "quote":
            return quote(data);
        case "trades":
            return trades(data);
        case "candles":
            return candles(data);
        case "subscribe":
            return subscribe(data);
        case "unsubscribe":
            return unsubscribe(data);
        case "orders":
            return getOrders(data);
        case "fills":
            return fills(data);
        case "place_order":
            return placeOrder(data);
        case "modify_price":
            return modifyPrice(data);
        case "modify_volume":
            return modifyVolume(data);
        case "cancel_order":
            return cancelOrder(data);
        case "portfolio":
            return portfolio();
        case "ping":
            return ping();
        default:
            throw gatewayError("unsupported_command", `Unsupported command '${command}'.`);
    }
}

async function handleLine(line) {
    let request;
    try {
        if (line.length > MAX_MESSAGE_LENGTH)
            throw gatewayError("message_too_large", "Gateway request exceeds 16 MiB.");
        request = JSON.parse(line);
        if (request.version !== PROTOCOL_VERSION)
            throw gatewayError("unsupported_version",
                `Unsupported gateway protocol version '${request.version}'.`);
        if (!Number.isSafeInteger(request.request_id) || request.request_id <= 0)
            throw gatewayError("invalid_request", "request_id must be a positive integer.");
        if (typeof request.command !== "string" || request.command.length === 0)
            throw gatewayError("invalid_request", "command must be a non-empty string.");
        const result = await dispatch(request.command, dataObject(request));
        await send({
            kind: MessageKinds.response,
            request_id: request.request_id,
            data: result == null ? null : result
        });
    } catch (error) {
        await send({
            kind: MessageKinds.response,
            request_id: Number.isSafeInteger(request?.request_id)
                ? request.request_id
                : 0,
            error: normalizeError(error)
        });
    }
}

const input = readline.createInterface({
    input: process.stdin,
    crlfDelay: Infinity,
    terminal: false
});

input.on("line", line => {
    if (line.length === 0)
        return;
    commandQueue = commandQueue
        .catch(() => undefined)
        .then(() => handleLine(line))
        .catch(error => stderr("fatal", [error]));
});

input.on("close", () => {
    isDisconnecting = true;
    try {
        stockSocket?.disconnect();
    } catch {
    }
});

process.on("uncaughtException", error => {
    void send({ kind: MessageKinds.error, error: normalizeError(error) });
});

process.on("unhandledRejection", error => {
    void send({ kind: MessageKinds.error, error: normalizeError(error) });
});
