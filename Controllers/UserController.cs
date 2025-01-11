using Connect4.Models;
using Connect4.Utils;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using BC = BCrypt.Net.BCrypt;
using Microsoft.AspNetCore.Authorization;

namespace Connect4.Controllers
{
  [ApiController]
  [Route("api/[controller]")]
  public class UserController : ControllerBase
  {
    private readonly IDatabase db;

    public UserController(IConnectionMultiplexer redis)
    {
      db = redis.GetDatabase();
    }

    [HttpPost("register")]
    [ProducesResponseType(200)]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<User>> RegisterUser([FromBody] User user)
    {
      if (string.IsNullOrWhiteSpace(user.Username))
      {
        return BadRequest("Username is required.");
      }

      var existingUser = await db.HashGetAllAsync($"user:{user.Username}");
      if (existingUser.Length > 0)
      {
        return Conflict($"User '{user.Username}' already exists.");
      }

      // Hash the password
      user.Password = BC.HashPassword(user.Password);
      user.AvatarPath = "Assets/Avatars/avatar1.png";
      user.Balance = 0;
      user.SkillScore = 0;
      user.TotalGamesPlayed = 0;
      user.TotalWins = 0;
      user.TotalDraws = 0;
      user.PurchasedAvatars = new List<uint> { 0 }; // First avatar is free

      try
      {
        var token = GenerateJwtToken(user.Username);

        await db.HashSetAsync($"user:{user.Username}", new HashEntry[]
        {
          new HashEntry("Username", user.Username),
          new HashEntry("Password", user.Password),
          new HashEntry("AvatarPath", user.AvatarPath),
          new HashEntry("Balance", user.Balance),
          new HashEntry("SkillScore", user.SkillScore),
          new HashEntry("TotalGamesPlayed", user.TotalGamesPlayed),
          new HashEntry("TotalWins", user.TotalWins),
          new HashEntry("TotalDraws", user.TotalDraws),
          // Store PurchasedAvatars as a comma-separated string
        new HashEntry("PurchasedAvatars", string.Join(",", user.PurchasedAvatars))
        });

        return Ok(token);
      }
      catch (SecurityTokenException ex)
      {
        return Unauthorized($"Token generation error: {ex.Message}");
      }
      catch (Exception ex)
      {
        return StatusCode(500, $"Internal server error: {ex.Message}");
      }
    }

    [HttpGet]
    [Route("register")]
    public IActionResult GetRegisterPage()
    {
      Console.WriteLine("called");
      return File("~/register.html", "text/html");
    }


    [HttpPost("login")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<User>> LoginUser([FromBody] User user)
    {
      if (string.IsNullOrWhiteSpace(user.Username) || string.IsNullOrWhiteSpace(user.Password))
      {
        return BadRequest("Username and Password are required.");
      }

      var userHash = await db.HashGetAllAsync($"user:{user.Username}");
      if (userHash.Length == 0)
      {
        return Unauthorized($"Invalid Username or Password");
      }

      var storedPassword = userHash.FirstOrDefault(x => x.Name == "Password").Value;
      if (storedPassword.IsNullOrEmpty || !BC.Verify(user.Password, storedPassword))
      {
        return Unauthorized($"Invalid Username or Password");
      }

      try
      {
        var token = GenerateJwtToken(user.Username);
        return Ok(token);
      }
      catch (SecurityTokenException ex)
      {
        return Unauthorized($"Token generation error: {ex.Message}");
      }
      catch (Exception ex)
      {
        return StatusCode(500, $"Internal server error: {ex.Message}");
      }
    }

    [HttpGet]
    [Route("profile")]
    public IActionResult GetProfilePage()
    {
      Console.WriteLine("called");
      return File("~/profile.html", "text/html");
    }

    [HttpGet("{username}")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<User>> GetUser(string username)
    {
      string? authHeader = Request.Headers["Authorization"];
      if (string.IsNullOrEmpty(authHeader))
      {
        return BadRequest(new { Message = "Token is missing" });
      }

      // Extract token  and remove "Bearer " prefix
      string token = authHeader.Substring("Bearer ".Length).Trim();

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
        // Validate token
        var principal = handler.ValidateToken(token, validationParameters, out _);
        var usernameFromToken = principal.Identity?.Name;

        if (string.IsNullOrEmpty(usernameFromToken))
        {
          return Unauthorized(new { Message = "Invalid token" });
        }

        // Fetch user details from Redis
        var userHash = await db.HashGetAllAsync($"user:{username}");
        if (userHash.Length == 0)
        {
          return NotFound(new { Message = $"User '{username}' not found." });
        }

        var user = userHash
            .Where(u => u.Name != "Password")
            .Where(u => u.Name != "PurchasedAvatars")
            .ToDictionary(
                x => x.Name.ToString(),
                x => Helpers.GetValueWithType(x.Value)
            );

        var purchasedStr = userHash.FirstOrDefault(x => x.Name == "PurchasedAvatars").Value.ToString();
        var purchasedArr = purchasedStr.Split(',').Select(int.Parse).ToArray();
        user["PurchasedAvatars"] = purchasedArr;

        return Ok(user);
      }
      catch (SecurityTokenException ex)
      {
        return Unauthorized($"Token generation error: {ex.Message}");
      }
      catch (Exception ex)
      {
        return StatusCode(500, $"Internal server error: {ex.Message}");
      }
    }


    private string GenerateJwtToken(string username)
    {
      var claims = new[]
      {
          new Claim(JwtRegisteredClaimNames.Sub, username),
          new Claim(ClaimTypes.Name, username),
          new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
      };

      var jwtSecretKey = DotNetEnv.Env.GetString("JWT_SECRET_KEY");

      var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey));
      var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
      var appUrl = DotNetEnv.Env.GetString("APP_URL");
      Console.WriteLine(appUrl);
      var token = new JwtSecurityToken(
          issuer: appUrl,
          audience: appUrl,
          claims: claims,
          expires: DateTime.Now.AddMinutes(30),
          signingCredentials: creds
      );

      return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [HttpDelete("delete-account")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<ActionResult> DeleteAccount([FromHeader] string authorization)
    {
      // Ensure user is authenticated by checking JWT token
      var token = authorization?.Replace("Bearer ", "");
      if (string.IsNullOrEmpty(token))
      {
        return Unauthorized("You must be logged in to delete your account.");
      }

      try
      {
        var username = ValidateJwtToken(token);

        // Ensure user exists
        var userHash = await db.HashGetAllAsync($"user:{username}");
        if (userHash.Length == 0)
        {
          return NotFound("User not found.");
        }

        // Delete user's data from Redis
        await db.KeyDeleteAsync($"user:{username}");

        return Ok(new { Message = "Account deleted successfully." });
      }
      catch (Exception ex)
      {
        return StatusCode(500, $"Internal server error: {ex.Message}");
      }
    }

    private string ValidateJwtToken(string token)
    {
      // Validate JWT token and extract username from it
      var handler = new JwtSecurityTokenHandler();
      var jwtToken = handler.ReadToken(token) as JwtSecurityToken;
      if (jwtToken == null) throw new Exception("Invalid token.");
      var username = jwtToken?.Claims?.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

      if (string.IsNullOrEmpty(username))
      {
        throw new Exception("User not found in token.");
      }

      return username;
    }

    [HttpGet("Assets/Avatars/{avatarName}")]
    public IActionResult GetAvatar(string avatarName)
    {
      // Get root project directory path
      var rootDirectory = Directory.GetCurrentDirectory();

      // File path for avatar image
      var filePath = Path.Combine(rootDirectory, "Assets", "Avatars", avatarName);

      // Check if file exists
      if (!System.IO.File.Exists(filePath))
      {
        return NotFound();
      }

      // Read file bytes
      var fileBytes = System.IO.File.ReadAllBytes(filePath);

      // Get file extension and determine the MIME type
      var fileExtension = Path.GetExtension(avatarName).ToLower();
      var mimeType = fileExtension switch
      {
        ".png" => "image/png",
        ".jpg" => "image/jpeg",
        ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        _ => "application/octet-stream"
      };

      // Return file with it's MIME type
      return File(fileBytes, mimeType);
    }

    [HttpGet]
    [Route("shop")]
    public IActionResult GetShopPage()
    {
      return File("~/shop.html", "text/html");
    }

    [HttpGet("Assets/Avatars")]
    public IActionResult GetAllAvatars()
    {
      // Get root project directory path
      var rootDirectory = Directory.GetCurrentDirectory();
      var avatarsDirectory = Path.Combine(rootDirectory, "Assets", "Avatars");

      // Check if directory exists
      if (!Directory.Exists(avatarsDirectory))
      {
        return NotFound(new { Message = "Avatars directory not found." });
      }

      // Get all avatar files in the directory
      var avatarFiles = Directory.GetFiles(avatarsDirectory)
          .Select((filePath, index) =>
          {
            var fileName = Path.GetFileName(filePath);
            var price = 200 + (index * 200); // Increment price by 200 for each avatar

            return new
            {
              ID = index,
              Url = $"/Assets/Avatars/{fileName}",
              Price = price
            };
          })
          .ToList();

      // Return list of avatars
      return Ok(avatarFiles);
    }

    // FIX THIS XDDD
    [HttpPost("{username}/{avatarId}")]
    public async Task<IActionResult> PurchaseAvatar(string username, uint avatarId)
    {
      // Check if the user exists
      var userKey = $"user:{username}";
      var userHash = await db.HashGetAllAsync(userKey);
      if (userHash.Length == 0)
      {
        return NotFound($"User '{username}' not found.");
      }

      // Extract user's balance
      var balanceEntry = userHash.FirstOrDefault(x => x.Name == "Balance");
      var balance = balanceEntry.Value.IsNullOrEmpty ? 0 : (uint)balanceEntry.Value;

      // Extract purchased avatars
      var purchasedAvatarsStr = userHash.FirstOrDefault(x => x.Name == "PurchasedAvatars").Value.ToString();
      var purchasedAvatars = string.IsNullOrWhiteSpace(purchasedAvatarsStr)
          ? new HashSet<uint>()
          : new HashSet<uint>(purchasedAvatarsStr.Split(',').Select(uint.Parse));

      if (purchasedAvatars.Contains(avatarId))
      {
        return BadRequest(new { Message = "Avatar already purchased." });
      }

      var avatarPrice = 200 + (avatarId * 200);

      // Check if user has enough balance
      if (balance < avatarPrice)
      {
        return BadRequest(new { Message = "Insufficient balance." });
      }

      balance -= avatarPrice;
      purchasedAvatars.Add(avatarId);

      await db.HashSetAsync(userKey, new[]
      {
        new HashEntry("Balance", balance),
        new HashEntry("PurchasedAvatars", string.Join(",", purchasedAvatars))
    });

      return Ok(new { Message = "Avatar purchased successfully.", NewBalance = balance });
    }


    // WORKS
    [HttpPost("{username}/change-avatar/{avatarId}")]
    public async Task<IActionResult> ChangeAvatar(string username, uint avatarId)
    {
      // Check if the user exists
      var userHash = await db.HashGetAllAsync($"user:{username}");
      if (userHash.Length == 0)
      {
        return NotFound($"User '{username}' not found.");
      }

      var purchasedAvatarsStr = userHash.FirstOrDefault(x => x.Name == "PurchasedAvatars").Value.ToString();
      var purchasedAvatars = purchasedAvatarsStr.Split(',').Select(uint.Parse).ToList();

      if (!purchasedAvatars.Contains(avatarId))
      {
        return BadRequest(new { Message = "Avatar not owned by the user." });
      }
      try
      {
        // Update AvatarPath in Redis
        await db.HashSetAsync($"user:{username}", "AvatarPath", $"Assets/Avatars/avatar{avatarId}.png");
        return Ok(new { Message = "Avatar changed successfully." });
      }
      catch (Exception ex)
      {
        return StatusCode(500, $"Internal server error: {ex.Message}");
      }

    }
    [HttpGet("{username}/purchased-avatars")]
    public async Task<IActionResult> GetPurchasedAvatars(string username)
    {
      var userHash = await db.HashGetAllAsync($"user:{username}");
      if (userHash.Length == 0)
      {
        return NotFound($"User '{username}' not found.");
      }

      // Get purchased avatars
      var purchasedAvatarsStr = userHash.FirstOrDefault(x => x.Name == "PurchasedAvatars").Value.ToString();
      var purchasedAvatars = purchasedAvatarsStr.Split(',').Select(uint.Parse).ToList();

      return Ok(purchasedAvatars);
    }
  }
}
