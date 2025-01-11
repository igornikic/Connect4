using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Connect4.Models;
using Connect4.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Connect4.Controllers
{
  [ApiController]
  [Route("")]
  public class GameController : ControllerBase
  {
    private readonly IDatabase db;
    private readonly ISubscriber sub;

    private static ConcurrentDictionary<string, WebSocket> queuedPlayersSockets = new ConcurrentDictionary<string, WebSocket>();
    private static ConcurrentDictionary<WebSocket, string> activeConnectionsToPlayers = new ConcurrentDictionary<WebSocket, string>();

    public GameController(IConnectionMultiplexer redis)
    {
      db = redis.GetDatabase();
      sub = redis.GetSubscriber();
    }

    [HttpGet("ws/{username}")]
    public async Task ListenForMatch(string username)
    {
      if (HttpContext.WebSockets.IsWebSocketRequest)
      {
        var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        queuedPlayersSockets[username] = webSocket;

        Console.WriteLine($"WebSocket connection established for user: {username}");

        // Keep connection open to listen for messages (if needed)
        var buffer = new byte[1024 * 4];
        while (webSocket.State == WebSocketState.Open)
        {
          var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
          if (result.MessageType == WebSocketMessageType.Close)
          {
            Console.WriteLine($"WebSocket connection closed for user: {username}");
            queuedPlayersSockets.TryRemove(username, out _);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
          }
        }
      }
      else
      {
        HttpContext.Response.StatusCode = 400;
      }
    }

    [HttpGet("ws/game/{gameID}")]
    public async Task<ActionResult<Game>> GameWebSocket(uint gameID)
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

        // Check if game is over before establishing WebSocket connection
        var gameKey = $"game:{gameID}:game{gameID}";
        var gameStatus = db.HashGet(gameKey, "winner").ToString();
        Console.WriteLine(gameStatus);
        if (!string.IsNullOrEmpty(gameStatus))
        {
          // Return response if the game is over
          return BadRequest(new { Message = "Game is already over." });
        }

        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
          var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

          // Store active WebSocket connection
          activeConnectionsToPlayers[webSocket] = username;
          Console.WriteLine($"WebSocket connection established for game {gameID} by player {username}.");

          // Send initial game state over WebSocket
          var gameData = await GetInitialGameData(gameID); // Get game data from Redis
          var initialGameData = JsonSerializer.Serialize(gameData);
          await webSocket.SendAsync(
              new ArraySegment<byte>(Encoding.UTF8.GetBytes(initialGameData)),
              WebSocketMessageType.Text,
              true,
              CancellationToken.None);

          // Start monitoring the next player's turn
          var initialTurn = db.HashGet(gameKey, "currentTurn").ToString();
          _ = MonitorPlayerTurns(gameID, initialTurn); // Fire and forget

          // Subscribe to game updates
          await sub.SubscribeAsync(RedisChannel.Literal(gameKey), async (channel, message) =>
          {
            if (webSocket.State == WebSocketState.Open)
            {
              await webSocket.SendAsync(
                    new ArraySegment<byte>(Encoding.UTF8.GetBytes(message.ToString())),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );
            }
          });

          var buffer = new byte[1024 * 4];
          while (webSocket.State == WebSocketState.Open)
          {
            // Check if game is over before making move
            gameStatus = db.HashGet(gameKey, "winner").ToString();

            if (!string.IsNullOrEmpty(gameStatus))
            {
              Console.WriteLine("Game over. Closing all WebSocket connections for this game.");

              // Iterate through all connections and close those related to this game
              foreach (var connection in activeConnectionsToPlayers.ToList())
              {
                var webSocketFromDictionary = connection.Key; // Use WebSocket from the dictionary
                if (webSocketFromDictionary.State == WebSocketState.Open)
                {
                  try
                  {
                    await webSocketFromDictionary.CloseAsync(WebSocketCloseStatus.NormalClosure, "Game is over", CancellationToken.None);
                  }
                  catch (Exception ex)
                  {
                    Console.WriteLine($"Error closing WebSocket: {ex.Message}");
                  }
                }

                // Remove connection from the dictionary regardless of the state
                activeConnectionsToPlayers.TryRemove(webSocketFromDictionary, out _);
              }
              break;
            }

            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            if (result.MessageType == WebSocketMessageType.Close)
            {
              Console.WriteLine($"Player {username} disconnected.");
              return new EmptyResult();
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
              var index = int.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
              Console.WriteLine($"Player {username} made a move at index {index}.");

              // Update game board
              var gameUpdate = ProcessPlayerMove(gameID, username, index);

              // Publish update to all clients
              if (gameUpdate != null)
              {
                await db.PublishAsync(RedisChannel.Literal(gameKey), gameUpdate);

                // Start monitoring the next player's turn
                var nextTurn = db.HashGet(gameKey, "currentTurn").ToString();
                _ = MonitorPlayerTurns(gameID, nextTurn); // Fire and forget
              }
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

    private string? ProcessPlayerMove(uint gameID, string player, int col)
    {
      var gameKey = $"game:{gameID}:game{gameID}";
      var boardKey = $"game:{gameID}:board{gameID}";

      // Fetch game state and board
      var board = db.ListRange(boardKey).Select(x => (int)x).ToArray();
      var currentTurn = db.HashGet(gameKey, "currentTurn").ToString();

      if (currentTurn != player) return null;

      // Player's disk
      int playerNumber = player == db.HashGet(gameKey, "player1").ToString() ? 1 : 2;

      // Place disk at the deepest free row
      int boardIndex = -1;
      bool validMove = false;
      for (int row = 5; row >= 0; row--)
      {
        boardIndex = row * 7 + col;
        if (board[boardIndex] == 0)
        {
          board[boardIndex] = playerNumber;
          validMove = true;
          break;
        }
      }

      if (!validMove) return null;

      // Check for winner
      bool isWinner = CheckWinner(board, boardIndex, playerNumber);

      // Check for draw
      bool isDraw = !isWinner && board.All(cell => cell != 0);

      if (isWinner || isDraw)
      {
        HandleGameEnd(gameID, player, isWinner, isDraw);
      }

      // Update board and current turn
      db.ListSetByIndex(boardKey, boardIndex, playerNumber);
      var nextTurn = player == db.HashGet(gameKey, "player1").ToString()
          ? db.HashGet(gameKey, "player2").ToString()
          : db.HashGet(gameKey, "player1").ToString();

      db.HashSet(gameKey, "currentTurn", nextTurn);
      var winner = isWinner ? player : isDraw ? "None" : null;
      db.HashSet(gameKey, "winner", winner);

      // Return game state
      var gameState = new
      {
        board,
        currentTurn = nextTurn,
        winner
      };
      return JsonSerializer.Serialize(gameState);
    }

    private bool CheckWinner(int[] board, int boardIndex, int playerNumber)
    {
      int[][] directions = new[]
      {
        new[] { 1, -1 },   // Horizontal
        new[] { 7, -7 },   // Vertical
        new[] { 6, -6 },   // Diagonal 1
        new[] { 8, -8 }    // Diagonal 2
      };

      foreach (var direction in directions)
      {
        int count = 1; // Count includes current disk

        foreach (int step in direction)
        {
          int index = boardIndex;
          while (true)
          {
            index += step;

            // Index out of bounds, break
            if (index < 0 || index >= 42) break;

            // Calculate row and column of new index
            int newRow = index / 7;
            int newColumn = index % 7;

            // Calculate row and column of previous index
            int currentRow = (index - step) / 7;
            int currentColumn = (index - step) % 7;

            // Horizontal checks
            if (step == 1 || step == -1)
            {
              if (newRow != currentRow) break;
            }

            // Diagonal checks
            if (step == 6 || step == -6 || step == 8 || step == -8)
            {
              if (Math.Abs(newColumn - currentColumn) != 1) break;
            }

            // Check if the current position matches player number
            if (board[index] != playerNumber) break;

            // Increment count for valid match
            count++;

            if (count >= 4) return true; // Win condition met
          }
        }
      }

      return false;
    }

    private void HandleGameEnd(uint gameID, string winner, bool isWinner, bool isDraw, string? wonOnTime = null)
    {
      var player1 = db.HashGet($"game:{gameID}:game{gameID}", "player1").ToString();
      var player2 = db.HashGet($"game:{gameID}:game{gameID}", "player2").ToString();

      UpdatePlayerStats(player1, isWinner && winner == player1, isDraw, winner != player1);
      UpdatePlayerStats(player2, isWinner && winner == player2, isDraw, winner != player2);

      NotifyPlayers(player1, wonOnTime);
      NotifyPlayers(player2, wonOnTime);
    }

    private void UpdatePlayerStats(string username, bool isWin, bool isDraw, bool isLoss)
    {
      var userKey = $"user:{username}";

      // Check if user exists
      if (!db.KeyExists(userKey)) return;

      // Increment total games played
      db.HashIncrement(userKey, "TotalGamesPlayed", 1);

      if (isWin)
      {
        int increase = new Random().Next(10, 16);
        db.HashIncrement(userKey, "TotalWins", 1);
        double newSkillScore = db.HashIncrement(userKey, "SkillScore", increase);
        db.HashIncrement(userKey, "Balance", 10);

        // Update leaderboard
        UpdateLeaderboard(username, newSkillScore);
      }
      else if (isLoss)
      {
        int decrease = new Random().Next(10, 16);
        double currentSkillScore = (double)db.HashGet(userKey, "SkillScore"); // Get current score
        double newSkillScore = Math.Max(0, currentSkillScore - decrease);
        db.HashSet(userKey, "SkillScore", newSkillScore); // Avoid negatives

        // Update leaderboard
        UpdateLeaderboard(username, newSkillScore);
      }
      else if (isDraw)
      {
        db.HashIncrement(userKey, "TotalDraws", 1);
        db.HashIncrement(userKey, "Balance", 5);
      }
    }


    // Notify to players end game results
    private async void NotifyPlayers(string username, string? wonOnTime)
    {
      if (activeConnectionsToPlayers.Values.Contains(username))
      {
        var userKey = $"user:{username}";
        var updatedUser = db.HashGetAll(userKey).Where(u => u.Name != "Password")
                            .ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());

        if (!string.IsNullOrEmpty(wonOnTime))
        {
          updatedUser["WonOnTime"] = wonOnTime;
        }
        var userUpdateMessage = JsonSerializer.Serialize(updatedUser);

        foreach (var connection in activeConnectionsToPlayers.Where(c => c.Value == username))
        {
          var webSocket = connection.Key;
          if (webSocket.State == WebSocketState.Open)
          {
            try
            {
              await webSocket.SendAsync(
                  new ArraySegment<byte>(Encoding.UTF8.GetBytes(userUpdateMessage)),
                  WebSocketMessageType.Text,
                  true,
                  CancellationToken.None);

              // Delay a little bit to make sure userUpdateMessage is sent
              await Task.Delay(1000);
              await webSocket.CloseAsync(
                  WebSocketCloseStatus.NormalClosure,
                  "Game over",
                  CancellationToken.None
              );

              Console.WriteLine($"{username} disconnected by NotifyPlayer");

              activeConnectionsToPlayers.TryRemove(webSocket, out _);
            }
            catch (Exception ex)
            {
              Console.WriteLine($"Error notifying player {username}: {ex.Message}");
            }
          }
        }
      }
    }


    [HttpPost("api/game/queue/{username}")]
    [Authorize]
    public async Task<IActionResult> JoinQueue(string username)
    {
      // Get SkillScore of user
      var skillScoreValue = await db.HashGetAsync($"user:{username}", "SkillScore");
      uint skillScore = (uint)skillScoreValue;

      var matchmakingQueueKey = "MatchmakingQueue";

      // Add player to sorted set with their skill score
      await db.SortedSetAddAsync(matchmakingQueueKey, username, skillScore);

      uint minSkill;
      if (skillScore > 200)
        minSkill = skillScore - 200;
      else
        minSkill = 0;

      uint maxSkill = skillScore + 200;

      // Get players within the specified skill range
      var potentialPlayers = await db.SortedSetRangeByScoreAsync(matchmakingQueueKey, minSkill, maxSkill);

      if (potentialPlayers.Length >= 2)
      {
        // Pop two players from the range
        var player1 = potentialPlayers[0];
        var player2 = potentialPlayers[1];

        // New game
        var game = new Game
        {
          ID = (uint)db.StringIncrement("game:ID_counter"),
          Player1 = player1.ToString(),
          Player2 = player2.ToString(),
          CurrentTurn = new Random().Next(2) == 0 ? player1 : player2,
          Winner = "",
        };

        // Store game information
        var gameKey = $"game:{game.ID}:game{game.ID}";
        await db.HashSetAsync(gameKey, new HashEntry[]
        {
          new HashEntry("ID", game.ID),
          new HashEntry("player1", game.Player1),
          new HashEntry("player2", game.Player2),
          new HashEntry("currentTurn", game.CurrentTurn),
          new HashEntry("winner", game.Winner)
        });

        // Store initial game board
        await db.ListRightPushAsync($"game:{game.ID}:board{game.ID}", Enumerable.Repeat((RedisValue)0, 42).ToArray());

        // Remove matched players from the queue
        await db.SortedSetRemoveAsync(matchmakingQueueKey, player1);
        await db.SortedSetRemoveAsync(matchmakingQueueKey, player2);

        if (queuedPlayersSockets.TryGetValue(player1.ToString(), out var socket1) && socket1.State == WebSocketState.Open)
        {
          var message = Encoding.UTF8.GetBytes(game.ID.ToString());
          await socket1.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        if (queuedPlayersSockets.TryGetValue(player2.ToString(), out var socket2) && socket2.State == WebSocketState.Open)
        {
          var message = Encoding.UTF8.GetBytes(game.ID.ToString());
          await socket2.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        // Game would be ready here
        return Ok("Searching for Opponent...");
      }
      else
      {
        // Not enough players in skill range yet
        return Ok("Searching for Opponent...");
      }
    }

    // NOT USING THIS FOR NOW
    [HttpGet("api/game/data/{gameID}")]
    [Authorize]
    public async Task<IActionResult> GetGameData(uint gameID)
    {
      var gameKey = $"game:{gameID}:game{gameID}";

      var gameData = await db.HashGetAllAsync(gameKey);

      if (gameData.Length == 0)
      {
        return NotFound($"Game with ID {gameID} not found.");
      }

      // Convert HashEntry array into dictionary
      var gameDataDict = gameData.ToDictionary(
          x => x.Name.ToString(),
          x => Helpers.GetValueWithType(x.Value)
      );

      // Serialize dictionary into JSON
      var gameDataJson = JsonSerializer.Serialize(gameDataDict);

      return Ok(gameDataJson);
    }
    public async Task<Dictionary<string, object?>> GetInitialGameData(uint gameID)
    {
      var gameKey = $"game:{gameID}:game{gameID}";

      var gameData = await db.HashGetAllAsync(gameKey);

      if (gameData.Length == 0)
      {
        throw new Exception($"Game with ID {gameID} not found.");
      }

      // Convert HashEntry array into dictionary
      var gameDataDict = gameData.ToDictionary(
          x => x.Name.ToString(),   // Key as string
          x => Helpers.GetValueWithType(x.Value) // Method to get the value with correct type
      );

      return gameDataDict;
    }

    public void UpdateLeaderboard(string username, double skillScore)
    {
      const string leaderboardKey = "leaderboard";

      // Update user's score in sorted set
      db.SortedSetAdd(leaderboardKey, username, skillScore);
    }


    // Get top 10 Players
    [HttpGet("/api/leaderboard")]
    public List<PlayerRank> GetTopPlayers(int topN = 10)
    {
      const string leaderboardKey = "leaderboard";
      // Get top N players from the sorted set (with descending order)
      var players = db.SortedSetRangeByRankWithScores(leaderboardKey, 0, topN - 1, Order.Descending);

      var resultList = new List<PlayerRank>();
      foreach (var player in players)
      {
        var playerObj = new PlayerRank { Username = player.Element.ToString(), SkillScore = player.Score };
        resultList.Add(playerObj);
      }

      return resultList;
    }

    private async Task MonitorPlayerTurns(uint gameID, string currentPlayer)
    {
      var gameKey = $"game:{gameID}:game{gameID}";

      // Start a 40-second countdown for current player's turn
      var turnTimeout = TimeSpan.FromSeconds(40);
      var startTime = DateTime.UtcNow;

      while ((DateTime.UtcNow - startTime) < turnTimeout)
      {
        // Check if the game is already over
        var winner = db.HashGet(gameKey, "winner").ToString();
        if (!string.IsNullOrEmpty(winner)) return;

        // Check if the turn has changed
        var currentTurn = db.HashGet(gameKey, "currentTurn").ToString();
        if (currentTurn != currentPlayer) return;

        await Task.Delay(1000); // Wait 1 second and check again
      }

      var gameStatus = db.HashGet(gameKey, "winner").ToString();
      if (string.IsNullOrEmpty(gameStatus))
      {
        Console.WriteLine($"Player {currentPlayer} failed to make a move in time. Declaring other player as the winner.");

        var player1 = db.HashGet(gameKey, "player1").ToString();
        var player2 = db.HashGet(gameKey, "player2").ToString();
        var opponent = currentPlayer == player1 ? player2 : player1;

        // Declare opponent as the winner
        db.HashSet(gameKey, "winner", opponent);
        Console.WriteLine($"OPPONENT WON:{opponent} p1{player1}. p2 {player1}, cur {currentPlayer}, gameID{gameID}");
        HandleGameEnd(gameID, opponent, true, false, opponent);
      }
    }
  }
}
