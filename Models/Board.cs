namespace Connect4.Models
{
  public class Board
  {
    public required uint GameID {get; set;}
    public uint[,] BoardState { get; set; } = new uint[6, 7];
  }
}
