using System;
using System.Collections.Concurrent;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Runs actions on the main thread at the next Editor update. Unlike <see cref="EditorApplication.delayCall"/>,
    /// <see cref="EditorApplication.update"/> also runs while the Editor is in the background and a tool such as
    /// the Unity CLI keeps it ticking, so work posted here finishes without the user focusing the Editor.
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorMainThread
    {
        private static readonly ConcurrentQueue<Action> Pending = new();

        static EditorMainThread()
        {
            EditorApplication.update += Drain;
        }

        /// <summary>
        /// Queues <paramref name="action"/> for the next Editor update. Safe to call from any thread.
        /// </summary>
        public static void Post(Action action)
        {
            if (action != null)
            {
                Pending.Enqueue(action);
            }
        }

        private static void Drain()
        {
            while (Pending.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogException(exception);
                }
            }
        }
    }
}
