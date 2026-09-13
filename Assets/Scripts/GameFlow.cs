using UnityEngine;

namespace SnowCannon
{
    /// <summary>
    /// Central place for the one global flow action the whole game shares: leaving the
    /// application. ESC routes here from both the title screen and the play scene so the
    /// player can always bail out without reaching for Alt+F4.
    /// </summary>
    public static class GameFlow
    {
        public static void Quit()
        {
#if UNITY_EDITOR
            // In the editor, closing the play session is the equivalent of quitting.
            if (UnityEditor.EditorApplication.isPlaying)
            {
                UnityEditor.EditorApplication.ExitPlaymode();
                return;
            }
#endif
            Application.Quit();
        }
    }
}
