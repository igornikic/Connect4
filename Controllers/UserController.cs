using Connect4.Models;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using BC = BCrypt.Net.BCrypt;

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
      user.TotalGamesPlayed = 0;
      user.TotalWins = 0;

      try
      {
        var token = GenerateJwtToken(user.Username);

        await db.HashSetAsync($"user:{user.Username}", new HashEntry[]
        {
          new HashEntry("Username", user.Username),
          new HashEntry("Password", user.Password),
          new HashEntry("AvatarPath", user.AvatarPath),
          new HashEntry("Balance", user.Balance),
          new HashEntry("TotalGamesPlayed", user.TotalGamesPlayed),
          new HashEntry("TotalWins", user.TotalWins)
        });

        return Ok(token);
      }
      catch (SecurityTokenException ex)
      {
          return StatusCode(500, $"Token generation error: {ex.Message}");
      }
      catch (Exception ex)
      {
        return StatusCode(500, $"Internal server error: {ex.Message}");
      }
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
          return StatusCode(500, $"Token generation error: {ex.Message}");
      }
      catch (Exception ex)
      {
        return StatusCode(500, $"Internal server error: {ex.Message}");
      }
    }

    [HttpGet("{username}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<User>> GetUser(string username)
    {
      var userHash = await db.HashGetAllAsync($"user:{username}");
      if (userHash.Length == 0)
      {
          return NotFound($"User '{username}' not found.");
      }

      var user = string.Join("\n", userHash.Select(u => $"{u.Name}: {u.Value}"));

      return Ok(user);
    }

    private string GenerateJwtToken(string username)
    {
      var claims = new[]
      {
          new Claim(JwtRegisteredClaimNames.Sub, username),
          new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
      };

      var jwtSecretKey = DotNetEnv.Env.GetString("JWT_SECRET_KEY");

      var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey));
      var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

      var token = new JwtSecurityToken(
          issuer: "http://localhost:5175",
          audience: "http://localhost:5175",
          claims: claims,
          expires: DateTime.Now.AddMinutes(30),
          signingCredentials: creds
      );

      return new JwtSecurityTokenHandler().WriteToken(token);
    }
  }
}
