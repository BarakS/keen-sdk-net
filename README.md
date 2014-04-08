keen-sdk-net
============

Installation
------------

The easiest way to get started with the .NET SDK is to use the [KeenClient NuGet package](http://www.nuget.org/packages/KeenClient/). 

That can be installed from the Package Manager Console in Visual Studio with the command :

```
  PM> Install-Package KeenClient
```

The most up to date code is available in this repo.

```
  https://github.com/keenlabs/keen-sdk-net
```  

Initializing the Library
------------------------

```
  var prjSettings = new ProjectSettingsProvider("YourProjectID", writeKey: "YourWriteKey");
  var keenClient = new KeenClient(prjSettings);
```

Recording Events
----------------

Event data is provided to the client as an object. A simple way to do this is with an anonymous object:

```
  var aPurchase = new
    {
        category = "magical animals",
        username = "hagrid",
        price = 7.13,
        payment_type = "information",
        animal_type = "norwegian ridgeback dragon"
    };
    
  keenClient.AddEvent("purchases", aPurchase);
```

Recording Events Asynchronously
-------------------------------

Sometimes you want to record events in a non-blocking manner.  This is pretty simple:

```
  keenClient.AddEventAsync("purchases", aPurchase);
```

Using Global Properties
-----------------------

Static global properties are added with the KeenClient AddGlobalProperty method:

```
  keenClient.AddGlobalProperty("clienttype", "mobile");
```

Static global properties are added at the root level of all events just before they are sent or cached.

Dynamic global properties are added the same way, but rather than a static object an object supporting IDynamicPropertyValue is added. The class DynamicPropertyValue implements this interface and may be used to provide dynamic properties with a Func<object> delegate:

```
  var dynProp = new DynamicPropertyValue(() => new Random().Next(9999));
  keenClient.AddGlobalProperty("bonusfield", dynProp );
```

The delegate function is executed each time event data is added, but it may also be executed at other times as well.

Caching
-------

KeenClient supports an event data cache interface that allows transmission of event data to the Keen.IO server to be deferred until you call SendCachedEvents(). You may implement your own cache by supporting the IEventCache interface or you may use one of the two cache classes included, EventCacheMemory and EventCachePortable which store event data in memory and in portable storage, respectively.

To enable caching provide an instance supporting IEventCache when constructing KeenClient:

```
      var client = new KeenClient(new ProjectSettingsProviderEnv(), new EventCacheMemory());
```

Or:

```
      var client = new KeenClient(new ProjectSettingsProviderEnv(), EventCachePortable.New());
```

Events are added as usual, and at any time you may transmit the cached events to the server:

```
      client.SendCachedEvents();
```

The server may reject one or more events included in the cache. If this happens the item that was rejected will be recorded and transmission of the remaining cached events will continue. After all events in the cache have been transmitted, if any events were rejected they will be attached as instances of CachedEvent to an instance of KeenBulkException which will then be thrown. The KeenBulkException FailedEvents property may be accessed to review the failures.

Global properties are evaluated and added when AddEvent() is called, so dynamic properties will not be evaluated when SendCachedEvents() is called.

Full Example
------------

```
  static void Main(string[] args){
      // Set up the client
      var prjSettings = new ProjectSettingsProvider("YourProjectID", writeKey: "YourWriteKey");
      var keenClient = new KeenClient(prjSettings);

      keenClient.AddGlobalProperty("clienttype", "mobile");

      var dynProp = new DynamicPropertyValue(() => new Random().Next(9999));
      keenClient.AddGlobalProperty("bonusfield", dynProp );

      // Build an event object
      var aPurchase = new
      {
          category = "magical animals",
          username = "hagrid",
          price = 7.13,
          payment_type = "information",
          animal_type = "norwegian ridgeback dragon"
      };

      // send the event
      keenClient.AddEvent( "purchases", aPurchase);
  }
```

