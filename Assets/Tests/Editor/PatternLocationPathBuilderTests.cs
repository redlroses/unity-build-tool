using System;
using NUnit.Framework;
using BuildTool.Editor.DirectoryBuilders;
using UnityEngine;

namespace BuildTool.Editor.Tests
{
    [TestFixture]
    public class PatternLocationPathBuilderTests
    {
        [Test]
        public void DefaultPattern_IsNotEmpty()
        {
            var builder = new PatternLocationPathBuilder();
            Assert.IsFalse(string.IsNullOrWhiteSpace(builder.Pattern));
            Assert.AreEqual(PatternLocationPathBuilder.DefaultPattern, builder.Pattern);
        }

        [Test]
        public void Pattern_WhenSetToNull_FallsBackToDefault()
        {
            var builder = new PatternLocationPathBuilder { Pattern = null };
            Assert.AreEqual(PatternLocationPathBuilder.DefaultPattern, builder.Pattern);
        }

        [Test]
        public void GetLocationPathName_NullProfile_DoesNotThrow()
        {
            var builder = new PatternLocationPathBuilder
            {
                Pattern = "{ProfileName}_{Version}/{ProductName}"
            };

            Assert.DoesNotThrow(() =>
            {
                var path = builder.GetLocationPathName(null, "Build/TestOut");
                Assert.IsFalse(string.IsNullOrEmpty(path));
                Assert.IsTrue(path.StartsWith("Build/TestOut", StringComparison.OrdinalIgnoreCase));
                Assert.IsTrue(path.Contains("Profile_"));
            });
        }

        [Test]
        public void BuildTool_GetRootBuildPath_FormatsCorrectly()
        {
            var date = new DateTime(2026, 10, 26, 15, 30, 0);
            var path = BuildTool.GetRootBuildPath("42", date);

            Assert.IsFalse(string.IsNullOrWhiteSpace(path));
            Assert.IsTrue(path.Contains("42"), "Expected path to contain build index 42");
            Assert.IsTrue(path.Contains("2026-10-26"), "Expected path to contain date 2026-10-26");
        }

        [Test]
        public void CustomToken_RegisteredAndResolved_Successfully()
        {
            const string tokenName = "CustomChannel";
            PatternLocationPathBuilder.RegisterToken(tokenName, ctx => "Nightly", "Nightly release stream");

            try
            {
                var builder = new PatternLocationPathBuilder
                {
                    UseCustomPattern = true,
                    Pattern = "{CustomChannel}/{ProductName}"
                };

                var path = builder.GetLocationPathName(null, "Build/Output");
                Assert.IsTrue(path.Contains("Nightly"), "Expected path to contain resolved custom token 'Nightly'");
            }
            finally
            {
                PatternLocationPathBuilder.UnregisterToken(tokenName);
            }
        }

        [Test]
        public void UseCustomPattern_Toggle_ControlsEffectivePattern()
        {
            var builder = new PatternLocationPathBuilder
            {
                RawPattern = "MyCustomFolder/{ProductName}",
                UseCustomPattern = false
            };

            Assert.AreEqual(PatternLocationPathBuilder.DefaultPattern, builder.Pattern);

            builder.UseCustomPattern = true;
            Assert.AreEqual("MyCustomFolder/{ProductName}", builder.Pattern);
        }

        [Test]
        public void CustomToken_WithParameter_PassesParameterToResolver()
        {
            const string tokenName = "ConfigValue";
            PatternLocationPathBuilder.RegisterToken(
                tokenName, ctx => $"Config_{ctx.Parameter}", "Test parameterized token");

            try
            {
                var builder = new PatternLocationPathBuilder
                {
                    UseCustomPattern = true,
                    Pattern = "{ConfigValue:ReleaseCandidate}/{ProductName}"
                };

                var path = builder.GetLocationPathName(null, "Build/Output");
                Assert.IsTrue(path.Contains("Config_ReleaseCandidate"), "Expected parameterized token to be resolved");
            }
            finally
            {
                PatternLocationPathBuilder.UnregisterToken(tokenName);
            }
        }

        [Test]
        public void CustomToken_WhenThrows_RetainsTokenGracefully()
        {
            const string tokenName = "FailingToken";
            PatternLocationPathBuilder.RegisterToken(
                tokenName, ctx => throw new InvalidOperationException("Boom"), "Failing token");

            try
            {
                var builder = new PatternLocationPathBuilder
                {
                    UseCustomPattern = true,
                    Pattern = "{FailingToken}/{ProductName}"
                };

                Assert.DoesNotThrow(() =>
                {
                    var path = builder.GetLocationPathName(null, "Build/Output");
                    Assert.IsTrue(path.Contains("{FailingToken}"));
                });
            }
            finally
            {
                PatternLocationPathBuilder.UnregisterToken(tokenName);
            }
        }

        [Test]
        public void UnknownToken_PreservedInOutput()
        {
            var builder = new PatternLocationPathBuilder
            {
                UseCustomPattern = true,
                Pattern = "{UnknownTokenXYZ}/{ProductName}"
            };

            var path = builder.GetLocationPathName(null, "Build/Output");
            Assert.IsTrue(path.Contains("{UnknownTokenXYZ}"));
        }

        [Test]
        public void GetAvailableTokens_ContainsDefaultAndCustomTokens()
        {
            const string customToken = "TempToken123";
            PatternLocationPathBuilder.RegisterToken(customToken, ctx => "val", "Temporary token description");

            try
            {
                var tokens = PatternLocationPathBuilder.GetAvailableTokens();
                Assert.IsTrue(tokens.ContainsKey("ProfileName"));
                Assert.IsTrue(tokens.ContainsKey("ProductName"));
                Assert.IsTrue(tokens.ContainsKey("Version"));
                Assert.IsTrue(tokens.ContainsKey(customToken));
                Assert.AreEqual("Temporary token description", tokens[customToken]);
            }
            finally
            {
                PatternLocationPathBuilder.UnregisterToken(customToken);
            }
        }
    }
}
