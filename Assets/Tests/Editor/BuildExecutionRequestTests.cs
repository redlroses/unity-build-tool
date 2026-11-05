using NUnit.Framework;
using BuildTool.Editor.Pipeline;

namespace BuildTool.Editor.Tests
{
    [TestFixture]
    public class BuildExecutionRequestTests
    {
        [Test]
        public void ParseExtensionArgs_KeyValuePairs_ParsedCorrectly()
        {
            var req = new BuildExecutionRequest();
            req.ParseExtensionArgs("zip=all;addressables.clean=true;retry=3");

            Assert.AreEqual("all", req.GetExtensionArg("zip"));
            Assert.IsTrue(req.GetExtensionBool("addressables.clean"));
            Assert.AreEqual(3, req.GetExtensionInt("retry"));
        }

        [Test]
        public void ParseExtensionArgs_CommaSeparated_ParsedCorrectly()
        {
            var req = new BuildExecutionRequest();
            req.ParseExtensionArgs("zip=ProfileA,ProfileB");

            Assert.AreEqual("ProfileA,ProfileB", req.GetExtensionArg("zip"));
        }

        [Test]
        public void ParseExtensionArgs_MultiplePairsWithCommaLists_ParsedCorrectly()
        {
            var req = new BuildExecutionRequest();
            req.ParseExtensionArgs("zip=Win,Android;clean=true;zip.exclude=*DontShip*,D3D12");

            Assert.AreEqual("Win,Android", req.GetExtensionArg("zip"));
            Assert.IsTrue(req.GetExtensionBool("clean"));
            Assert.AreEqual("*DontShip*,D3D12", req.GetExtensionArg("zip.exclude"));
        }

        [Test]
        public void ParseExtensionArgs_FlagWithoutValue_DefaultsToTrue()
        {
            var req = new BuildExecutionRequest();
            req.ParseExtensionArgs("flagA;flagB");

            Assert.IsTrue(req.GetExtensionBool("flagA"));
            Assert.IsTrue(req.GetExtensionBool("flagB"));
        }

        [TestCase("true", true)]
        [TestCase("1", true)]
        [TestCase("yes", true)]
        [TestCase("false", false)]
        [TestCase("0", false)]
        [TestCase("no", false)]
        public void GetExtensionBool_Formats_EvaluatedCorrectly(string rawValue, bool expected)
        {
            var req = new BuildExecutionRequest();
            req.SetExtensionArg("testKey", rawValue);

            Assert.AreEqual(expected, req.GetExtensionBool("testKey"));
        }

        [Test]
        public void GetExtensionArg_MissingKey_ReturnsDefaultValue()
        {
            var req = new BuildExecutionRequest();

            Assert.AreEqual("defaultVal", req.GetExtensionArg("nonExistent", "defaultVal"));
            Assert.IsTrue(req.GetExtensionBool("nonExistent", true));
            Assert.AreEqual(42, req.GetExtensionInt("nonExistent", 42));
        }
    }
}
