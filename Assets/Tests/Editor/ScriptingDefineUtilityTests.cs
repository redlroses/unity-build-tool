using System.Linq;
using BuildTool.Editor;
using NUnit.Framework;
using UnityEditor.Build;

namespace BuildTool.Editor.Tests
{
    [TestFixture]
    public class ScriptingDefineUtilityTests
    {
        private const string TestDefine = "BUILDTOOL_TEST_DEFINE_TEMP";

        [TearDown]
        public void TearDown()
        {
            if (ScriptingDefineUtility.IsDefined(TestDefine))
            {
                ScriptingDefineUtility.SetDefined(TestDefine, false);
            }
        }

        [Test]
        public void DefineConstants_HaveExpectedValues()
        {
            Assert.AreEqual("BUILDTOOL_CLI_EXPERIMENTAL", ScriptingDefineUtility.CliExperimentalDefine);
            Assert.AreEqual("BUILDTOOL_TRACKING", ScriptingDefineUtility.TrackingDefine);
        }

        [Test]
        public void GetTargetGroupsToUpdate_ContainsStandaloneAndServer()
        {
            var targets = ScriptingDefineUtility.GetTargetGroupsToUpdate().ToList();

            Assert.IsNotNull(targets);
            Assert.Contains(NamedBuildTarget.Standalone, targets);
            Assert.Contains(NamedBuildTarget.Server, targets);
        }

        [Test]
        public void SetDefined_EnablesAndDisablesSymbol()
        {
            ScriptingDefineUtility.SetDefined(TestDefine, false);
            Assert.IsFalse(ScriptingDefineUtility.IsDefined(TestDefine));

            ScriptingDefineUtility.SetDefined(TestDefine, true);
            Assert.IsTrue(ScriptingDefineUtility.IsDefined(TestDefine));

            ScriptingDefineUtility.SetDefined(TestDefine, false);
            Assert.IsFalse(ScriptingDefineUtility.IsDefined(TestDefine));
        }

        [Test]
        public void SetDefined_DuplicateCalls_RemainIdempotent()
        {
            ScriptingDefineUtility.SetDefined(TestDefine, true);
            ScriptingDefineUtility.SetDefined(TestDefine, true);

            Assert.IsTrue(ScriptingDefineUtility.IsDefined(TestDefine));

            ScriptingDefineUtility.SetDefined(TestDefine, false);
            ScriptingDefineUtility.SetDefined(TestDefine, false);

            Assert.IsFalse(ScriptingDefineUtility.IsDefined(TestDefine));
        }
    }
}
