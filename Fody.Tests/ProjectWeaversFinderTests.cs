using System;
using Moq;
using NUnit.Framework;

[TestFixture]
public class ProjectWeaversFinderTests
{
    [Test]
    public void NotFound()
    {
        var loggerMock = new Mock<BuildLogger>();
        loggerMock.Setup(x => x.LogInfo(It.IsAny<string>()));
        var logger = loggerMock.Object;
        var processor = new Processor
                                       {
                                           ProjectPath = Environment.CurrentDirectory,
                                           Logger = logger,
                                           SolutionDir = Environment.CurrentDirectory
                                       };
        processor.FindProjectWeavers();
        Assert.IsEmpty(processor.ConfigFiles);
        loggerMock.Verify();
        
    }
}