﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

using Keen.Core;
using System.IO;
using System.Diagnostics;
using Newtonsoft.Json;
using System.Collections;
using System.Dynamic;
using System.Threading;
using Newtonsoft.Json.Linq;
using Keen.Net;
using Keen.Core.EventCache;

namespace Keen.NET.Test
{
    public class TestBase
    {
        public static bool UseMocks = false;

        [TestFixtureSetUp]
        public void Setup()
        {
            if (UseMocks)
                SetupEnv();
        }

        [TestFixtureTearDown]
        public void TearDown()
        {
            if (UseMocks)
                ResetEnv();
        }

        public static void SetupEnv()
        {
            foreach (var s in new[] { "KEEN_PROJECT_ID", "KEEN_MASTER_KEY", "KEEN_WRITE_KEY", "KEEN_READ_KEY" })
            {
                Environment.SetEnvironmentVariable(s, "0123456789ABCDEF");
            }
        }

        public static void ResetEnv()
        {
            foreach (var s in new[] { "KEEN_PROJECT_ID", "KEEN_MASTER_KEY", "KEEN_WRITE_KEY", "KEEN_READ_KEY" })
                Environment.SetEnvironmentVariable(s, null);
        }

    }

    [TestFixture]
    public class BulkEventTest : TestBase
    {
        [Test]
        public void AddEvents_InvalidProject_Throws()
        {
            var settingsEnv = new ProjectSettingsProviderEnv();
            var settings = new ProjectSettingsProvider(projectId: "X", writeKey: settingsEnv.WriteKey);
            var client = new KeenClient(settings);
            if (UseMocks)
                client.Event = new EventMock(settings,
                    AddEvents: new Func<JObject, IProjectSettings, IEnumerable<CachedEvent>>((e, p) =>
                    {
                        if ((p == settings)
                            &&(p.ProjectId=="X"))
                            throw new KeenException();
                        else
                            throw new Exception("Unexpected value");
                    }));
            Assert.Throws<KeenException>(() => client.AddEvents("AddEventTest", new []{ new {AProperty = "AValue" }}));
        }


        [Test]
        public void AddEvents_PartialFailure_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            if (UseMocks)
                client.Event = new EventMock(settings,
                    AddEvents: new Func<JObject, IProjectSettings, IEnumerable<CachedEvent>>((e, p) =>
                    {
                        var err = e.SelectToken("$.AddEventTest[2]") as JObject;
                        if (null == err)
                            throw new Exception("Unexpected error, test data not found");

                        return new List<CachedEvent>(){new CachedEvent("AddEventTest", e)};
                    }));

            object invalidEvent = new ExpandoObject();
            ((IDictionary<string, object>)invalidEvent).Add("$" + new string('.', 260), "AValue");

            var events = (from i in Enumerable.Range(1, 2)
                         select new { AProperty = "AValue" + i }).ToList<object>();
            events.Add(invalidEvent);

            Assert.Throws<KeenBulkException>(() => client.AddEvents("AddEventTest", events));
        }
        
        [Test]
        public void AddEvents_NoCache_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            var done = false;
            if (UseMocks)
                client.Event = new EventMock(settings,
                    AddEvents: new Func<JObject, IProjectSettings, IEnumerable<CachedEvent>>((e, p) =>
                    {
                        done = true;
                        Assert.True(p == settings, "Incorrect settings");
                        Assert.NotNull(e.Property("AddEventTest"), "Expected collection not found");
                        Assert.AreEqual(e.Property("AddEventTest").Value.AsEnumerable().Count(), 3, "Expected exactly 3 collection members");
                        foreach (dynamic q in ((dynamic)e).AddEventTest)
                            Assert.NotNull(q.keen.timestamp, "keen.timestamp properties should exist");
                        return new List<CachedEvent>();
                    }));

            var events = from i in Enumerable.Range(1,3)
                         select new { AProperty = "AValue" + i};

            Assert.DoesNotThrow(() => client.AddEvents("AddEventTest", events));
            Assert.True((UseMocks && done) || !UseMocks, "AddEvent mock was not called");
        }

        [Test]
        public void AddEvents_Cache_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings, new EventCacheMemory());
            if (UseMocks)
                client.Event = new EventMock(settings,
                    AddEvents: new Func<JObject, IProjectSettings, IEnumerable<CachedEvent>>((e, p) =>
                    {
                        // Should not be called with caching enabled
                        Assert.Fail();
                        return new List<CachedEvent>();
                    }));

            var events = from i in Enumerable.Range(1, 3)
                         select new { AProperty = "AValue" + i };

            Assert.DoesNotThrow(() => client.AddEvents("AddEventTest", events));

            // reset the AddEvents mock
            if (UseMocks)
                client.Event = new EventMock(settings,
                    AddEvents: new Func<JObject, IProjectSettings, IEnumerable<CachedEvent>>((e, p) =>
                    {
                        Assert.True(p == settings, "Incorrect settings");
                        Assert.NotNull(e.Property("AddEventTest"), "Expected collection not found");
                        Assert.AreEqual(e.Property("AddEventTest").Value.AsEnumerable().Count(), 3, "Expected exactly 3 collection members");
                        foreach (dynamic q in ((dynamic)e).AddEventTest)
                            Assert.NotNull(q.keen.timestamp, "keen.timestamp properties should exist");
                        return new List<CachedEvent>();
                    }));
            Assert.DoesNotThrow(() => client.SendCachedEvents());
        }
    }

    [TestFixture]
    public class KeenClientTest : TestBase
    {
        [Test]
        public void Constructor_ProjectSettingsNull_Throws()
        {
            Assert.Throws<KeenException>(() => new KeenClient(null));
        }

        [Test]
        public void Constructor_ProjectSettingsNoProjectID_Throws()
        {
            var settings = new ProjectSettingsProvider(projectId: "", masterKey: "X", writeKey: "X");
            Assert.Throws<KeenException>(() => new KeenClient(settings));
        }

        [Test]
        public void Constructor_ProjectSettingsNoMasterOrWriteKeys_Throws()
        {
            var settings = new ProjectSettingsProvider(projectId: "X");
            Assert.Throws<KeenException>(() => new KeenClient(settings));
        }

        [Test]
        public void GetCollectionSchema_NullProjectId_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            Assert.Throws<KeenException>(() => client.GetSchema(null));
        }

        [Test]
        public void GetCollectionSchema_EmptyProjectId_Throws()
        {
            var settingsEnv = new ProjectSettingsProviderEnv();
            var settings = new ProjectSettingsProvider(projectId: "X", masterKey: settingsEnv.MasterKey);
            var client = new KeenClient(settings);
            Assert.Throws<KeenException>(() => client.GetSchema(""));
        }


        [Test]
        public void GetCollectionSchema_InvalidProjectId_Throws()
        {
            var settingsEnv = new ProjectSettingsProviderEnv();
            var settings = new ProjectSettingsProvider(projectId: "X", masterKey: settingsEnv.MasterKey);
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    GetSchema: new Func<string, IProjectSettings, JObject>((c, p) =>
                    {
                        if ((p == settings) && (c == "X"))
                            throw new KeenResourceNotFoundException();
                        else
                            throw new Exception("Unexpected value");
                    }));
            Assert.Throws<KeenResourceNotFoundException>(() => client.GetSchema("X"));
        }

        [Test]
        public void GetCollectionSchema_ValidProjectIdInvalidSchema_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    GetSchema: new Func<string, IProjectSettings, JObject>((c, p) =>
                    {
                        if ((p == settings) && (c == "DoesntExist"))
                            throw new KeenResourceNotFoundException();
                        return new JObject();
                    }));

            Assert.Throws<KeenResourceNotFoundException>(() => client.GetSchema("DoesntExist"));
        }

        [Test]
        public void GetCollectionSchema_ValidProject_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    GetSchema: new Func<string, IProjectSettings, JObject>((c, p) =>
                    {
                        if ((p == settings) && (c == "AddEventTest"))
                            return JObject.Parse("{\"properties\":{\"AProperty\": \"string\"}}");
                        else
                            throw new KeenResourceNotFoundException(c);
                    }));

            Assert.DoesNotThrow(() =>
            {
                dynamic response = client.GetSchema("AddEventTest");
                Assert.NotNull(response["properties"]);
                Assert.NotNull(response["properties"]["AProperty"]);
                Assert.True((string)response["properties"]["AProperty"] == "string");
            });
        }

        [Test]
        public void AddEvent_InvalidProjectId_Throws()
        {
            var settingsEnv = new ProjectSettingsProviderEnv();
            var settings = new ProjectSettingsProvider(projectId: "X", writeKey: settingsEnv.WriteKey);
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    AddEvent: new Action<string, JObject, IProjectSettings>((c, e, p) =>
                    {
                        if ((p == settings) && (c == "X") && (e["X"].Value<string>() == "X"))
                            throw new KeenResourceNotFoundException(c);
                    }));

            Assert.Throws<KeenResourceNotFoundException>(() => client.AddEvent("X", new { X = "X" }));
        }

        [Test]
        public void AddEvent_ValidProjectIdInvalidWriteKey_Throws()
        {
            var settingsEnv = new ProjectSettingsProviderEnv();
            var settings = new ProjectSettingsProvider(projectId: settingsEnv.ProjectId, writeKey: "X");
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    AddEvent: new Action<string, JObject, IProjectSettings>((c, e, p) =>
                    {
                        if ((p == settings) && (c == "X") && (e["X"].Value<string>() == "X"))
                            throw new KeenInvalidApiKeyException(c);
                    }));
            Assert.Throws<KeenInvalidApiKeyException>(() => client.AddEvent("X", new { X = "X" }));
        }

        [Test]
        public void AddEvent_InvalidCollectionNameBlank_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            Assert.Throws<KeenException>(() => client.AddEvent("", new { AProperty = "AValue" }));
        }

        [Test]
        public void AddEvent_InvalidCollectionNameNull_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            Assert.Throws<KeenException>(() => client.AddEvent(null, new { AProperty = "AValue" }));
        }

        [Test]
        public void AddEvent_InvalidCollectionNameDollarSign_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            Assert.Throws<KeenException>(() => client.AddEvent("$Invalid", new { AProperty = "AValue" }));
        }

        [Test]
        public void AddEvent_InvalidCollectionNameTooLong_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            Assert.Throws<KeenException>(() => client.AddEvent(new String('X', 257), new { AProperty = "AValue" }));
        }

        [Test]
        public void AddEvent_InvalidKeenNamespaceProperty_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    AddEvent: new Action<string,JObject,IProjectSettings>((c,e,p) => 
                    {
                        if ((p == settings) 
                            && (c == "X") 
                            && (null != e.Property("keen"))
                            && (e.Property("keen").Value.GetType()==typeof(JObject))
                            && (null!=((JObject)e.Property("keen").Value).Property("AProperty")))
                                throw new KeenInvalidKeenNamespacePropertyException();
                        else
                            throw new Exception("Unexpected value");
                    }));

            Assert.Throws<KeenInvalidKeenNamespacePropertyException>(() => client.AddEvent("X", new { keen = new { AProperty = "AValue" } }));
        }

        [Test]
        public void AddEvent_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    AddEvent: new Action<string, JObject, IProjectSettings>((c, e, p) =>
                    {
                        if ((p == settings) && (c == "AddEventTest") && (e["AProperty"].Value<string>()=="AValue"))
                            return;
                        else
                            throw new Exception("Unexpected values");
                    }));
            Assert.DoesNotThrow(() => client.AddEvent("AddEventTest", new { AProperty = "AValue" }));
        }

        [Test]
        public void AddEvent_ScopedKeyWrite_Success()
        {
            var settingsEnv = new ProjectSettingsProviderEnv();
            var scope = "{\"timestamp\": \"2014-02-25T22:09:27.320082\", \"allowed_operations\": [\"write\"]}";
            var scopedKey = ScopedKey.EncryptString(settingsEnv.MasterKey, scope);
            var settings = new ProjectSettingsProvider(masterKey: settingsEnv.MasterKey, projectId: settingsEnv.ProjectId, writeKey: scopedKey);

            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    AddEvent: new Action<string, JObject, IProjectSettings>((c, e, p) =>
                    {
                        var key = JObject.Parse(ScopedKey.Decrypt(p.MasterKey, p.WriteKey));
                        if ((key["allowed_operations"].Values<string>().First()=="write") && (p == settings) && (c == "AddEventTest") && (e["AProperty"].Value<string>()=="CustomKey"))
                            return;
                        else
                            throw new Exception("Unexpected value");
                    }));

            Assert.DoesNotThrow(() => client.AddEvent("AddEventTest", new { AProperty = "CustomKey" }));
        }

        //[Test]
        //public void AddEvent_MultipleEventsInvalidCollection_Throws()
        //{
        //    var settings = new ProjectSettingsProviderEnv();
        //    var client = new KeenClient(settings);
        //    var collection = new
        //    {
        //        AddEventTest = from i in Enumerable.Range(1, 10)
        //                       select new { AProperty = "AValue" + i },
        //        InvalidCollection = 2,
        //    };
        //    Assert.Throws<KeenInternalServerErrorException>(() => client.AddEvents(collection));
        //}

        //[Test]
        //public void AddEvent_MultipleEventsInvalidItem_Throws()
        //{
        //    var settings = new ProjectSettingsProviderEnv();
        //    var client = new KeenClient(settings);
        //    var collection = new { AddEventTest = new List<dynamic>() };

        //    foreach( var k in new[]{ "ValidProperty", "Invalid.Property" })
        //    {
        //        IDictionary<string, object> item = new ExpandoObject();
        //        item.Add(k, "AValue");
        //        collection.AddEventTest.Add(item);
        //    }

        //    Assert.DoesNotThrow(() => client.AddEvents(collection));
        //}

        //[Test]
        //public void AddEvent_MultipleEventsAnonymous_Success()
        //{
        //    var settings = new ProjectSettingsProviderEnv();
        //    var client = new KeenClient(settings);
        //    var collection = new
        //    {
        //        AddEventTest = from i in Enumerable.Range(1, 10)
        //                       select new { AProperty = "AValue" + i }
        //    };
        //    Assert.DoesNotThrow(() => client.AddEvents(collection));
        //}

        //[Test]
        //public void AddEvent_MultipleEventsExpando_Success()
        //{
        //    var settings = new ProjectSettingsProviderEnv();
        //    var client = new KeenClient(settings);

        //    dynamic collection = new ExpandoObject();
        //    collection.AddEventTest = new List<dynamic>();
        //    foreach( var i in Enumerable.Range(1,10))
        //    {
        //        dynamic anEvent = new ExpandoObject();
        //        anEvent.AProperty = "AValue" + i;
        //        collection.AddEventTest.Add(anEvent);
        //    }

        //    Assert.DoesNotThrow(() => client.AddEvents(collection));
        //}

        //private class TestCollection
        //{
        //    public class TestEvent
        //    {
        //        public string AProperty { get; set; }
        //    }
        //    public List<TestEvent> AddEventTest { get; set; }
        //}

        //[Test]
        //public void AddEvent_MultipleEvents_Success()
        //{
        //    var settings = new ProjectSettingsProviderEnv();
        //    var client = new KeenClient(settings);

        //    var collection = new TestCollection()
        //    {
        //        AddEventTest = (from i in Enumerable.Range(1, 10)
        //                       select new TestCollection.TestEvent() { AProperty = "AValue"+i}).ToList()
        //    };

        //    Assert.DoesNotThrow(() => client.AddEvents(collection));
        //}

        [Test]
        public void DeleteCollection_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    DeleteCollection: new Action<string, IProjectSettings>((c, p) =>
                    {
                        if ((p == settings) && (c == "DeleteColTest"))
                            return;
                        else
                            throw new Exception("Unexpected value");
                    }));

            // Idempotent, does not matter if collection does not exist.
            Assert.DoesNotThrow(() => client.DeleteCollection("DeleteColTest"));
        }
    }

    [TestFixture]
    public class KeenClientGlobalPropertyTests : TestBase
    {
        [Test]
        public void AddGlobalProperty_SimpleValue_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    AddEvent: new Action<string, JObject, IProjectSettings>((c, e, p) =>
                    {
                        if ((p == settings) && (c == "AddEventTest") 
                            && (e["AProperty"].Value<string>() == "AValue") 
                            && (e["AGlobal"].Value<string>() == "AValue"))
                            return;
                        else
                            throw new Exception("Unexpected value");
                    }));

            Assert.DoesNotThrow(() =>
                {
                    client.AddGlobalProperty("AGlobal", "AValue");
                    client.AddEvent("AddEventTest", new { AProperty = "AValue" });
                });
        }

        [Test]
        public void AddGlobalProperty_InvalidValueNameDollar_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            Assert.Throws<KeenException>(() => client.AddGlobalProperty("$AGlobal", "AValue"));
        }

        [Test]
        public void AddGlobalProperty_InvalidValueNamePeriod_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            Assert.Throws<KeenException>(() => client.AddGlobalProperty("A.Global", "AValue"));
        }

        [Test]
        public void AddGlobalProperty_InvalidValueNameLength_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            Assert.Throws<KeenException>(() => client.AddGlobalProperty(new String('A', 256), "AValue"));
        }

        [Test]
        public void AddGlobalProperty_InvalidValueNameNull_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            Assert.Throws<KeenException>(() => client.AddGlobalProperty(null, "AValue"));
        }


        [Test]
        public void AddGlobalProperty_InvalidValueNameBlank_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            Assert.Throws<KeenException>(() => client.AddGlobalProperty("", "AValue"));
        }

        [Test]
        public void AddGlobalProperty_ObjectValue_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    AddEvent: new Action<string, JObject, IProjectSettings>((c, e, p) =>
                    {
                        if ((p == settings) && (c == "AddEventTest") 
                            && (e["AProperty"].Value<string>() == "AValue") 
                            && (e["AGlobal"]["AProperty"].Value<string>() == "AValue"))
                            return;
                        else
                            throw new Exception("Unexpected value");
                    }));

            Assert.DoesNotThrow(() =>
            {
                client.AddGlobalProperty("AGlobal", new { AProperty = "AValue" });
                client.AddEvent("AddEventTest", new { AProperty = "AValue" });
            });
        }

        [Test]
        public void AddGlobalProperty_CollectionValue_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    AddEvent: new Action<string, JObject, IProjectSettings>((c, e, p) =>
                    {
                        if ((p == settings) && (c == "AddEventTest")
                            && (e["AProperty"].Value<string>() == "AValue")
                            && (e["AGlobal"].Values<int>().All((x) => (x == 1) || (x == 2) || (x == 3))))
                            return;
                        else
                            throw new Exception("Unexpected value");
                    }));

            Assert.DoesNotThrow(() =>
            {
                client.AddGlobalProperty("AGlobal", new[] { 1, 2, 3, });
                client.AddEvent("AddEventTest", new { AProperty = "AValue" });
            });
        }

        [Test]
        public void AddGlobalProperty_DelegateSimpleValue_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    AddEvent: new Action<string, JObject, IProjectSettings>((c, e, p) =>
                    {
                        if ((p == settings) && (c == "AddEventTest")
                            && (e["AProperty"].Value<string>() == "AValue")
                            && (e["AGlobal"]!=null))
                            return;
                        else
                            throw new Exception("Unexpected value");
                    }));

            Assert.DoesNotThrow(() =>
            {
                client.AddGlobalProperty("AGlobal", new DynamicPropertyValue(() => DateTime.Now.Millisecond));
                client.AddEvent("AddEventTest", new { AProperty = "AValue" });
                client.AddEvent("AddEventTest", new { AProperty = "AValue" });
            });
        }

        [Test]
        public void AddGlobalProperty_DelegateArrayValue_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    AddEvent: new Action<string, JObject, IProjectSettings>((c, e, p) =>
                    {
                        if ((p == settings) && (c == "AddEventTest") 
                            && (e["AProperty"].Value<string>() == "AValue")
                            && (e["AGlobal"].Values<int>().All((x) => (x == 1) || (x == 2) || (x == 3))))
                            return;
                        else
                            throw new Exception("Unexpected value");
                    }));

            Assert.DoesNotThrow(() =>
            {
                client.AddGlobalProperty("AGlobal", new DynamicPropertyValue(() => new[] { 1, 2, 3 }));
                client.AddEvent("AddEventTest", new { AProperty = "AValue" });
            });
        }

        [Test]
        public void AddGlobalProperty_DelegateObjectValue_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    AddEvent: new Action<string, JObject, IProjectSettings>((c, e, p) =>
                    {
                        if ((p == settings) && (c == "AddEventTest") 
                            && (e["AProperty"].Value<string>() == "AValue") 
                            && (e["AGlobal"].Value<JObject>()["SubProp1"].Value<string>() == "Value"))
                            return;
                        else
                            throw new Exception("Unexpected value");
                    }));

            Assert.DoesNotThrow(() =>
            {
                client.AddGlobalProperty("AGlobal", new DynamicPropertyValue(() => new { SubProp1 = "Value", SubProp2 = "Value" }));
                client.AddEvent("AddEventTest", new { AProperty = "AValue" });
            });
        }

        [Test]
        public void AddGlobalProperty_DelegateNullDynamicValue_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            Assert.Throws<KeenException>(() => { client.AddGlobalProperty("AGlobal", new DynamicPropertyValue(() => null)); });
        }

        [Test]
        public void AddGlobalProperty_DelegateNullValueProvider_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            Assert.Throws<KeenException>(() => { client.AddGlobalProperty("AGlobal", null); });
        }

        [Test]
        public void AddGlobalProperty_DelegateValueProviderNullReturn_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    AddEvent: new Action<string, JObject, IProjectSettings>((c, e, p) =>
                    {
                        if ((p == settings) && (c == "AddEventTest") 
                            && (e["AProperty"].Value<string>() == "AValue")
                            && (e["AGlobal"].Value<string>() == "value"))
                            return;
                        else
                            throw new Exception("Unexpected value");
                    }));

            var i = 0;
            // return valid for the first two tests, then null
            client.AddGlobalProperty("AGlobal", new DynamicPropertyValue(() => i++ > 1 ? null : "value"));
            // This is the second test (AddGlobalProperty runs the first)
            Assert.DoesNotThrow(() => client.AddEvent("AddEventTest", new { AProperty = "AValue" }));
            // Third test should fail.
            Assert.Throws<KeenException>(() => { client.AddEvent("AddEventTest", new { AProperty = "AValue" }); });
        }

        [Test]
        public void AddGlobalProperty_DelegateValueProviderThrows_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            Assert.Throws<KeenException>(() => client.AddGlobalProperty("AGlobal", new DynamicPropertyValue(() => { throw new Exception("test exception"); })));
        }
    }

    [TestFixture]
    public class KeenClientCachingTest : TestBase
    {
        [Test]
        public void Caching_SendEmptyEvents_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings, new EventCacheMemory());
            Assert.DoesNotThrow(() => client.SendCachedEvents());
        }

        [Test]
        public void Caching_ClearEvents_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings, new EventCacheMemory());
            Assert.DoesNotThrow(() => client.EventCache.Clear());
        }

        [Test]
        public void Caching_AddEvents_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings, new EventCacheMemory());

            Assert.DoesNotThrow(() => client.AddEvent("CachedEventTest", new { AProperty = "AValue" }));
            Assert.DoesNotThrow(() => client.AddEvent("CachedEventTest", new { AProperty = "AValue" }));
            Assert.DoesNotThrow(() => client.AddEvent("CachedEventTest", new { AProperty = "AValue" }));
        }

        [Test]
        public void Caching_SendEvents_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings, new EventCacheMemory());
            if (UseMocks)
                client.Event = new EventMock(settings,
                    AddEvents: new Func<JObject, IProjectSettings, IEnumerable<CachedEvent>>((e, p) =>
                    {
                        if ((p == settings) 
                            && (null!=e.Property("CachedEventTest"))
                            && (e.Property("CachedEventTest").Value.Children().Count()==3))
                            return new List<CachedEvent>();
                        else
                            throw new Exception("Unexpected value");
                    }));

            client.AddEvent("CachedEventTest", new { AProperty = "AValue" });
            client.AddEvent("CachedEventTest", new { AProperty = "AValue" });
            client.AddEvent("CachedEventTest", new { AProperty = "AValue" });

            Assert.DoesNotThrow(() => client.SendCachedEvents());
            Assert.Null(client.EventCache.TryTake(), "Cache is empty");
        }

        [Test]
        public void Caching_SendManyEvents_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings, new EventCacheMemory());
            var total = 0;
            if (UseMocks)
                client.Event = new EventMock(settings,
                    AddEvents: new Func<JObject, IProjectSettings, IEnumerable<CachedEvent>>((e, p) =>
                    {
                        if ((p == settings)
                            && (null != e.Property("CachedEventTest"))
                            && ((e.Property("CachedEventTest").Value.Children().Count() == KeenConstants.BulkBatchSize)))
                        {
                            total += e.Property("CachedEventTest").Value.Children().Count();
                            return new List<CachedEvent>();
                        }
                        else
                            throw new Exception("Unexpected value");
                    }));

            for (int i = 0; i < KeenConstants.BulkBatchSize; i++)
                client.AddEvent("CachedEventTest", new { AProperty = "AValue" });

            Assert.DoesNotThrow(() => client.SendCachedEvents());
            Assert.Null(client.EventCache.TryTake(), "Cache is empty");
            Assert.True( !UseMocks || ( total == KeenConstants.BulkBatchSize + 5));
        }

        [Test]
        public void Caching_SendInvalidEvents_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings, new EventCacheMemory());
            if (UseMocks)
                client.Event = new EventMock(settings,
                    AddEvents: new Func<JObject, IProjectSettings, IEnumerable<CachedEvent>>((e, p) =>
                    {
                        if (p == settings)
                            throw new KeenException("Mock exception");
                        else
                            throw new Exception("Unexpected value");
                    }));

            var anEvent = new JObject();
            anEvent.Add("AProperty", "AValue");
            client.AddEvent("CachedEventTest", anEvent);
            
            anEvent.Add("keen", JObject.FromObject(new {invalidPropName = "value"} ));
            client.AddEvent("CachedEventTest", anEvent);
            Assert.Throws<KeenException>(() => client.SendCachedEvents());
        }

    }

    [TestFixture]
    public class AsyncTests : TestBase
    {
        [Test]
        public async Task Async_DeleteCollection_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    DeleteCollection: new Action<string, IProjectSettings>((c, p) =>
                    {
                        if ((p == settings) && (c == "DeleteColTest"))
                            return;
                        else
                            throw new Exception("Unexpected value");
                    }));

            await client.DeleteCollectionAsync("DeleteColTest");
        }

        [Test]
        [ExpectedException("Keen.Core.KeenException")]
        public async Task Async_GetCollectionSchema_NullProjectId_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            await client.GetSchemaAsync(null);
        }

        [Test]
        [ExpectedException("Keen.Core.KeenResourceNotFoundException")]
        public async Task Async_AddEvent_InvalidProjectId_Throws()
        {
            var settingsEnv = new ProjectSettingsProviderEnv();
            var settings = new ProjectSettingsProvider(projectId: "X", writeKey: settingsEnv.WriteKey);
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    AddEvent: new Action<string, JObject, IProjectSettings>((c, e, p) =>
                    {
                        if ((p == settings) && (c == "AddEventTest") && (e["AProperty"].Value<string>() == "Value"))
                            throw new KeenResourceNotFoundException(c);
                    }));

            await client.AddEventAsync("AddEventTest", new { AProperty = "Value" });
        }

        [Test]
        [ExpectedException("Keen.Core.KeenInvalidApiKeyException")]
        public async Task Async_AddEvent_ValidProjectIdInvalidWriteKey_Throws()
        {
            var settingsEnv = new ProjectSettingsProviderEnv();
            var settings = new ProjectSettingsProvider(projectId: settingsEnv.ProjectId, writeKey: "X");
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    AddEvent: new Action<string, JObject, IProjectSettings>((c, e, p) =>
                    {
                        if ((p == settings) && (c == "AddEventTest") && (e["AProperty"].Value<string>() == "Value"))
                            throw new KeenInvalidApiKeyException(c);
                    }));

            await client.AddEventAsync("AddEventTest", new { AProperty = "Value" });
        }

        [Test]
        public async Task Async_AddEvent_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            if (UseMocks)
                client.EventCollection = new EventCollectionMock(settings,
                    AddEvent: new Action<string, JObject, IProjectSettings>((c, e, p) =>
                    {
                        if ((p == settings) && (c == "AddEventTest") && (e["AProperty"].Value<string>() == "Value"))
                            throw new KeenResourceNotFoundException(c);
                    }));
            
            await client.AddEventAsync("AddEventTest", new { AProperty = "AValue" });
        }

        [Test]
        [ExpectedException("Keen.Core.KeenException")]
        public async Task Async_AddEvents_NullCollection_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            await client.AddEventsAsync("AddEventTest", null);
        }

        [Test]
        public async Task Async_AddEvents_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings);
            if (UseMocks)
                client.Event = new EventMock(settings,
                    AddEvents: new Func<JObject, IProjectSettings, IEnumerable<CachedEvent>>((e, p) =>
                    {
                        Assert.AreEqual(p, settings, "Unexpected settings object");
                        Assert.AreEqual(e.Property("AddEventTest").Value.AsEnumerable().Count(), 3, "Expected exactly 3 collection members");
                        return new List<CachedEvent>();
                    }));

            var events = from e in Enumerable.Range(1, 3)
                         select new { AProperty = "Value" + e };

            await client.AddEventsAsync("AddEventTest", events);
        }

        [Test]
        public async Task Async_Caching_SendEvents_Success()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings, new EventCacheMemory());
            if (UseMocks)
                client.Event = new EventMock(settings,
                    AddEvents: new Func<JObject, IProjectSettings, IEnumerable<CachedEvent>>((e, p) =>
                    {
                        Assert.AreEqual(p, settings, "Unexpected settings object");
                        Assert.AreEqual(e.Property("CachedEventTest").Value.AsEnumerable().Count(), 3, "Expected exactly 3 collection members");
                        return new List<CachedEvent>();
                    }));

            client.AddEvent("CachedEventTest", new { AProperty = "AValue" });
            client.AddEvent("CachedEventTest", new { AProperty = "AValue" });
            client.AddEvent("CachedEventTest", new { AProperty = "AValue" });

            await client.SendCachedEventsAsync();
            Assert.Null(client.EventCache.TryTake(), "Cache should be empty");
        }

        [Test]
        [ExpectedException("Keen.Core.KeenBulkException")]
        public async Task Async_Caching_SendInvalidEvents_Throws()
        {
            var settings = new ProjectSettingsProviderEnv();
            var client = new KeenClient(settings, new EventCacheMemory());
            if (UseMocks)
                client.Event = new EventMock(settings,
                    AddEvents: new Func<JObject, IProjectSettings, IEnumerable<CachedEvent>>((e, p) =>
                    {
                        var err = e.SelectToken("$.AddEventTest[2]") as JObject;
                        if (null == err)
                            throw new Exception("Unexpected error, test data not found");

                        return new List<CachedEvent>() { new CachedEvent("AddEventTest", e) };
                    }));

            object invalidEvent = new ExpandoObject();
            ((IDictionary<string, object>)invalidEvent).Add("$" + new string('.', 260), "AValue");

            var events = (from i in Enumerable.Range(1, 2)
                          select new { AProperty = "AValue" + i }).ToList<object>();
            events.Add(invalidEvent);

            await client.AddEventsAsync("AddEventTest", events);
            await client.SendCachedEventsAsync();
        }
    }
}
