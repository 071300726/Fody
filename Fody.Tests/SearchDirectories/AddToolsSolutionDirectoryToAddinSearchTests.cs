using System.IO;
using Moq;
using NUnit.Framework;

[TestFixture]
public class AddToolsSolutionDirectoryToAddinSearchTests
{
    [Test]
    public void Simple()
    {
        var logger = new Mock<BuildLogger>().Object;
        var processor = new AddinFinder
            {
                Logger = logger,
                SolutionDirectoryPath = "Solution"
            };
        processor.AddToolsSolutionDirectoryToAddinSearch();
        var searchPaths = processor.AddinSearchPaths;
        Assert.AreEqual(Path.GetFullPath(@"Solution\Tools"), searchPaths[0]);
    }
}