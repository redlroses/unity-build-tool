using NUnit.Framework;
using UnityEditor;
using BuildTool.Editor.DirectoryBuilders;

namespace BuildTool.Editor.Tests
{
    [TestFixture]
    public class SimpleLocationPathNameBuilderTests
    {
        private class TestableSimpleLocationPathNameBuilder : SimpleLocationPathNameBuilder
        {
            public (string suffix, string extension) TestGetPlatformSuffixAndExtension(BuildTarget target)
            {
                return GetPlatformSuffixAndExtension(target);
            }
        }

        [Test]
        public void AndroidExtension_ReflectsBuildAppBundleSetting()
        {
            var builder = new TestableSimpleLocationPathNameBuilder();
            var originalSetting = EditorUserBuildSettings.buildAppBundle;

            try
            {
                EditorUserBuildSettings.buildAppBundle = true;
                var (_, extAab) = builder.TestGetPlatformSuffixAndExtension(BuildTarget.Android);
                Assert.AreEqual(".aab", extAab);

                EditorUserBuildSettings.buildAppBundle = false;
                var (_, extApk) = builder.TestGetPlatformSuffixAndExtension(BuildTarget.Android);
                Assert.AreEqual(".apk", extApk);
            }
            finally
            {
                EditorUserBuildSettings.buildAppBundle = originalSetting;
            }
        }

        [Test]
        public void WindowsExtension_ReturnsExe()
        {
            var builder = new TestableSimpleLocationPathNameBuilder();
            var (_, ext) = builder.TestGetPlatformSuffixAndExtension(BuildTarget.StandaloneWindows64);
            Assert.AreEqual(".exe", ext);
        }

        [Test]
        public void GetLocationPathName_NormalizesToForwardSlashes()
        {
            var builder = new SimpleLocationPathNameBuilder();
            var path = builder.GetLocationPathName(null, "Build/TestFolder");

            Assert.IsFalse(string.IsNullOrEmpty(path));
            Assert.IsFalse(path.Contains("\\"), "LocationPathName must not contain backslashes");
            Assert.IsTrue(path.Contains("/"));
        }
    }
}
