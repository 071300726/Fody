using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Mdb;
using Mono.Cecil.Pdb;
using Mono.Cecil.Rocks;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

public partial class InnerWeaver : MarshalByRefObject, IInnerWeaver
{
    public string ProjectDirectoryPath { get; set; }
    public string AssemblyFilePath { get; set; }
    public string SolutionDirectoryPath { get; set; }
    public string References { get; set; }
    public List<WeaverEntry> Weavers { get; set; }
    public string KeyFilePath { get; set; }
    public bool SignAssembly { get; set; }
    public ILogger Logger { get; set; }
    public string IntermediateDirectoryPath { get; set; }
    public List<string> ReferenceCopyLocalPaths { get; set; }
    public List<string> DefineConstants { get; set; }
    bool cancelRequested;
    List<WeaverHolder> weaverInstances = new List<WeaverHolder>();
    Action cancelDelegate;

    Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
    {
        var assemblyName = new AssemblyName(args.Name).Name;
        if (assemblyName == "Mono.Cecil")
        {
            return typeof(ModuleDefinition).Assembly;
        }
        if (assemblyName == "Mono.Cecil.Rocks")
        {
            return typeof(MethodBodyRocks).Assembly;
        }
        if (assemblyName == "Mono.Cecil.Pdb")
        {
            return typeof(PdbReader).Assembly;
        }
        if (assemblyName == "Mono.Cecil.Mdb")
        {
            return typeof(MdbReader).Assembly;
        }
        foreach (var weaverPath in Weavers.Select(x => x.AssemblyPath))
        {
            var directoryName = Path.GetDirectoryName(weaverPath);
            var assemblyFileName = assemblyName + ".dll";
            var assemblyPath = Path.Combine(directoryName, assemblyFileName);
            if (!File.Exists(assemblyPath))
            {
                continue;
            }
            try
            {
                return LoadFromFile(assemblyPath);
            }
            catch (Exception exception)
            {
                var message = $"Failed to load '{assemblyPath}'. Going to swallow and continue to let other AssemblyResolve events to attempt to resolve. Exception:{exception}";
                Logger.LogWarning(message);
            }
        }
        return null;
    }

    public void Execute()
    {
        ResolveEventHandler assemblyResolve = CurrentDomain_AssemblyResolve;
        try
        {
            SplitUpReferences();
            GetSymbolProviders();
            ReadModule();
            AppDomain.CurrentDomain.AssemblyResolve += assemblyResolve;
            InitialiseWeavers();
            ExecuteWeavers();
            AddWeavingInfo();
            AddProcessedFlag();
            FindStrongNameKey();
            WriteModule();
            ExecuteAfterWeavers();
            DisposeWeavers();

            if (weaverInstances
                .Any(_ => _.WeaverDelegate.AfterWeavingExecute != null))
            {
                ReadModule();
                WriteModule();
            }
        }
        catch (Exception exception)
        {
            AppDomain.CurrentDomain.AssemblyResolve -= assemblyResolve;
            Logger.LogException(exception);
        }

    }

    public void Cancel()
    {
        cancelRequested = true;
        var action = cancelDelegate;
        action?.Invoke();
    }

    void AddProcessedFlag()
    {
        ModuleDefinition.Types.Add(new TypeDefinition(null, "ProcessedByFody", TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Interface));
    }

    void InitialiseWeavers()
    {
        foreach (var weaverConfig in Weavers)
        {
            if (cancelRequested)
            {
                return;
            }
            Logger.LogDebug($"Weaver '{weaverConfig.AssemblyPath}'.");
            Logger.LogDebug("  Initializing weaver");
            var assembly = LoadAssembly(weaverConfig.AssemblyPath);

            var weaverType = assembly.FindType(weaverConfig.TypeName);

            var delegateHolder = weaverType.GetDelegateHolderFromCache();
            var weaverInstance = delegateHolder.ConstructInstance();
            var weaverHolder = new WeaverHolder
                               {
                                   Instance = weaverInstance,
                                   WeaverDelegate = delegateHolder,
                                   Config = weaverConfig
                               };
            weaverInstances.Add(weaverHolder);

            SetProperties(weaverConfig, weaverInstance, delegateHolder);
        }
    }

    void ExecuteWeavers()
    {
        foreach (var weaver in weaverInstances)
        {
            if (cancelRequested)
            {
                return;
            }
            try
            {
                if (weaver.WeaverDelegate.Cancel != null)
                {
                    cancelDelegate = () => weaver.WeaverDelegate.Cancel(weaver.Instance);
                }
                Logger.SetCurrentWeaverName(weaver.Config.AssemblyName);
                var startNew = Stopwatch.StartNew();
                Logger.LogDebug("  Executing Weaver ");
                weaver.WeaverDelegate.Execute(weaver.Instance);
                var finishedMessage = $"  Finished '{weaver.Config.AssemblyName}' in {startNew.ElapsedMilliseconds}ms {Environment.NewLine}";
                Logger.LogDebug(finishedMessage);
            }
            finally
            {
                cancelDelegate = null;
                Logger.ClearWeaverName();
            }
        }
    }

    void AddWeavingInfo()
    {
        var typeDefinition = new TypeDefinition(ModuleDefinition.Name.Replace(".dll", string.Empty), "FodyWeavingResults",
            TypeAttributes.Class | TypeAttributes.NotPublic);

        typeDefinition.CustomAttributes.Add(new CustomAttribute(ModuleDefinition.ImportReference(typeof(DebuggerHiddenAttribute).GetConstructor(Type.EmptyTypes))));

        foreach (var weaver in weaverInstances)
        {
            var fd = new FieldDefinition(weaver.Config.AssemblyName, FieldAttributes.Assembly | FieldAttributes.Literal | FieldAttributes.HasDefault, ModuleDefinition.ImportReference(typeof(string)));
            fd.Constant ="1.10.0";

            typeDefinition.Fields.Add(fd);
        }

        ModuleDefinition.Types.Add(typeDefinition);
    }

    void ExecuteAfterWeavers()
    {
        foreach (var weaver in weaverInstances
            .Where(_ => _.WeaverDelegate.AfterWeavingExecute != null))
        {
            if (cancelRequested)
            {
                return;
            }
            try
            {
                Logger.SetCurrentWeaverName(weaver.Config.AssemblyName);
                var startNew = Stopwatch.StartNew();
                Logger.LogDebug("  Executing After Weaver");
                weaver.WeaverDelegate.AfterWeavingExecute(weaver.Instance);
                var finishedMessage = $"  Finished '{weaver.Config.AssemblyName}' in {startNew.ElapsedMilliseconds}ms {Environment.NewLine}";
                Logger.LogDebug(finishedMessage);
            }
            finally
            {
                Logger.ClearWeaverName();
            }
        }
    }

    void DisposeWeavers()
    {
        foreach (var disposable in weaverInstances
            .Select(x => x.Instance)
            .OfType<IDisposable>())
        {
            disposable.Dispose();
        }
    }


    public sealed override object InitializeLifetimeService()
    {
        // Returning null designates an infinite non-expiring lease.
        // We must therefore ensure that RemotingServices.Disconnect() is called when
        // it's no longer needed otherwise there will be a memory leak.
        return null;
    }

    public void Dispose()
    {
        //Disconnects the remoting channel(s) of this object and all nested objects.
        RemotingServices.Disconnect(this);
    }

}
