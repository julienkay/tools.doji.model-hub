#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Keeps files written to <c>persistentDataPath</c> across page loads on the Web. There the folder lives in memory
    /// until the player copies it to IndexedDB, which Unity does only on request unless the page template sets
    /// <c>autoSyncPersistentDataPath</c>. Other platforms write to disk directly, so this does nothing there.
    /// </summary>
    internal static class PersistentStorage
    {
        /// <summary>
        /// Saves the files written so far. Call it after a download, a conversion, or a delete.
        /// </summary>
        public static void Flush()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            JS_FileSystem_Sync();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Part of Unity's Web framework: starts copying persistentDataPath to IndexedDB.
        [DllImport("__Internal")]
        private static extern void JS_FileSystem_Sync();
#endif
    }
}
