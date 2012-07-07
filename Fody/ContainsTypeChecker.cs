using System;

public class ContainsTypeChecker
{
    static IContainsTypeChecker innerWeavingTask;

    static ContainsTypeChecker()
    {
        var appDomainSetup = new AppDomainSetup
        {
            ApplicationBase = AssemblyLocation.CurrentDirectory(),
        };
        var appDomain = AppDomain.CreateDomain("Fody.ContainsTypeChecker", null, appDomainSetup);
        var instanceAndUnwrap = appDomain.CreateInstanceAndUnwrap("FodyIsolated", "IsolatedContainsTypeChecker");
        innerWeavingTask = (IContainsTypeChecker)instanceAndUnwrap;
    }


    //TODO: possibly cache based on file stamp to avoid cros domain call. need to profile.
    public virtual bool Check(string assemblyPath, string typeName)
    {
        return innerWeavingTask.Check(assemblyPath, typeName);
    }

}