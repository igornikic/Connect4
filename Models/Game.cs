namespace Connect4.Models
{
  public class Game
  {
    public required uint ID {get; set;}
    public required string Player1 { get; set; }

    public required string Player2 { get; set; }

    public string? CurrentTurn { get; set; }

    public string Winner { get; set; } = string.Empty;
  }
}
