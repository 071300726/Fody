using System;
using System.IO;
using Moq;
using NUnit.Framework;
#if(DEBUG)

[TestFixture]
public class NugetPackagePathFinderTest
{
    [Test]
    public void NoNugetConfig()
    {
        var processor = new Processor
            {
                SolutionDir = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "../../NugetPackagePathFinder/FakeSolution")),
                Logger = new Mock<BuildLogger>().Object
            };

        processor.FindNugetPackagePath();
        Assert.IsTrue(processor.PackagesPath.EndsWith("\\FakeSolution\\Packages"));
    }

    
}
#endif