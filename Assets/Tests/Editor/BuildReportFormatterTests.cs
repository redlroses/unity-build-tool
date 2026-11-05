using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BuildTool.Editor.Tests
{
    [TestFixture]
    public class BuildReportFormatterTests
    {
        [Test]
        public void GetSafeProfileName_NullProfileAndReport_ReturnsProfile()
        {
            var name = BuildReportFormatter.GetSafeProfileName(null, null);
            Assert.AreEqual("Profile", name);
        }

        [Test]
        public void GetSafeProfileName_WithValidProfile_ReturnsProfileName()
        {
            var profile = ScriptableObject.CreateInstance<BuildProfile>();
            profile.name = "MyUniqueProfile";

            try
            {
                var name = BuildReportFormatter.GetSafeProfileName(profile, null);
                Assert.AreEqual("MyUniqueProfile", name);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void FormatSingle_NullReport_ReturnsInformativeString()
        {
            var text = BuildReportFormatter.FormatSingle(null, null);
            Assert.AreEqual("Build report is null.", text);
        }

        [Test]
        public void FormatSummaryTable_EmptyList_ReturnsNoReportsMessage()
        {
            var text = BuildReportFormatter.FormatSummaryTable(new List<(BuildProfile Profile, BuildReport Report)>());
            Assert.AreEqual("No build reports available.", text);
        }

        [Test]
        public void SaveProfileReportToFile_NullReport_ReturnsNullWithoutThrowing()
        {
            Assert.DoesNotThrow(() =>
            {
                var path = BuildReportFormatter.SaveProfileReportToFile(null, null, "Build/TestPath");
                Assert.IsNull(path);
            });
        }
    }
}

