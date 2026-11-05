using BuildTool.Tracking.Editor;
using NUnit.Framework;
using UnityEngine;

namespace BuildTool.Tracking.Editor.Tests
{
    public class BuildTrackingSettingsTests
    {
        [Test]
        public void DefaultBranchName_ReturnsBuildTracking()
        {
            var settings = ScriptableObject.CreateInstance<BuildTrackingSettings>();

            Assert.AreEqual("build-tracking", settings.DefaultBranchName);

            Object.DestroyImmediate(settings);
        }

        [Test]
        public void GetBranchName_WhenEmptyOverride_ReturnsDefaultBranchName()
        {
            var settings = ScriptableObject.CreateInstance<BuildTrackingSettings>();
            settings.ProjectBranchOverride = string.Empty;

            Assert.AreEqual("build-tracking", settings.GetBranchName());

            Object.DestroyImmediate(settings);
        }
    }
}
