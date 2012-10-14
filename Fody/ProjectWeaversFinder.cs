using System.Collections.Generic;
using System.IO;

public partial class Processor
{ 
    public List<string> ConfigFiles = new List<string>();

    public string SolutionConfigFilePath;


    public void FindProjectWeavers()
    {
        var fodyDirConfigFilePath = Path.Combine(AssemblyLocation.CurrentDirectory(), "FodyWeavers.xml");
        if (File.Exists(fodyDirConfigFilePath))
        {
            ConfigFiles.Add(fodyDirConfigFilePath);
            Logger.LogInfo(string.Format("Found path to weavers file '{0}'.", fodyDirConfigFilePath));
        }

        var solutionConfigFilePath = Path.Combine(SolutionDirectoryPath, "FodyWeavers.xml");
        if (File.Exists(solutionConfigFilePath))
        {
            ConfigFiles.Add(solutionConfigFilePath);
            Logger.LogInfo(string.Format("Found path to weavers file '{0}'.", solutionConfigFilePath));
        }

        var projectDirectory = Path.GetDirectoryName(ProjectFilePath);
        var projectConfigFilePath = Path.Combine(projectDirectory, "FodyWeavers.xml");
        if (File.Exists(projectConfigFilePath))
        {
            ConfigFiles.Add(projectConfigFilePath);
            Logger.LogInfo(string.Format("Found path to weavers file '{0}'.", projectConfigFilePath));
        }


        if (ConfigFiles.Count == 0)
        {
            var pathsSearched = string.Join("', '", fodyDirConfigFilePath, solutionConfigFilePath, projectConfigFilePath);
            Logger.LogInfo(string.Format("Could not find path to weavers file. Searched '{0}'.", pathsSearched));
        }
    }

}