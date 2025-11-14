using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.FileProviders;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// static files (same as before)
var staticPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "wasm-client", "static"));
if (!Directory.Exists(staticPath))
{
    Console.WriteLine($"Warning: static path not found: {staticPath}");
}
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(staticPath),
    RequestPath = ""
});

app.UseWebSockets();

// Distributed sync infrastructure -------------------------------------------------

// Redis connection (ENV: REDIS_URL or default localhost:6379)
var redisUrl = Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379";
var redis = ConnectionMultiplexer.Connect(redisUrl);
var db = redis.GetDatabase();
var subscriber = redis.GetSubscriber();

// Instance id to avoid echoing our own publishes back as new remote events
var instanceId = Guid.NewGuid().ToString();

// Rooms -> connected sockets (thread-safe)
var rooms = new ConcurrentDictionary<string, ConcurrentDictionary<WebSocket, string>>();

// Track subscriptions per room so we subscribe only once per instance
var subscribedRooms = new ConcurrentDictionary<string, bool>();

// Compose Redis keys/channels
static string RoomStateKey(string room) => $"room:{room}:state";
static string RoomChannel(string room) => $"room:{room}:channel";

// Message schema used over WS and Redis
// {
//   "type": "join" | "leave" | "op" | "snapshot" | "ping",
//   "room": "room-id",
//   "clientId": "client-id",
//   "timestamp": 1234567890,
//   "payload": { ... },
//   "origin": "instance-id" // added by servers when publishing
// }

// Helper: ensure subscription to room channel
async Task EnsureSubscribedAsync(string room)
{
    if (subscribedRooms.TryAdd(room, true))
    {
        var channel = RoomChannel(room);
        await subscriber.SubscribeAsync(channel, (chan, value) =>
        {
            try
            {
                var msg = JObject.Parse(value);
                var origin = msg.Value<string>("origin");
                // ignore messages published by this instance
                if (origin == instanceId) return;
                BroadcastToLocalClients(room, msg.ToString(Formatting.None));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to handle redis message for {room}: {ex}");
            }
        });
        Console.WriteLine($"Subscribed to Redis channel: {RoomChannel(room)}");
    }
}

// Broadcast JSON string to all local clients connected to given room
void BroadcastToLocalClients(string room, string json)
{
    if (!rooms.TryGetValue(room, out var sockets)) return;
    var buffer = Encoding.UTF8.GetBytes(json);
    var tasks = new List<Task>();
    foreach (var kv in sockets)
    {
        var ws = kv.Key;
        if (ws.State == WebSocketState.Open)
        {
            tasks.Add(ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None));
        }
    }

    // fire-and-forget
    _ = Task.WhenAll(tasks);
}

// Handle incoming op: persist to Redis and publish
async Task HandleOpAsync(string room, JObject msg)
{
    // payload is authoritative state or patch depending on your client design.
    // For this scaffold we accept `payload` as the new authoritative JSON state.
    var payload = msg["payload"] ?? new JObject();

    // Save state to Redis (string)
    await db.StringSetAsync(RoomStateKey(room), payload.ToString(Formatting.None));

    // Add origin and publish on channel so other instances receive it
    msg["origin"] = instanceId;
    var serialized = msg.ToString(Formatting.None);
    await subscriber.PublishAsync(RoomChannel(room), serialized);

    // Also broadcast locally (so the sender will receive update via server too)
    BroadcastToLocalClients(room, serialized);
}

// On a client join, send latest snapshot to that client
async Task SendSnapshotToClientAsync(WebSocket ws, string room)
{
    var raw = await db.StringGetAsync(RoomStateKey(room));
    var snapshot = new JObject
    {
        ["type"] = "snapshot",
        ["room"] = room,
        ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        ["payload"] = string.IsNullOrEmpty(raw) ? new JObject() : JObject.Parse(raw)
    };
    var json = snapshot.ToString(Formatting.None);
    var bytes = Encoding.UTF8.GetBytes(json);
    if (ws.State == WebSocketState.Open)
    {
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }
}

// WebSocket endpoint ----------------------------------------------------------------
app.Map("/ws", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    string? currentRoom = null;

    var buffer = new byte[8192];

    try
    {
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.MessageType != WebSocketMessageType.Text) continue;

            var msgText = Encoding.UTF8.GetString(buffer, 0, result.Count);
            JObject msg;
            try
            {
                msg = JObject.Parse(msgText);
            }
            catch
            {
                Console.WriteLine("Invalid JSON from client, ignoring.");
                continue;
            }

            var type = msg.Value<string>("type");
            var room = msg.Value<string>("room") ?? "default";
            var clientId = msg.Value<string>("clientId") ?? Guid.NewGuid().ToString();

            if (type == "join")
            {
                // register socket to room
                var sockets = rooms.GetOrAdd(room, _ => new ConcurrentDictionary<WebSocket, string>());
                sockets.TryAdd(ws, clientId);
                currentRoom = room;

                // ensure we are subscribed to redis channel for this room
                await EnsureSubscribedAsync(room);

                // send snapshot to this client
                await SendSnapshotToClientAsync(ws, room);

                // notify room peers that client joined
                var joinNotify = new JObject
                {
                    ["type"] = "peer_join",
                    ["room"] = room,
                    ["clientId"] = clientId,
                    ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ["origin"] = instanceId
                };
                BroadcastToLocalClients(room, joinNotify.ToString(Formatting.None));
            }
            else if (type == "leave")
            {
                if (rooms.TryGetValue(room, out var sockets))
                {
                    sockets.TryRemove(ws, out _);
                }
                currentRoom = null;
            }
            else if (type == "op" || type == "patch")
            {
                // handle as authoritative update for now
                await EnsureSubscribedAsync(room);
                await HandleOpAsync(room, msg);
            }
            else if (type == "ping")
            {
                var pong = new JObject
                {
                    ["type"] = "pong",
                    ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                var bytes = Encoding.UTF8.GetBytes(pong.ToString(Formatting.None));
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            else
            {
                // unknown message types are ignored for now
                Console.WriteLine($"Unknown message type: {type}");
            }
        }
    }
    catch (WebSocketException wex)
    {
        Console.WriteLine($"WebSocketException: {wex.Message}");
    }
    finally
    {
        // clean up socket from any room it belonged to
        if (currentRoom != null && rooms.TryGetValue(currentRoom, out var sockets))
        {
            sockets.TryRemove(ws, out _);
        }
        if (ws.State != WebSocketState.Closed)
        {
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None); } catch { }
        }
    }
});

// fallback to index.html same as before
app.MapFallback(async ctx =>
{
    var file = Path.Combine(staticPath, "index.html");
    if (File.Exists(file))
    {
        ctx.Response.ContentType = "text/html";
        await ctx.Response.SendFileAsync(file);
    }
    else
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsync("index.html not found");
    }
});

app.Run("http://0.0.0.0:5000");