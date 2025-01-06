namespace Connect4.Models
{
  public class User
  {
    [Required(ErrorMessage = "Please enter your username")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "Your username must be between 2 and 20 characters long")]
    public required string Username { get; set; }

    [Required(ErrorMessage = "Please enter your password")]
    [StringLength(24, MinimumLength = 8, ErrorMessage = "Your password must be between 8 and 24 characters long")]
    public required string Password { get; set; }

    [Required(ErrorMessage = "Please choose an avatar")]
    public string AvatarPath { get; set; } = "Assets/Avatars/avatar1.png";

    public uint Balance { get; set; } = 0;
    public uint SkillScore { get; set; } = 0;
    public uint TotalGamesPlayed { get; set; } = 0;
    public uint TotalWins { get; set; } = 0;
    public uint TotalDraws { get; set; } = 0;
  }
}
