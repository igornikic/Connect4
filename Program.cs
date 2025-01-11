using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

DotNetEnv.Env.Load();
var jwtSecretKey = DotNetEnv.Env.GetString("JWT_SECRET_KEY");
var appUrl = DotNetEnv.Env.GetString("APP_URL");

// Redis connection
ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("localhost:6379");
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
      options.TokenValidationParameters = new TokenValidationParameters
      {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = appUrl,
        ValidAudience = appUrl,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey))
      };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
  app.UseExceptionHandler("/Error");
  // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
  app.UseHsts();
}

app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Serve menu.html for /user/{username}
app.MapGet("/user/{username}", async (HttpContext context) =>
{
  var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Pages", "menu.html");
  context.Response.ContentType = "text/html";
  Console.WriteLine(filePath);
  await context.Response.SendFileAsync(filePath);
});

// Serve game.html for /game/{gameID}
app.MapGet("/game/{gameID}", async (HttpContext context) =>
{
  var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Pages", "game.html");
  context.Response.ContentType = "text/html";
  await context.Response.SendFileAsync(filePath);
});

// Serve register.html for /register
app.MapGet("/register", async (HttpContext context) =>
{
  var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Pages", "register.html");
  Console.WriteLine(filePath);
  context.Response.ContentType = "text/html";
  await context.Response.SendFileAsync(filePath);
});

// Serve leaderboard.html for /leaderboard
app.MapGet("/leaderboard", async (HttpContext context) =>
{
  var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Pages", "leaderboard.html");
  context.Response.ContentType = "text/html";
  await context.Response.SendFileAsync(filePath);
});

app.MapGet("/profile", async (HttpContext context) =>
{
  var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Pages", "profile.html");
  context.Response.ContentType = "text/html";
  await context.Response.SendFileAsync(filePath);
});

app.MapGet("/shop", async (HttpContext context) =>
{
  var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Pages", "shop.html");
  context.Response.ContentType = "text/html";
  await context.Response.SendFileAsync(filePath);
});

app.Run();
