namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.ComponentModel;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.Shoonya;
using StockSharp.Shoonya.Native;
using StockSharp.Shoonya.Native.Model;
using StockSharp.Zebu;
using StockSharp.Zebu.Native;

[TestClass]
public class ZebuTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsOAuthAndNorenConfiguration()
    {
        var expires = new DateTime(
            2026,
            7,
            26,
            12,
            0,
            0,
            DateTimeKind.Utc);
        var source = new ZebuMessageAdapter(new IncrementalIdGenerator())
        {
            Key = "CLIENT-ID".Secure(),
            Secret = "CLIENT-SECRET".Secure(),
            AuthorizationCode = "AUTH-CODE".Secure(),
            RefreshToken = "REFRESH".Secure(),
            Token = "ACCESS".Secure(),
            UserId = "ZP001",
            AccountId = "ZP001-A",
            TokenExpiresAt = expires,
            DefaultProduct = ShoonyaProducts.Normal,
            ReconnectAttempts = 7,
            AuthorizationAddress =
                new("https://oauth.example.test/authorize"),
            RestEndpoint = "https://rest.example.test/",
            InstrumentEndpointTemplate =
                "https://static.example.test/{0}.zip",
            WebSocketEndpoint = "wss://stream.example.test/",
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new ZebuMessageAdapter(new IncrementalIdGenerator());
        target.Load(storage);

        AreEqual(source.Key.UnSecure(), target.Key.UnSecure());
        AreEqual(source.Secret.UnSecure(), target.Secret.UnSecure());
        AreEqual(
            source.AuthorizationCode.UnSecure(),
            target.AuthorizationCode.UnSecure());
        AreEqual(
            source.RefreshToken.UnSecure(),
            target.RefreshToken.UnSecure());
        AreEqual(source.Token.UnSecure(), target.Token.UnSecure());
        AreEqual(source.UserId, target.UserId);
        AreEqual(source.AccountId, target.AccountId);
        AreEqual(source.TokenExpiresAt, target.TokenExpiresAt);
        AreEqual(source.DefaultProduct, target.DefaultProduct);
        AreEqual(source.ReconnectAttempts, target.ReconnectAttempts);
        AreEqual(
            source.AuthorizationAddress,
            target.AuthorizationAddress);
        AreEqual(source.RestEndpoint, target.RestEndpoint);
        AreEqual(
            source.InstrumentEndpointTemplate,
            target.InstrumentEndpointTemplate);
        AreEqual(source.WebSocketEndpoint, target.WebSocketEndpoint);
        IsTrue(target is IKeySecretAdapter);
        IsTrue(target is ITokenAdapter);
    }

    [TestMethod]
    public void DefaultsAndAuthorizationUriUseOfficialOAuthEndpoints()
    {
        var adapter = new ZebuMessageAdapter(new IncrementalIdGenerator())
        {
            Key = "ZP00 1/U".Secure(),
        };

        AreEqual(
            "https://go.mynt.in/NorenWClientAPI/",
            adapter.RestEndpoint);
        AreEqual(
            "https://go.mynt.in/{0}_symbols.txt.zip",
            adapter.InstrumentEndpointTemplate);
        AreEqual(
            "wss://go.mynt.in/NorenWSAPI/",
            adapter.WebSocketEndpoint);
        AreEqual(
            "https://go.mynt.in/OAuthlogin/authorize/oauth?client_id=ZP00%201%2FU",
            adapter.CreateAuthorizationUri().AbsoluteUri);
        IsTrue(ShoonyaMessageAdapter.AllTimeFrames.Any());
    }

    [TestMethod]
    public async Task AuthorizationCodeExchangeUsesPublishedChecksum()
    {
        var handler = new CaptureHandler(
            new ResponseSpec(
                """
				{
				  "stat":"Ok",
				  "access_token":"ACCESS",
				  "refresh_token":"REFRESH",
				  "expires_in":"3600",
				  "uid":"ZP001",
				  "actid":"ZP001-A"
				}
				"""));
        using var client = new ZebuOAuthClient(
            new("https://go.example.test/NorenWClientAPI/"),
            handler);

        var result = await client.ExchangeCode(
            "ABC".Secure(),
            "123".Secure(),
            "x1y2z3".Secure(),
            CancellationToken.None);

        AreEqual(
            "7b482d7b380a3067eaba4c9c909b19253c4fa0edb5833e246401b8497c99a9c3",
            ZebuOAuthClient.ComputeChecksum("ABC", "123", "x1y2z3"));
        AreEqual("ACCESS", result.AccessToken);
        AreEqual("REFRESH", result.RefreshToken);
        AreEqual("ZP001", result.UserId);
        AreEqual("ZP001-A", result.AccountId);
        AreEqual(3600, result.ExpiresIn);

        var request = handler.Requests.Single();
        AreEqual(
            "https://go.example.test/NorenWClientAPI/GenAcsTok",
            request.Uri.AbsoluteUri);
        AreEqual("text/plain; charset=utf-8", request.ContentType);
        IsNull(request.Authorization);
        var body = JObject.Parse(request.Body["jData=".Length..]);
        AreEqual("x1y2z3", body["code"].Value<string>());
        AreEqual(
            "7b482d7b380a3067eaba4c9c909b19253c4fa0edb5833e246401b8497c99a9c3",
            body["checksum"].Value<string>());
    }

    [TestMethod]
    public async Task RefreshAcceptsCurrentSessionTokenAlias()
    {
        var handler = new CaptureHandler(
            new ResponseSpec(
                """
				{"stat":"Ok","susertoken":"ACCESS-2",
				 "refresh_token":"REFRESH-2","expires_in":"1800","uid":"ZP002"}
				"""));
        using var client = new ZebuOAuthClient(
            new("https://go.example.test/NorenWClientAPI/"),
            handler);

        var result = await client.Refresh(
            "REFRESH-1".Secure(),
            CancellationToken.None);

        AreEqual("ACCESS-2", result.AccessToken);
        AreEqual("REFRESH-2", result.RefreshToken);
        AreEqual("ZP002", result.AccountId);
        AreEqual(
            "https://go.example.test/NorenWClientAPI/RefreshToken",
            handler.Requests[0].Uri.AbsoluteUri);
        AreEqual(
            "REFRESH-1",
            JObject.Parse(
                handler.Requests[0].Body["jData=".Length..])
                ["refresh_token"]
                .Value<string>());
    }

    [TestMethod]
    public void OAuthErrorsAndInvalidJsonAreRejected()
    {
        ThrowsExactly<InvalidOperationException>(() =>
            ZebuOAuthClient.ParseToken(
                "GenAcsTok",
                """{"stat":"Not_Ok","emsg":"Invalid authorization code"}"""));
        ThrowsExactly<InvalidDataException>(() =>
            ZebuOAuthClient.ParseToken("GenAcsTok", "<html>"));
        ThrowsExactly<InvalidOperationException>(() =>
            ZebuOAuthClient.ParseToken(
                "GenAcsTok",
                """{"stat":"Ok","uid":"ZP001"}"""));
    }

    [TestMethod]
    public async Task BearerTransportUsesRawJDataApi2AndEncodedSymbol()
    {
        var handler = new CaptureHandler(
            new ResponseSpec(
                """{"stat":"Ok","norenordno":"ORDER-1"}"""));
        using var client = new ShoonyaRestClient(
            "ZP001",
            "ZP001",
            "ACCESS".Secure(),
            "https://go.example.test/NorenWClientAPI/",
            "https://static.example.test/{0}.zip",
            true,
            handler);

        var orderId = await client.PlaceOrder(
            Order("M&M-EQ"),
            CancellationToken.None);

        AreEqual("ORDER-1", orderId);
        var request = handler.Requests.Single();
        AreEqual("Bearer ACCESS", request.Authorization);
        AreEqual("text/plain; charset=utf-8", request.ContentType);
        IsFalse(request.Body.Contains("jKey="));
        var body = JObject.Parse(request.Body["jData=".Length..]);
        AreEqual("API2", body["ordersource"].Value<string>());
        AreEqual("M%26M-EQ", body["tsym"].Value<string>());
        AreEqual("ZP001", body["uid"].Value<string>());
        AreEqual("10", body["qty"].Value<string>());
    }

    [TestMethod]
    public async Task LegacyTransportKeepsFormJKeyProtocol()
    {
        var handler = new CaptureHandler(
            new ResponseSpec(
                """{"stat":"Ok","norenordno":"ORDER-2"}"""));
        using var client = new ShoonyaRestClient(
            "FA001",
            "FA001",
            "SESSION".Secure(),
            "https://api.example.test/NorenWClientTP/",
            "https://static.example.test/{0}.zip",
            false,
            handler);

        await client.PlaceOrder(
            Order("M&M-EQ"),
            CancellationToken.None);

        var request = handler.Requests.Single();
        IsNull(request.Authorization);
        AreEqual(
            "application/x-www-form-urlencoded; charset=utf-8",
            request.ContentType);
        IsTrue(request.Body.Contains("&jKey=SESSION"));
        IsTrue(
            Uri.UnescapeDataString(request.Body)
                .Contains("\"ordersource\":\"API\""));
        IsTrue(
            Uri.UnescapeDataString(request.Body)
                .Contains("\"tsym\":\"M&M-EQ\""));
    }

    [TestMethod]
    public void SocketLoginPayloadSwitchesBetweenOAuthAndSessionModes()
    {
        using var oauth = new ShoonyaSocketClient(
            "ZP001",
            "ZP001-A",
            "ACCESS".Secure(),
            true,
            3,
            new WorkingTime(),
            "wss://go.example.test/NorenWSAPI/",
            true);
        using var legacy = new ShoonyaSocketClient(
            "FA001",
            "FA001",
            "SESSION".Secure(),
            true,
            3,
            new WorkingTime(),
            "wss://api.example.test/NorenWSTP/",
            false);

        var oauthPayload = JObject.Parse(oauth.CreateLoginPayload());
        AreEqual("a", oauthPayload["t"].Value<string>());
        AreEqual("ACCESS", oauthPayload["accesstoken"].Value<string>());
        IsNull(oauthPayload["susertoken"]);
        AreEqual("API", oauthPayload["source"].Value<string>());

        var legacyPayload = JObject.Parse(legacy.CreateLoginPayload());
        AreEqual("c", legacyPayload["t"].Value<string>());
        AreEqual("SESSION", legacyPayload["susertoken"].Value<string>());
        IsNull(legacyPayload["accesstoken"]);
    }

    [TestMethod]
    public async Task PublishedMasterArchivesMapAllSegmentLayouts()
    {
        var equity = await ShoonyaRestClient.ParseInstrumentArchive(
            "NSE",
            CreateArchive(
                "NSE_symbols.txt",
                """
				Exchange,Token,LotSize,Symbol,TradingSymbol,Instrument,TickSize,
				NSE,26000,1,Nifty 50,NIFTY INDEX,INDEX,0,
				NSE,2885,1,RELIANCE,RELIANCE-EQ,EQ,0.05,
				"""),
            CancellationToken.None);
        var derivative = await ShoonyaRestClient.ParseInstrumentArchive(
            "NFO",
            CreateArchive(
                "NFO_symbols.txt",
                """
				Exchange,Token,LotSize,Symbol,TradingSymbol,Expiry,Instrument,OptionType,StrikePrice,TickSize,
				NFO,156871,900,ZYDUSLIFE,ZYDUSLIFE29SEP26P1600,29-SEP-2026,OPTSTK,PE,1600,0.05,
				"""),
            CancellationToken.None);
        var currency = await ShoonyaRestClient.ParseInstrumentArchive(
            "CDS",
            CreateArchive(
                "CDS_symbols.txt",
                """
				Exchange,Token,LotSize,Precision,Multiplier,Symbol,TradingSymbol,Expiry,Instrument,OptionType,StrikePrice,TickSize,
				CDS,17274,1,4,1000,USDJPY,USDJPY29DEC26P141,29-DEC-2026,OPTCUR,PE,141,0.01,
				"""),
            CancellationToken.None);
        var commodity = await ShoonyaRestClient.ParseInstrumentArchive(
            "MCX",
            CreateArchive(
                "MCX_symbols.txt",
                """
				Exchange,Token,LotSize,GNGD,Symbol,TradingSymbol,Expiry,Instrument,OptionType,StrikePrice,TickSize,
				MCX,574822,100,0.1,SILVER100,SILVER10031JUL26,31-JUL-2026,FUTCOM,XX,0,1,
				"""),
            CancellationToken.None);

        AreEqual(2, equity.Length);
        AreEqual(SecurityTypes.Index, equity[0].ToSecurityType());
        AreEqual(SecurityTypes.Stock, equity[1].ToSecurityType());
        AreEqual(SecurityTypes.Option, derivative[0].ToSecurityType());
        AreEqual(OptionTypes.Put, derivative[0].OptionType.ToOptionType());
        AreEqual(1600m, derivative[0].StrikePrice);
        AreEqual(SecurityTypes.Option, currency[0].ToSecurityType());
        AreEqual(1000m, currency[0].Multiplier);
        AreEqual(SecurityTypes.Future, commodity[0].ToSecurityType());
        AreEqual("MCX|574822", commodity[0].ToSecurityId().Native);
    }

    [TestMethod]
    public void NativeCodesAndOrderStatesRemainNorenCompatible()
    {
        AreEqual("C", ShoonyaProducts.Delivery.ToNative());
        AreEqual("I", ShoonyaProducts.Intraday.ToNative());
        AreEqual("M", ShoonyaProducts.Normal.ToNative());
        AreEqual("B", Sides.Buy.ToNative());
        AreEqual(Sides.Sell, "S".ToSide());
        AreEqual("MKT", OrderTypes.Market.ToPriceType(0));
        AreEqual("SL-LMT", OrderTypes.Conditional.ToPriceType(100));
        AreEqual("SL-MKT", OrderTypes.Conditional.ToPriceType(0));
        AreEqual("IOC", ((TimeInForce?)TimeInForce.CancelBalance).ToRetention());
        AreEqual(
            OrderStates.Pending,
            "TRIGGER_PENDING".ToOrderState(null));
        AreEqual(
            OrderStates.Active,
            "OPEN".ToOrderState(null));
        AreEqual(
            OrderStates.Done,
            "COMPLETE".ToOrderState("Fill"));
        AreEqual(
            OrderStates.Failed,
            "REJECTED".ToOrderState("Rejected"));
    }

    [TestMethod]
    public void SparseDepthUpdatesMergeWithoutErasingPreviousLevels()
    {
        var state = new ShoonyaMarketUpdate();
        state.Apply(JsonConvert.DeserializeObject<ShoonyaMarketUpdate>(
            """
			{"t":"dk","e":"NSE","tk":"22","lp":"1156.25",
			 "bp1":"1156.00","bq1":"4","bo1":"1",
			 "sp1":"1156.50","sq1":"10","so1":"2",
			 "bp2":"1155.80","bq2":"67","bo2":"4"}
			"""));
        state.Apply(JsonConvert.DeserializeObject<ShoonyaMarketUpdate>(
            """
			{"t":"df","e":"NSE","tk":"22","lp":"1157.00",
			 "sq1":"3","so1":"1"}
			"""));

        AreEqual(1157m, state.LastPrice.ToDecimal());
        AreEqual(2, state.GetBids().Length);
        AreEqual(1156m, state.GetBids()[0].Price);
        AreEqual(1156.50m, state.GetAsks()[0].Price);
        AreEqual(3m, state.GetAsks()[0].Volume);
        AreEqual(1, state.GetAsks()[0].OrdersCount);
    }

    private static ShoonyaPlaceOrderRequest Order(string symbol)
        => new()
        {
            UserId = "ZP001",
            AccountId = "ZP001",
            Side = "B",
            Product = "C",
            Exchange = "NSE",
            TradingSymbol = symbol,
            Quantity = "10",
            DisclosedQuantity = "0",
            PriceType = "LMT",
            Price = "100.50",
            TriggerPrice = "0",
            Retention = "DAY",
        };

    private static byte[] CreateArchive(string name, string content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            true))
        using (var writer = new StreamWriter(
            archive.CreateEntry(name).Open(),
            new UTF8Encoding(false)))
            writer.Write(content);
        return stream.ToArray();
    }

    private sealed record ResponseSpec(
        string Json,
        HttpStatusCode StatusCode = HttpStatusCode.OK);

    private sealed record CapturedRequest(
        Uri Uri,
        HttpMethod Method,
        string Authorization,
        string ContentType,
        string Body);

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly Queue<ResponseSpec> _responses;

        public CaptureHandler(params ResponseSpec[] responses)
        {
            _responses = new(responses);
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new(
                request.RequestUri,
                request.Method,
                request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.ToString(),
                request.Content == null
                    ? null
                    : await request.Content.ReadAsStringAsync(
                        cancellationToken)));
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    "No fake Zebu response was configured.");
            }
            var spec = _responses.Dequeue();
            return new(spec.StatusCode)
            {
                Content = new StringContent(
                    spec.Json,
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }
}
