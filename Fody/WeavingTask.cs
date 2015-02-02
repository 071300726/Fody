using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Fody
{

    public class WeavingTask : Task
    {
        [Required]
        public string AssemblyPath { set; get; }

        [Required]
        public string IntermediateDir { get; set; }

        public string KeyFilePath { get; set; }
        public bool SignAssembly { get; set; }

        [Required]
        public string ProjectDirectory { get; set; }

        [Required]
        public string References { get; set; }

        [Required]
        public ITaskItem[] ReferenceCopyLocalPaths { get; set; }

        [Required]
        public string SolutionDir { get; set; }

        [Required]
        public string DefineConstants { get; set; }

        public override bool Execute()
        {
            var referenceCopyLocalPaths = ReferenceCopyLocalPaths.Select(x => x.ItemSpec).ToList();
            var defineConstants = DefineConstants.Split(';').ToList();
            return new Processor
                   {
                       Logger = new BuildLogger
                                {
                                    BuildEngine = BuildEngine,
                                },
                       AssemblyFilePath = AssemblyPath,
                       IntermediateDirectory = IntermediateDir,
                       KeyFilePath = KeyFilePath,
                       SignAssembly = SignAssembly,
                       ProjectDirectory = ProjectDirectory,
                       References = References,
                       SolutionDirectory = SolutionDir,
                       ReferenceCopyLocalPaths = referenceCopyLocalPaths,
                       DefineConstants = defineConstants
                   }.Execute();
        }
    }
}