using BuildTool.Editor.DirectoryBuilders;
using NUnit.Framework;
using BuildToolCore = BuildTool.Editor.BuildTool;

namespace BuildTool.Tracking.Editor.Tests
{
    [TestFixture]
    public class PatternLocationPathBuilderTrackingTests
    {
        [Test]
        public void BuildIndex_WhenRegisterDisabled_PreviewWritesLocal()
        {
            var previousRegisterState = BuildToolCore.IsRegisterVersionEnabled;
            try
            {
                BuildToolCore.IsRegisterVersionEnabled = false;

                var rootPath = BuildToolCore.GetRootBuildPath();
                Assert.IsTrue(
                    rootPath.Contains("local"),
                    $"Expected root preview to contain 'local', got '{rootPath}'");

                var builder = new PatternLocationPathBuilder();
                var profilePath = builder.GetLocationPathName(null, "Build/TestOut");
                Assert.IsTrue(
                    profilePath.Contains("local"),
                    $"Expected profile preview to contain 'local', got '{profilePath}'");
            }
            finally
            {
                BuildToolCore.IsRegisterVersionEnabled = previousRegisterState;
            }
        }

        [Test]
        public void BuildIndex_WhenRegisterEnabled_PreviewDoesNotWriteLocal()
        {
            var previousRegisterState = BuildToolCore.IsRegisterVersionEnabled;
            try
            {
                BuildToolCore.IsRegisterVersionEnabled = true;

                var rootPath = BuildToolCore.GetRootBuildPath();
                Assert.IsFalse(
                    rootPath.Contains(".local"),
                    $"Expected root preview to not contain '.local' when register is on, got '{rootPath}'");

                var builder = new PatternLocationPathBuilder();
                var profilePath = builder.GetLocationPathName(null, "Build/TestOut");
                Assert.IsFalse(
                    profilePath.Contains(".local"),
                    $"Expected profile preview to not contain '.local' when register is on, got '{profilePath}'");
            }
            finally
            {
                BuildToolCore.IsRegisterVersionEnabled = previousRegisterState;
            }
        }
    }
}
