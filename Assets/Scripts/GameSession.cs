using UnityEngine;

namespace SnowCannon
{
    /// <summary>Run state shared across scenes.</summary>
    public static class GameSession
    {
        public const string MenuScene = "Menu";
        public const string PlayScene = "Play";

        public static int LastScore;
        public static int LastLevelReached;
        public static int LastSnowmenHit;
        public static bool LastRunCompleted;

        public static void BeginRun()
        {
            LastScore = 0;
            LastLevelReached = 1;
            LastSnowmenHit = 0;
            LastRunCompleted = false;
        }

        public static void EndRun(int score, int level, int hits, bool survived)
        {
            LastScore = score;
            LastLevelReached = level;
            LastSnowmenHit = hits;
            LastRunCompleted = survived;
            Settings.HighScore = score;
            Settings.Save();
        }
    }
}
