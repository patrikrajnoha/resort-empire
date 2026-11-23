using System;
using UnityEngine;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>
    /// The one folder that holds the local save slots. Defaults to <see cref="Application.persistentDataPath"/>;
    /// tests and tools redirect it so they never touch a player's real saves.
    /// </summary>
    public static class ResortSaveLocation
    {
        private static string overrideRoot;

        public static string Root => overrideRoot ?? Application.persistentDataPath;

        /// <summary>Redirects <see cref="Root"/> until the returned scope is disposed.</summary>
        public static IDisposable Redirect(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A save folder is required.", nameof(root));
            var previous = overrideRoot;
            overrideRoot = root;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly string previous;
            private bool disposed;

            public Scope(string previous) { this.previous = previous; }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                overrideRoot = previous;
            }
        }
    }
}
