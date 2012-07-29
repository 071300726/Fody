using System.IO;

public class AssemblyPathValidator
{
    public string AssemblyPath;
    public BuildLogger Logger;

    public void Execute()
    {
        if (!File.Exists(AssemblyPath))
        {
            throw new WeavingException(string.Format("AssemblyPath \"{0}\" does not exists. If you have not done a build you can ignore this error.", AssemblyPath));
        }

        Logger.LogInfo(string.Format("AssemblyPath: {0}", AssemblyPath));
    }

}