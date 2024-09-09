using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public class Player
{
    public string Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

public class GameState
{
    public string GameId { get; set; }
    public List<Player> Players { get; set; } = new List<Player>();
}

public class Program
{
    private static Dictionary<string, GameState> _games = new Dictionary<string, GameState>();
    private static Dictionary<WebSocket, string> _clientGameMap =
        new Dictionary<WebSocket, string>();
    private static Random _random = new Random();

    public static async Task Main(string[] args)
    {
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:8090/");
        listener.Start();
        Console.WriteLine("Server started on http://localhost:8090/");

        while (true)
        {
            HttpListenerContext context = await listener.GetContextAsync();
            if (context.Request.IsWebSocketRequest)
            {
                HandleWebSocketRequest(context);
            }
            else
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
            }
        }
    }

    private static async void HandleWebSocketRequest(HttpListenerContext context)
    {
        WebSocketContext wsContext = await context.AcceptWebSocketAsync(null);
        WebSocket webSocket = wsContext.WebSocket;

        byte[] buffer = new byte[1024];
        while (webSocket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                CancellationToken.None
            );
            if (result.MessageType == WebSocketMessageType.Text)
            {
                string message = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                Console.WriteLine($"Received: {message}");
                HandleMessage(webSocket, message);
            }
            else if (result.MessageType == WebSocketMessageType.Close)
            {
                if (_clientGameMap.TryGetValue(webSocket, out string gameId))
                {
                    _clientGameMap.Remove(webSocket);
                    GameState gameState = _games[gameId];
                    gameState.Players.RemoveAll(p => p.Id == webSocket.GetHashCode().ToString());
                    await BroadcastGameState(gameId);
                }
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closed by the server",
                    CancellationToken.None
                );
            }
        }
    }

    private static void HandleMessage(WebSocket webSocket, string message)
    {
        var command = JsonSerializer.Deserialize<Dictionary<string, string>>(message);
        if (command["type"] == "create")
        {
            string gameId;
            do
            {
                gameId = _random.Next(100, 1000).ToString();
            } while (_games.ContainsKey(gameId));

            _games[gameId] = new GameState { GameId = gameId };
            _clientGameMap[webSocket] = gameId;
            SendMessage(
                webSocket,
                JsonSerializer.Serialize(new { type = "gameCreated", gameId = gameId })
            );
        }
        else if (command["type"] == "join")
        {
            string gameId = command["gameId"];
            if (_games.TryGetValue(gameId, out GameState gameState))
            {
                string playerId = webSocket.GetHashCode().ToString();
                Player player = new Player
                {
                    Id = playerId,
                    X = 1,
                    Y = 1
                };
                gameState.Players.Add(player);
                _clientGameMap[webSocket] = gameId;
                BroadcastGameState(gameId);
            }
            else
            {
                SendMessage(
                    webSocket,
                    JsonSerializer.Serialize(new { type = "error", message = "Game not found" })
                );
            }
        }
        else if (command["type"] == "move")
        {
            if (_clientGameMap.TryGetValue(webSocket, out string gameId))
            {
                GameState gameState = _games[gameId];
                Player player = gameState.Players.Find(p =>
                    p.Id == webSocket.GetHashCode().ToString()
                );
                if (player != null)
                {
                    switch (command["direction"])
                    {
                        case "up":
                            player.Y = Math.Max(1, player.Y - 1);
                            break;
                        case "down":
                            player.Y = Math.Min(13, player.Y + 1);
                            break;
                        case "left":
                            player.X = Math.Max(1, player.X - 1);
                            break;
                        case "right":
                            player.X = Math.Min(18, player.X + 1);
                            break;
                    }
                    BroadcastGameState(gameId);
                }
            }
        }
    }

    private static async Task BroadcastGameState(string gameId)
    {
        if (_games.TryGetValue(gameId, out GameState gameState))
        {
            string gameStateJson = JsonSerializer.Serialize(gameState);
            Console.WriteLine($"Sending game state to clients in game {gameId}: {gameStateJson}");
            foreach (var client in _clientGameMap)
            {
                if (client.Value == gameId && client.Key.State == WebSocketState.Open)
                {
                    await client.Key.SendAsync(
                        new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(gameStateJson)),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None
                    );
                }
            }
        }
    }

    private static async void SendMessage(WebSocket webSocket, string message)
    {
        if (webSocket.State == WebSocketState.Open)
        {
            await webSocket.SendAsync(
                new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(message)),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );
        }
    }
}
