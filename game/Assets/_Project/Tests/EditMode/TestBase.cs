using NUnit.Framework;
using UnityEngine.TestTools;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Base for all pure-logic fixtures. The embedded mcp-unity editor package throws NREs /
    /// LogErrors on the editor update loop during headless (batch) test runs when its MCP server
    /// isn't connected; the Unity Test Framework would otherwise fail any test that merely
    /// observes those unrelated logs. These unit tests never assert on logs, so ignore failing
    /// messages and judge each test purely by its own assertions.
    /// </summary>
    public abstract class TestBase
    {
        [SetUp]
        public void IgnoreUnrelatedEditorLogNoise() => LogAssert.ignoreFailingMessages = true;
    }
}
