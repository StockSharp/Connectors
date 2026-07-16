global using global::System;
global using global::System.Buffers.Binary;
global using global::System.Collections.Concurrent;
global using global::System.Collections.Generic;
global using global::System.ComponentModel.DataAnnotations;
global using global::System.Globalization;
global using global::System.IO;
global using global::System.IO.Compression;
global using global::System.Linq;
global using global::System.Net;
global using global::System.Net.Http;
global using global::System.Net.Http.Headers;
global using global::System.Net.WebSockets;
global using global::System.Runtime.Serialization;
global using global::System.Security;
global using global::System.Security.Cryptography;
global using global::System.Text;
global using global::System.Threading;
global using global::System.Threading.Tasks;

global using global::Ecng.Collections;
global using global::Ecng.Common;
global using global::Ecng.ComponentModel;
global using global::Ecng.Logging;
global using global::Ecng.Serialization;

global using global::Google.Protobuf;
global using global::Newtonsoft.Json;

global using global::StockSharp.Localization;
global using global::StockSharp.Longbridge.Native;
global using global::StockSharp.Longbridge.Native.Model;
global using global::StockSharp.Longbridge.Native.Protos.Control;
global using global::StockSharp.Longbridge.Native.Protos.Quote;
global using global::StockSharp.Longbridge.Native.Protos.Trade;
global using global::StockSharp.Messages;

global using LongbridgeControlCommand = global::StockSharp.Longbridge.Native.Protos.Control.Command;
global using LongbridgeQuoteCommand = global::StockSharp.Longbridge.Native.Protos.Quote.Command;
global using LongbridgeTradeCommand = global::StockSharp.Longbridge.Native.Protos.Trade.Command;
global using LongbridgeControlError = global::StockSharp.Longbridge.Native.Protos.Control.Error;
global using LongbridgeTradeNotification = global::StockSharp.Longbridge.Native.Protos.Trade.Notification;
global using LongbridgeHeartbeat = global::StockSharp.Longbridge.Native.Protos.Control.Heartbeat;
global using LongbridgeQuoteTrade = global::StockSharp.Longbridge.Native.Protos.Quote.Trade;
global using ProtobufMessage = global::Google.Protobuf.IMessage;
global using DataType = global::StockSharp.Messages.DataType;
global using TimeInForce = global::StockSharp.Messages.TimeInForce;
