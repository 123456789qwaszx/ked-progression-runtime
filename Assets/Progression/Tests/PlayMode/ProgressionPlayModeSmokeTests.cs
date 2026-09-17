using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Ked.Progression.Tests
{
    public sealed class ProgressionPlayModeSmokeTests
    {
        [UnityTest]
        public IEnumerator RuntimeAssembly_IsAvailableInPlayMode()
        {
            Assert.That(
                typeof(ProgressionDriver).Assembly.GetName().Name,
                Is.EqualTo("Ked.Progression"));

            yield return null;
        }
    }
}
