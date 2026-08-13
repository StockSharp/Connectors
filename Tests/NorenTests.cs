namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
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
using StockSharp.Noren;
using StockSharp.Noren.Native;
using StockSharp.Noren.Native.Model;
using StockSharp.Shoonya;
using StockSharp.Zebu;

[TestClass]
public class NorenTests : BaseTestClass
{
    [TestMethod]
    public void ConcreteNorenTypesDoNotHidePublicProperties()
    {
        AssertNoHiddenPublicProperties(typeof(ShoonyaMessageAdapter));
        AssertNoHiddenPublicProperties(typeof(ZebuMessageAdapter));
        AssertNoHiddenPublicProperties(typeof(ShoonyaOrderCondition));
        AssertNoHiddenPublicProperties(typeof(ZebuOrderCondition));

        AreEqual(
            typeof(ShoonyaProducts),
            typeof(ShoonyaMessageAdapter)
                .GetProperty(nameof(ShoonyaMessageAdapter.DefaultProduct))
                .PropertyType);
        AreEqual(
            typeof(NorenProducts),
            typeof(ZebuMessageAdapter)
                .GetProperty(nameof(ZebuMessageAdapter.DefaultProduct))
                .PropertyType);
        AreEqual(
            typeof(ShoonyaProducts?),
            typeof(ShoonyaOrderCondition)
                .GetProperty(nameof(ShoonyaOrderCondition.Product))
                .PropertyType);
        AreEqual(
            typeof(NorenProducts?),
            typeof(ZebuOrderCondition)
                .GetProperty(nameof(ZebuOrderCondition.Product))
                .PropertyType);
    }

    [TestMethod]
    public void ConcreteAdaptersUseNeutralNorenBase()
    {
        IsTrue(typeof(NorenMessageAdapter).IsAssignableFrom(
            typeof(ShoonyaMessageAdapter)));
        IsTrue(typeof(NorenMessageAdapter).IsAssignableFrom(
            typeof(ZebuMessageAdapter)));
        IsFalse(typeof(ShoonyaMessageAdapter).IsAssignableFrom(
            typeof(ZebuMessageAdapter)));
        IsTrue(typeof(NorenOrderCondition).IsAssignableFrom(
            typeof(ShoonyaOrderCondition)));
        IsTrue(typeof(NorenOrderCondition).IsAssignableFrom(
            typeof(ZebuOrderCondition)));

        var shoonyaReferences = typeof(ShoonyaMessageAdapter)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();
        var zebuReferences = typeof(ZebuMessageAdapter)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        IsTrue(shoonyaReferences.Contains("StockSharp.Noren"));
        IsTrue(zebuReferences.Contains("StockSharp.Noren"));
        IsFalse(zebuReferences.Contains("StockSharp.Shoonya"));
        IsTrue(ShoonyaMessageAdapter.AllTimeFrames.Any());
        IsTrue(ZebuMessageAdapter.AllTimeFrames.Any());

        var conditionFactory = typeof(NorenMessageAdapter).GetMethod(
            "CreateOrderCondition",
            BindingFlags.Instance | BindingFlags.NonPublic);
        AreEqual(
            typeof(ShoonyaOrderCondition),
            conditionFactory.Invoke(
                new ShoonyaMessageAdapter(
                    new IncrementalIdGenerator()),
                null)
                .GetType());
        AreEqual(
            typeof(ZebuOrderCondition),
            conditionFactory.Invoke(
                new ZebuMessageAdapter(
                    new IncrementalIdGenerator()),
                null)
                .GetType());
    }

    private static void AssertNoHiddenPublicProperties(Type type)
    {
        const BindingFlags declaredPublic = BindingFlags.Public |
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.DeclaredOnly;

        foreach (var property in type.GetProperties(declaredPublic))
        {
            for (var baseType = type.BaseType;
                baseType != null;
                baseType = baseType.BaseType)
            {
                var baseProperty = baseType.GetProperty(
                    property.Name,
                    declaredPublic);
                if (baseProperty is null)
                    continue;

                var overridesBaseSlot = property
                    .GetAccessors()
                    .Any(accessor =>
                        accessor.GetBaseDefinition() != accessor);
                IsTrue(
                    overridesBaseSlot,
                    $"{type.Name}.{property.Name} hides " +
                    $"{baseType.Name}.{baseProperty.Name}.");
            }
        }
    }

    [TestMethod]
    public void ShoonyaFacadeLoadsLegacyProductSetting()
    {
        var storage = new SettingsStorage()
            .Set(
                nameof(ShoonyaMessageAdapter.DefaultProduct),
                ShoonyaProducts.Normal);
        var adapter = new ShoonyaMessageAdapter(
            new IncrementalIdGenerator());

        adapter.Load(storage);

        AreEqual(ShoonyaProducts.Normal, adapter.DefaultProduct);
        var saved = new SettingsStorage();
        adapter.Save(saved);
        AreEqual(
            NorenProducts.Normal,
            saved.GetValue<object>(
                nameof(ShoonyaMessageAdapter.DefaultProduct)));
        AreEqual(
            "https://api.shoonya.com/NorenWClientTP/",
            adapter.RestEndpoint);
        AreEqual(
            "https://api.shoonya.com/{0}_symbols.txt.zip",
            adapter.InstrumentEndpointTemplate);
        AreEqual(
            "wss://api.shoonya.com/NorenWSTP/",
            adapter.WebSocketEndpoint);
        AreEqual(10, adapter.ReconnectAttempts);
        IsTrue(adapter is ITokenAdapter);

        var zebuAdapter = new ZebuMessageAdapter(
            new IncrementalIdGenerator());
        zebuAdapter.Load(storage);
        AreEqual(NorenProducts.Normal, zebuAdapter.DefaultProduct);

        var condition = new ShoonyaOrderCondition();
        condition.Parameters[nameof(condition.Product)] =
            ShoonyaProducts.Normal;

        AreEqual(ShoonyaProducts.Normal, condition.Product);
        condition.Product = ShoonyaProducts.Normal;
        AreEqual(
            NorenProducts.Normal,
            condition.Parameters[nameof(condition.Product)]);

        var zebuCondition = new ZebuOrderCondition();
        zebuCondition.Parameters[nameof(zebuCondition.Product)] =
            ShoonyaProducts.Normal;
        AreEqual(NorenProducts.Normal, zebuCondition.Product);
    }

    [TestMethod]
    public async Task BearerTransportUsesRawJDataApi2AndEncodedSymbol()
    {
        var handler = new CaptureHandler(
            new ResponseSpec(
                """{"stat":"Ok","norenordno":"ORDER-1"}"""));
        using var client = new NorenRestClient(
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
        using var client = new NorenRestClient(
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
        using var oauth = new NorenSocketClient(
            "ZP001",
            "ZP001-A",
            "ACCESS".Secure(),
            true,
            3,
            new WorkingTime(),
            "wss://go.example.test/NorenWSAPI/",
            true);
        using var legacy = new NorenSocketClient(
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
        var equity = await NorenRestClient.ParseInstrumentArchive(
            "NSE",
            CreateArchive(
                "NSE_symbols.txt",
                """
				Exchange,Token,LotSize,Symbol,TradingSymbol,Instrument,TickSize,
				NSE,26000,1,Nifty 50,NIFTY INDEX,INDEX,0,
				NSE,2885,1,RELIANCE,RELIANCE-EQ,EQ,0.05,
				"""),
            CancellationToken.None);
        var derivative = await NorenRestClient.ParseInstrumentArchive(
            "NFO",
            CreateArchive(
                "NFO_symbols.txt",
                """
				Exchange,Token,LotSize,Symbol,TradingSymbol,Expiry,Instrument,OptionType,StrikePrice,TickSize,
				NFO,156871,900,ZYDUSLIFE,ZYDUSLIFE29SEP26P1600,29-SEP-2026,OPTSTK,PE,1600,0.05,
				"""),
            CancellationToken.None);
        var currency = await NorenRestClient.ParseInstrumentArchive(
            "CDS",
            CreateArchive(
                "CDS_symbols.txt",
                """
				Exchange,Token,LotSize,Precision,Multiplier,Symbol,TradingSymbol,Expiry,Instrument,OptionType,StrikePrice,TickSize,
				CDS,17274,1,4,1000,USDJPY,USDJPY29DEC26P141,29-DEC-2026,OPTCUR,PE,141,0.01,
				"""),
            CancellationToken.None);
        var commodity = await NorenRestClient.ParseInstrumentArchive(
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
        AreEqual("C", NorenProducts.Delivery.ToNative());
        AreEqual("I", NorenProducts.Intraday.ToNative());
        AreEqual("M", NorenProducts.Normal.ToNative());
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
        var state = new NorenMarketUpdate();
        state.Apply(JsonConvert.DeserializeObject<NorenMarketUpdate>(
            """
			{"t":"dk","e":"NSE","tk":"22","lp":"1156.25",
			 "bp1":"1156.00","bq1":"4","bo1":"1",
			 "sp1":"1156.50","sq1":"10","so1":"2",
			 "bp2":"1155.80","bq2":"67","bo2":"4"}
			"""));
        state.Apply(JsonConvert.DeserializeObject<NorenMarketUpdate>(
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

    private static NorenPlaceOrderRequest Order(string symbol)
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
                    "No fake Noren response was configured.");
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
