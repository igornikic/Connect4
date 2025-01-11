using Microsoft.AspNetCore.Mvc;
using System.Net.WebSockets;
using System.Text;
using StackExchange.Redis;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

[ApiController]
[Route("ws/chat")]
public class ChatController : ControllerBase
{
  private readonly ISubscriber sub;
  private static ConcurrentDictionary<WebSocket, string> chatConnection = new ConcurrentDictionary<WebSocket, string>();

  public ChatController(IConnectionMultiplexer redis)
  {
    sub = redis.GetSubscriber();
  }


  [HttpGet("{gameID}")]
  public async Task<IActionResult> ChatWebSocket(uint gameID)
  {
    // Check if token is present in the query parameter
    if (!HttpContext.Request.Query.ContainsKey("token"))
    {
      return BadRequest(new { Message = "Token is missing." });
    }

    var token = HttpContext.Request.Query["token"].ToString();
    Console.WriteLine(token);

    var handler = new JwtSecurityTokenHandler();
    var jwtSecretKey = DotNetEnv.Env.GetString("JWT_SECRET_KEY");
    var appUrl = DotNetEnv.Env.GetString("APP_URL");
    var validationParameters = new TokenValidationParameters
    {
      ValidateIssuer = true,
      ValidateAudience = true,
      ValidateLifetime = true,
      ValidateIssuerSigningKey = true,
      ValidIssuer = appUrl,
      ValidAudience = appUrl,
      IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey))
    };

    try
    {
      // Validate token and extract username
      var principal = handler.ValidateToken(token, validationParameters, out _);
      var username = principal.Identity?.Name;

      if (string.IsNullOrEmpty(username))
      {
        return Unauthorized(new { Message = "Invalid token or username not found." });
      }
      if (HttpContext.WebSockets.IsWebSocketRequest)
      {
        var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        var chatKey = $"chat:game:{gameID}";

        // Add WebSocket to active connections
        chatConnection[webSocket] = username;
        Console.WriteLine($"Player {username} joined chat for game {gameID}.");

        // Subscribe to chat channel
        await sub.SubscribeAsync(RedisChannel.Literal(chatKey), async (channel, message) =>
        {
          if (webSocket.State == WebSocketState.Open)
          {
            var payload = Encoding.UTF8.GetBytes(message.ToString());
            await webSocket.SendAsync(
                    new ArraySegment<byte>(payload),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );
          }
        });

        var buffer = new byte[1024 * 4];
        while (webSocket.State == WebSocketState.Open)
        {
          var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

          if (result.MessageType == WebSocketMessageType.Close)
          {
            Console.WriteLine($"Player {username} disconnected from chat for game {gameID}.");
            chatConnection.TryRemove(webSocket, out _);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnected", CancellationToken.None);
            return new EmptyResult();
          }

          if (result.MessageType == WebSocketMessageType.Text)
          {
            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            Console.WriteLine($"Message from {username} in game {gameID}: {message}");

            // Publish message to Redis channel
            var chatMessage = $"{username}: {message}";
            await sub.PublishAsync(RedisChannel.Literal(chatKey), chatMessage);
          }
        }

      }
      else
      {
        return BadRequest(new { Message = "Request is not a WebSocket request." });
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"WebSocket error: {ex.Message}");
    }
    return new EmptyResult();
  }
}
