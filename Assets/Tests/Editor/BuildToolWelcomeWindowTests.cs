using NUnit.Framework;
using UnityEditor;

namespace BuildTool.Editor.Tests
{
    [TestFixture]
    public class BuildToolWelcomeWindowTests
    {
        private bool _originalShowOnUpdate;
        private bool _originalShowOnStart;
        private string _originalLastVersion;

        [SetUp]
        public void SetUp()
        {
            _originalShowOnUpdate = EditorPrefs.GetBool(BuildToolWelcomeWindow.ShowOnUpdatePrefKey, true);
            _originalShowOnStart = EditorPrefs.GetBool(BuildToolWelcomeWindow.ShowOnStartPrefKey, false);
            _originalLastVersion = EditorPrefs.GetString(BuildToolWelcomeWindow.LastVersionPrefKey, string.Empty);
        }

        [TearDown]
        public void TearDown()
        {
            EditorPrefs.SetBool(BuildToolWelcomeWindow.ShowOnUpdatePrefKey, _originalShowOnUpdate);
            EditorPrefs.SetBool(BuildToolWelcomeWindow.ShowOnStartPrefKey, _originalShowOnStart);
            EditorPrefs.SetString(BuildToolWelcomeWindow.LastVersionPrefKey, _originalLastVersion);
        }

        [Test]
        public void GetPackageVersion_ReturnsNonEmptyVersion()
        {
            var version = BuildToolWelcomeWindow.GetPackageVersion();
            Assert.IsFalse(string.IsNullOrEmpty(version));
            Assert.IsTrue(char.IsDigit(version[0]));
        }

        [Test]
        public void ShowOnUpdatePreference_PersistsCorrectly()
        {
            EditorPrefs.SetBool(BuildToolWelcomeWindow.ShowOnUpdatePrefKey, false);
            Assert.IsFalse(EditorPrefs.GetBool(BuildToolWelcomeWindow.ShowOnUpdatePrefKey, true));

            EditorPrefs.SetBool(BuildToolWelcomeWindow.ShowOnUpdatePrefKey, true);
            Assert.IsTrue(EditorPrefs.GetBool(BuildToolWelcomeWindow.ShowOnUpdatePrefKey, false));
        }

        [Test]
        public void ShowOnStartPreference_PersistsCorrectly()
        {
            EditorPrefs.SetBool(BuildToolWelcomeWindow.ShowOnStartPrefKey, true);
            Assert.IsTrue(EditorPrefs.GetBool(BuildToolWelcomeWindow.ShowOnStartPrefKey, false));

            EditorPrefs.SetBool(BuildToolWelcomeWindow.ShowOnStartPrefKey, false);
            Assert.IsFalse(EditorPrefs.GetBool(BuildToolWelcomeWindow.ShowOnStartPrefKey, true));
        }

        [Test]
        public void LastVersionPreference_PersistsCorrectly()
        {
            const string testVersion = "99.99.99";
            EditorPrefs.SetString(BuildToolWelcomeWindow.LastVersionPrefKey, testVersion);
            Assert.AreEqual(
                testVersion,
                EditorPrefs.GetString(BuildToolWelcomeWindow.LastVersionPrefKey, string.Empty));
        }

        [Test]
        public void CheckStartup_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => BuildToolWelcomeWindow.CheckStartup());
        }

        [Test]
        public void GetIcon_ValidOrFallback_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                var icon = BuildToolWelcomeWindow.GetIcon("d_BuildSettings.Editor", "d_CustomTool");
                _ = icon;
            });
        }

        [Test]
        public void SamplePaths_ConstantsAreValid()
        {
            Assert.IsFalse(string.IsNullOrEmpty(BuildToolWelcomeWindow.BuildManifestSamplePath));
            Assert.IsFalse(string.IsNullOrEmpty(BuildToolWelcomeWindow.BuildInfoSamplePath));
            Assert.AreEqual(BuildToolWelcomeWindow.BuildManifestSamplePath, BuildToolWelcomeWindow.SamplePath);
        }

        [Test]
        public void ResolveSamplePath_ReturnsNonEmptyPath()
        {
            var manifestPath = BuildToolWelcomeWindow.ResolveSamplePath(BuildToolWelcomeWindow.BuildManifestSamplePath);
            Assert.IsFalse(string.IsNullOrEmpty(manifestPath));

            var infoPath = BuildToolWelcomeWindow.ResolveSamplePath(BuildToolWelcomeWindow.BuildInfoSamplePath);
            Assert.IsFalse(string.IsNullOrEmpty(infoPath));
        }

        [Test]
        public void BuildUI_BuildSamplesTab_CanBeConstructedWithoutThrowing()
        {
            var window = EditorWindow.CreateInstance<BuildToolWelcomeWindow>();
            Assert.DoesNotThrow(() => window.BuildUI());
            UnityEngine.Object.DestroyImmediate(window);
        }

        [Test]
        public void IsSampleImported_ReturnsBooleanWithoutThrowing()
        {
            var isManifestImported = BuildToolWelcomeWindow.IsSampleImported(
                BuildToolWelcomeWindow.BuildManifestSamplePath,
                "Build Manifest Extension"
            );
            Assert.IsNotNull(isManifestImported);

            var isInfoImported = BuildToolWelcomeWindow.IsSampleImported(
                BuildToolWelcomeWindow.BuildInfoSamplePath,
                "Build Info Display"
            );
            Assert.IsNotNull(isInfoImported);
        }

        [Test]
        public void AuthorConstants_AreNotEmpty()
        {
            Assert.IsFalse(string.IsNullOrEmpty(BuildToolWelcomeWindow.AuthorName));
            Assert.IsFalse(string.IsNullOrEmpty(BuildToolWelcomeWindow.AuthorEmail));
            Assert.IsFalse(string.IsNullOrEmpty(BuildToolWelcomeWindow.AuthorGitHubUrl));
            Assert.AreEqual("redlroses", BuildToolWelcomeWindow.AuthorName);
            Assert.AreEqual("redlroses.rlr@gmail.com", BuildToolWelcomeWindow.AuthorEmail);
            Assert.AreEqual("https://github.com/redlroses", BuildToolWelcomeWindow.AuthorGitHubUrl);
        }

        [Test]
        public void BuildUI_CanSwitchToAboutAndCreditsTab_WithoutThrowing()
        {
            var window = EditorWindow.CreateInstance<BuildToolWelcomeWindow>();
            Assert.DoesNotThrow(() =>
            {
                window.BuildUI();
                window.SwitchTab(BuildToolWelcomeWindow.WelcomeTab.AboutAndCredits);
            });
            UnityEngine.Object.DestroyImmediate(window);
        }
    }
}
