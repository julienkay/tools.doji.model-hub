using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the asset that stores which optional model files a project turned on.
    /// </summary>
    internal sealed class ProjectOptionalFileSelectionsAssetTests
    {
        /// <summary>
        /// Unity finds the script for the selections asset. Without it, saved choices are lost on the next asset refresh.
        /// </summary>
        [Test]
        public void SelectionsAsset_HasMatchingScriptFile()
        {
            var asset = ScriptableObject.CreateInstance<ProjectOptionalFileSelectionsAsset>();
            try
            {
                var script = MonoScript.FromScriptableObject(asset);

                Assert.That(script, Is.Not.Null);
                Assert.That(script.GetClass(), Is.EqualTo(typeof(ProjectOptionalFileSelectionsAsset)));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
