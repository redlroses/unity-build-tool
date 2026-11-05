using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BuildTool.Editor.Pipeline;
using BuildTool.Editor.Extensions;
using BuildTool.Editor.Processors;
using NUnit.Framework;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildTool.Editor.Tests
{
    [TestFixture]
    public class ArchiverTests
    {
        private string _tempDirPath;

        [SetUp]
        public void SetUp()
        {
            _tempDirPath = Path.Combine(Path.GetTempPath(), "BuildTool_ArchiverTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirPath);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDirPath))
                {
                    Directory.Delete(_tempDirPath, true);
                }

                var zipPath = $"{_tempDirPath}.zip";
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
            }
            catch
            {
            }
        }

        [Test]
        public void ShouldExclude_WildcardPatterns_EvaluatedCorrectly()
        {
            var patterns = new[] { "*DontShip*", "D3D12", "Prefix*", "*Suffix" };

            Assert.IsTrue(Archiver.ShouldExclude("Project_DontShip_Data", patterns));
            Assert.IsTrue(Archiver.ShouldExclude("D3D12", patterns));
            Assert.IsTrue(Archiver.ShouldExclude("d3d12", patterns), "Should be case-insensitive");
            Assert.IsTrue(Archiver.ShouldExclude("Prefix_Something", patterns));
            Assert.IsTrue(Archiver.ShouldExclude("Something_Suffix", patterns));

            Assert.IsFalse(Archiver.ShouldExclude("NormalFolder", patterns));
            Assert.IsFalse(Archiver.ShouldExclude("ShipThis", patterns));
            Assert.IsFalse(Archiver.ShouldExclude("Something_Prefix", patterns));
        }

        [Test]
        public void Initialize_WithZipFalse_DisablesArchiver()
        {
            var archiver = new Archiver();
            var req = new BuildExecutionRequest();
            req.ExtensionArgs["zip"] = "false";

            archiver.Initialize(new BuildContext(req));

            Assert.IsFalse(archiver.IsEnabled);
        }

        [Test]
        public void Initialize_WithZipAll_EnablesArchiverForAllProfiles()
        {
            var archiver = new Archiver();
            var req = new BuildExecutionRequest();
            req.ExtensionArgs["zip"] = "all";

            archiver.Initialize(new BuildContext(req));

            Assert.IsTrue(archiver.IsEnabled);
            Assert.IsTrue(archiver.IsAll);
        }

        [Test]
        public void Initialize_WithSpecificProfiles_ConfiguresProfiles()
        {
            var archiver = new Archiver();
            var req = new BuildExecutionRequest();
            req.ExtensionArgs["zip"] = "ProfileA, ProfileB";

            archiver.Initialize(new BuildContext(req));

            Assert.IsTrue(archiver.IsEnabled);
            Assert.IsFalse(archiver.IsAll);
            Assert.IsTrue(archiver.IsProfileConfigured("ProfileA"));
            Assert.IsTrue(archiver.IsProfileConfigured("ProfileB"));
            Assert.IsFalse(archiver.IsProfileConfigured("ProfileC"));
        }

        [Test]
        public void Initialize_WithCustomExclusions_UpdatesExcludePatterns()
        {
            var archiver = new Archiver();
            var req = new BuildExecutionRequest();
            req.ExtensionArgs["zip.exclude"] = "*TempCache*, ExtraDebug";

            archiver.Initialize(new BuildContext(req));

            Assert.AreEqual("*TempCache*, ExtraDebug", archiver.ExcludePatterns);
        }

        [Test]
        public void GetParameters_ReturnsZipAndZipExclude()
        {
            var archiver = new Archiver();
            var parameters = archiver.GetParameters().ToList();

            Assert.IsNotNull(parameters);
            Assert.IsTrue(parameters.Any(p => p.Key == "zip"));
            Assert.IsTrue(parameters.Any(p => p.Key == "zip.exclude"));
        }

        [Test]
        public void PostProcess_CompressesFolder_AndSkipsExcludedFolders()
        {
            var archiver = new Archiver();
            var req = new BuildExecutionRequest();
            req.ExtensionArgs["zip"] = "all";
            req.ExtensionArgs["zip.exclude"] = "*DontShip*";
            archiver.Initialize(new BuildContext(req));

            var keepFolder = Path.Combine(_tempDirPath, "KeepFolder");
            var excludeFolder = Path.Combine(_tempDirPath, "Temp_DontShip_Folder");
            Directory.CreateDirectory(keepFolder);
            Directory.CreateDirectory(excludeFolder);

            File.WriteAllText(Path.Combine(keepFolder, "data.txt"), "keep this content");
            File.WriteAllText(Path.Combine(excludeFolder, "dummy.txt"), "delete this content");
            File.WriteAllText(Path.Combine(_tempDirPath, "game.exe"), "executable binary");

            var profile = ScriptableObject.CreateInstance<BuildProfile>();
            profile.name = "TestProfile";

            archiver.PostProcess(profile, _tempDirPath);

            Assert.IsTrue(Directory.Exists(excludeFolder), "Excluded folder should remain on disk");
            Assert.IsTrue(Directory.Exists(keepFolder), "Non-excluded folder should still exist");

            var expectedZipPath = $"{_tempDirPath}.zip";
            Assert.IsTrue(File.Exists(expectedZipPath), $"Zip file should exist at: {expectedZipPath}");

            using var archive = ZipFile.OpenRead(expectedZipPath);
            var entryNames = archive.Entries.Select(e => e.FullName).ToList();

            Assert.IsTrue(entryNames.Any(e => e.Contains("game.exe")), "Zip should contain game.exe");
            Assert.IsTrue(entryNames.Any(e => e.Contains("KeepFolder")), "Zip should contain KeepFolder");
            Assert.IsFalse(
                entryNames.Any(e => e.Contains("Temp_DontShip_Folder")),
                "Zip should NOT contain excluded folder");
        }

        private class TestPreExtension : BuildExtensionBase, IBuildPreprocessor
        {
            public override string Title => "Test Pre";
            public void PreProcess(BuildProfile profile) { }
            public override VisualElement CreateBodyGUI(IEnumerable<BuildProfile> profiles) => null;
        }

        private class TestDualExtension : BuildExtensionBase, IBuildPreprocessor, IBuildPostprocessor
        {
            public override string Title => "Test Dual";
            public void PreProcess(BuildProfile profile) { }
            public void PostProcess(BuildProfile profile, string path) { }
            public override VisualElement CreateBodyGUI(IEnumerable<BuildProfile> profiles) => null;
        }

        private class TestNoneExtension : BuildExtensionBase
        {
            public override string Title => "Test None";
            public override VisualElement CreateBodyGUI(IEnumerable<BuildProfile> profiles) => null;
        }

        [Test]
        public void CreateGUI_IncludesPostProcessBadge()
        {
            var archiver = new Archiver();
            var gui = archiver.CreateGUI(Array.Empty<BuildProfile>());
            Assert.IsNotNull(gui);

            var postBadge = gui.Q<Label>(className: "build-extension-badge-post");
            Assert.IsNotNull(postBadge);
            Assert.AreEqual("Post-Process", postBadge.text);

            var preBadge = gui.Q<Label>(className: "build-extension-badge-pre");
            Assert.IsNull(preBadge);
        }

        [Test]
        public void CreateGUI_PreprocessorExtension_IncludesPreBadgeOnly()
        {
            var ext = new TestPreExtension();
            var gui = ext.CreateGUI(Array.Empty<BuildProfile>());
            Assert.IsNotNull(gui.Q<Label>(className: "build-extension-badge-pre"));
            Assert.IsNull(gui.Q<Label>(className: "build-extension-badge-post"));
        }

        [Test]
        public void CreateGUI_DualExtension_IncludesBothBadges()
        {
            var ext = new TestDualExtension();
            var gui = ext.CreateGUI(Array.Empty<BuildProfile>());
            Assert.IsNotNull(gui.Q<Label>(className: "build-extension-badge-pre"));
            Assert.IsNotNull(gui.Q<Label>(className: "build-extension-badge-post"));
        }

        [Test]
        public void CreateGUI_PlainExtension_HasNoBadges()
        {
            var ext = new TestNoneExtension();
            var gui = ext.CreateGUI(Array.Empty<BuildProfile>());
            Assert.IsNull(gui.Q<VisualElement>(className: "build-extension-badges"));
        }
    }
}
