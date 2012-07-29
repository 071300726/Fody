using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Fody
{

    public class WeavingTask : Task
    {
        public string AddinSearchPaths { get; set; }

        [Required]
        public string AssemblyPath { set; get; }

        public string IntermediateDir { get; set; }
        public string KeyFilePath { get; set; }
        public string MessageImportance { set; get; }

        [Required]
        public string ProjectPath { get; set; }

        [Required]
        public string References { get; set; }

        [Required]
        public string SolutionDir { get; set; }

        BuildLogger logger;
        static object locker;

        static AppDomain appDomain;
        ContainsTypeChecker containsTypeChecker;

        static WeavingTask()
        {
            locker = new object();
            DomainAssemblyResolver.Connect();
        }

        public WeavingTask()
        {
            MessageImportance = "Low";
        }

        public override bool Execute()
        {
            BuildEngine.LogMessageEvent(new BuildMessageEventArgs(string.Format("Fody (version {0}) Executing", GetType().Assembly.GetName().Version), "", "Fody", Microsoft.Build.Framework.MessageImportance.High));

            var stopwatch = Stopwatch.StartNew();

            logger = new BuildLogger
                {
                    BuildEngine = BuildEngine,
                };
            logger.Initialise(MessageImportance);

            try
            {
                Inner();
                return !logger.ErrorOccurred;
            }
            catch (Exception exception)
            {
                logger.LogError(exception.ToFriendlyString());
                return false;
            }
            finally
            {
                stopwatch.Stop();
                logger.Flush();
                BuildEngine.LogMessageEvent(new BuildMessageEventArgs(string.Format("\tFinished Fody {0}ms.", stopwatch.ElapsedMilliseconds), "", "Fody", Microsoft.Build.Framework.MessageImportance.High));
            }
        }

        void Inner()
        {
            var projectPathFinder = new ProjectPathValidator
                {
                    Logger = logger,
                    ProjectPath = ProjectPath
                };
            projectPathFinder.Execute();

            var assemblyPathValidator = new AssemblyPathValidator
                {
                    Logger = logger,
                    AssemblyPath = AssemblyPath
                };
            assemblyPathValidator.Execute();
            var projectWeaversFinder = new ProjectWeaversFinder
                {
                    ProjectFilePath = ProjectPath,
                    Logger = logger,
                    SolutionDir = SolutionDir
                };
            projectWeaversFinder.Execute();
            var weaversXmlHistory = new WeaversXmlHistory
                {
                    Logger = logger,
                    ProjectWeaversFinder = projectWeaversFinder
                };

            containsTypeChecker = new ContainsTypeChecker();
            var fileChangedChecker = new FileChangedChecker
                {
                    ContainsTypeChecker = containsTypeChecker,
                    Logger = logger,
                    AssemblyPath = AssemblyPath
                };
            if (!fileChangedChecker.ShouldStart())
            {
                var weaversXmlChanged = weaversXmlHistory.CheckForChanged();
                if (!weaversXmlChanged)
                {

                    var innerProjectWeaversReader = new ProjectWeaversReader
                        {
                            ProjectWeaversFinder = projectWeaversFinder
                        };
                    innerProjectWeaversReader.Execute();
                    FindWeavers(innerProjectWeaversReader);
                    if (WeaversHistory.HasChanged(innerProjectWeaversReader.Weavers.Select(x => x.AssemblyPath)))
                    {
                        logger.LogWarning("A re-build is required to because a weaver changed");
                    }
                }
                return;
            }

            var solutionPathValidator = new SolutionPathValidator
                {
                    Logger = logger,
                    SolutionDir = SolutionDir
                };
            solutionPathValidator.Execute();

            var projectWeaversReader = new ProjectWeaversReader
                {
                    ProjectWeaversFinder = projectWeaversFinder
                };
            projectWeaversReader.Execute();

            FindWeavers(projectWeaversReader);

            if (projectWeaversReader.Weavers.Count == 0)
            {
                logger.LogWarning(string.Format("Could not find any weavers. Either add a project named 'Weavers' with a type named 'ModuleWeaver' or add some items to '{0}'.", ProjectWeaversFinder.FodyWeaversXml));
                return;
            }

            lock (locker)
            {
                ExecuteInOwnAppDomain(projectWeaversReader);
            }
            weaversXmlHistory.Flush();
        }

        void FindWeavers(ProjectWeaversReader projectWeaversReader)
        {
            var addinDirectoriesFinder = new AddinDirectoriesFinder
                {
                    Logger = logger, 
                    SolutionDir = SolutionDir,
                    AddinSearchPaths = AddinSearchPaths
                };
            var addinDirectories = addinDirectoriesFinder.FindAddinDirectories();

            var weaverProjectFileFinder = new WeaverProjectFileFinder
                {
                    Logger = logger,
                    SolutionDir = SolutionDir
                };
            weaverProjectFileFinder.Execute();


            var addinFilesEnumerator = new AddinFilesEnumerator
                {
                    AddinDirectories = addinDirectories
                };
            var weaverProjectContainsWeaverChecker = new WeaverProjectContainsWeaverChecker
                {
                    ContainsTypeChecker = containsTypeChecker,
                    WeaverProjectFileFinder = weaverProjectFileFinder
                };
            var weaverAssemblyPathFinder = new WeaverAssemblyPathFinder
                {
                    ContainsTypeChecker = containsTypeChecker,
                    AddinFilesEnumerator = addinFilesEnumerator,
                };
            var weaversConfiguredInstanceLinker = new WeaversConfiguredInstanceLinker
                {
                    WeaverProjectFileFinder = weaverProjectFileFinder,
                    WeaverAssemblyPathFinder = weaverAssemblyPathFinder,
                    ProjectWeaversReader = projectWeaversReader,
                    WeaverProjectContainsWeaverChecker = weaverProjectContainsWeaverChecker,
                };
            weaversConfiguredInstanceLinker.Execute();

            var noWeaversConfiguredInstanceLinker = new NoWeaversConfiguredInstanceLinker
                {
                    Logger = logger,
                    WeaverProjectFileFinder = weaverProjectFileFinder,
                    WeaverProjectContainsWeaverChecker = weaverProjectContainsWeaverChecker,
                    ProjectWeaversReader = projectWeaversReader,
                };
            noWeaversConfiguredInstanceLinker.Execute();
        }


        void ExecuteInOwnAppDomain(ProjectWeaversReader projectWeaversReader)
        {
            if (WeaversHistory.HasChanged(projectWeaversReader.Weavers.Select(x => x.AssemblyPath)) || appDomain == null)
            {
                if (appDomain != null)
                {
                    AppDomain.Unload(appDomain);
                }

                var appDomainSetup = new AppDomainSetup
                    {
                        ApplicationBase = AssemblyLocation.CurrentDirectory(),
                    };
                appDomain = AppDomain.CreateDomain("Fody", null, appDomainSetup);
            }
            var innerWeavingTask = (IInnerWeaver) appDomain.CreateInstanceAndUnwrap("FodyIsolated", "InnerWeaver");
            innerWeavingTask.AssemblyPath = AssemblyPath;
            innerWeavingTask.References = References;
            innerWeavingTask.KeyFilePath = KeyFilePath;
            innerWeavingTask.Logger = logger;
            innerWeavingTask.AssemblyPath = AssemblyPath;
            innerWeavingTask.Weavers = projectWeaversReader.Weavers;
            innerWeavingTask.IntermediateDir = IntermediateDir;

            innerWeavingTask.Execute();
        }
    }
}

