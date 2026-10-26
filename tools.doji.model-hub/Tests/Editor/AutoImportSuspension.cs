using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Stops automatic imports while the tests run, so a test's import records change only when the test changes them.
    /// </summary>
    [SetUpFixture]
    internal sealed class AutoImportSuspension
    {
        private bool _wasSuspended;

        [OneTimeSetUp]
        public void Suspend()
        {
            _wasSuspended = AutoImport.Suspended;
            AutoImport.Suspended = true;
        }

        [OneTimeTearDown]
        public void Resume()
        {
            AutoImport.Suspended = _wasSuspended;
        }
    }
}
