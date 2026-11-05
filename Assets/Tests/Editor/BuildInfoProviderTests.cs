using BuildTool.Tracking;
using NUnit.Framework;

namespace BuildTool.Editor.Tests
{
    public class BuildInfoProviderTests
    {
        private BuildInfo _originalInfo;

        [SetUp]
        public void SetUp()
        {
            _originalInfo = BuildInfoProvider.BuildInfo;
        }

        [TearDown]
        public void TearDown()
        {
            BuildInfoProvider.BuildInfo = _originalInfo;
        }

        [Test]
        public void BuildInfo_WhenAccessed_ReturnsNonNullInstance()
        {
            BuildInfoProvider.Reset();

            var info = BuildInfoProvider.BuildInfo;

            Assert.IsNotNull(info);
        }

        [Test]
        public void IsRegistered_WhenUnregisteredBuildInfo_ReturnsFalse()
        {
            BuildInfoProvider.BuildInfo = BuildInfo.Create();

            Assert.IsFalse(BuildInfoProvider.IsRegistered);
        }

        [Test]
        public void IsRegistered_WhenRegisteredBuildInfo_ReturnsTrue()
        {
            BuildInfoProvider.BuildInfo = BuildInfo.Create("42", "abcdef1", "2026-11-04");

            Assert.IsTrue(BuildInfoProvider.IsRegistered);
            Assert.AreEqual("42", BuildInfoProvider.BuildInfo.Index);
            Assert.AreEqual("abcdef1", BuildInfoProvider.BuildInfo.Commit);
            Assert.AreEqual("2026-11-04", BuildInfoProvider.BuildInfo.Date);
        }

        [Test]
        public void IsRegistered_WhenExplicitlyUnregistered_ReturnsFalse()
        {
            BuildInfoProvider.BuildInfo = BuildInfo.Create("local", "abcdef1", "2026-11-04", false);

            Assert.IsFalse(BuildInfoProvider.IsRegistered);
        }

        [Test]
        public void Reset_ClearsCachedInstanceAndReloadsOnNextAccess()
        {
            BuildInfoProvider.BuildInfo = BuildInfo.Create("999", "custom", "2026-12-31");
            Assert.AreEqual("999", BuildInfoProvider.BuildInfo.Index);

            BuildInfoProvider.Reset();

            var reloaded = BuildInfoProvider.BuildInfo;
            Assert.IsNotNull(reloaded);
            Assert.AreNotEqual("999", reloaded.Index);
        }
    }
}
