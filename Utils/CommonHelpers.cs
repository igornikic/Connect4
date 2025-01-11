namespace Connect4.Utils
{
  public static class Helpers
  {
    public static object? GetValueWithType(RedisValue redisValue)
    {
      if (redisValue.IsNullOrEmpty)
      {
        return null;
      }

      if (int.TryParse(redisValue, out int intValue))
      {
        return intValue;
      }

      if (bool.TryParse(redisValue, out bool boolValue))
      {
        return boolValue;
      }

      if (double.TryParse(redisValue, out double doubleValue))
      {
        return doubleValue;
      }

      return redisValue.ToString();
    }
  }
}