using System.CommandLine;
using System.IO.Compression;
using ProxyObjects.Engine;
using ProxyObjects.Runtime;

namespace ProxyObjects;

internal static class Program
{
    public static int Main(string[] args)
    {
        var fileArgument = new Argument<string>(
            "file", 
            "The input module to obfuscate.");
        
        var modeOption = new Option<string?>(
            "--mode",
            "Determines the type of proxy objects that are created." 
            + " Available modes: empty, mimic (default), failfast, stackoverflow, statechanger");
        
        var embedOption = new Option<bool>(
            "--dynamic",
            "Embed all proxy types in an embedded runtime assembly that is dynamically resolved.");
        
        var annotateTypesOption = new Option<bool>(
            "--annotate-types",
            "Annotates all instantiatable type definitions in the module with a proxy attribute, minimizing " 
            + "the amount of CIL instructions and metadata to be injected into the method body (does not always work " 
            + "well in conjunction with --dynamic).");

        var rootCommand = new RootCommand
        {
            fileArgument,
            modeOption,
            embedOption,
            annotateTypesOption,
        };

        rootCommand.SetHandler(Run, fileArgument, modeOption, embedOption, annotateTypesOption);
        
        return rootCommand.Invoke(args);
    }

    private static void Run(string file, string? mode, bool dynamicEmbed, bool annotateTypes)
    {
        // Load target file.
        var module = ModuleDefinition.FromFile(file);

        // Apply obfuscation.
        if (dynamicEmbed)
            ApplyAsDynamicProxy(module, mode, annotateTypes);
        else
            ApplyAsStaticProxy(module, mode, annotateTypes);

        // Create output directory if it doesn't exist yet.
        string outputDirectory = Path.Combine(Path.GetDirectoryName(file)!, "Output");
        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);
       
        // Save.
        module.Write(Path.Combine(outputDirectory, Path.GetFileName(file)));
    }

    private static ProxyFactory CreateProxyFactory(string? mode, ModuleDefinition module) => mode?.ToLower() switch
    {
        "empty" => new EmptyProxyFactory(module),
        "mimic" or null => new MimicProxyFactory(module),
        "failfast" => new CrashProxyFactory(module),
        "stackoverflow" => new StackOverflowProxyFactory(module),
        "statechanger" => new StateChangerProxyFactory(module),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), $"Unknown mode '{mode}'.")
    };

    private static void ApplyAsStaticProxy(ModuleDefinition module, string? mode, bool annotateTypes)
    {
        ProxyObjectObfuscation.ApplyToModule(module, CreateProxyFactory(mode, module), annotateTypes);
    }

    private static void ApplyAsDynamicProxy(ModuleDefinition module, string? mode, bool annotateTypes)
    {
        // Create new proxy assembly.
        var corlibAssembly = module.CorLibTypeFactory.CorLibScope.GetAssembly() ?? KnownCorLibs.MsCorLib_v4_0_0_0;
        string embeddedAssemblyName = NameObfuscation.ApplyHomoglyphs(corlibAssembly.Name)!;
        
        var embeddedAssembly = new AssemblyDefinition(embeddedAssemblyName, corlibAssembly.Version);
        var embeddedModule = new ModuleDefinition($"{embeddedAssemblyName}.dll", 
            new AssemblyReference(module.CorLibTypeFactory.CorLibScope.GetAssembly()!));
        embeddedAssembly.Modules.Add(embeddedModule);
        
        // Obfuscate all methods in target module.
        var factory = CreateProxyFactory(mode, embeddedModule);
        ProxyObjectObfuscation.ApplyToModule(module, factory, annotateTypes);
        
        // Make final amendments.
        InjectAsResource(module, embeddedModule);
        InjectDynamicResolver(module, embeddedAssemblyName);
    }

    private static void InjectAsResource(ModuleDefinition inputModule, ModuleDefinition embeddedModule)
    {
        using var tempStream = new MemoryStream();
        embeddedModule.Write(tempStream);

        tempStream.Position = 0;
        using var compressedStream = new MemoryStream();
        using (var deflate = new DeflateStream(compressedStream, CompressionMode.Compress))
            tempStream.CopyTo(deflate);

        inputModule.Resources.Add(new ManifestResource(
            embeddedModule.Assembly!.Name,
            ManifestResourceAttributes.Public,
            new DataSegment(compressedStream.ToArray())));
    }

    private static void InjectDynamicResolver(ModuleDefinition module, string embeddedAssemblyName)
    {
        // Look up relevant types and methods.
        var runtimeModule = ModuleDefinition.FromFile(typeof(EmbeddedAssemblyResolver).Assembly.Location);
        var resolverType = (TypeDefinition) runtimeModule.LookupMember(typeof(EmbeddedAssemblyResolver).MetadataToken);
        var initMethod = resolverType.Methods.First(m => m.Name == nameof(EmbeddedAssemblyResolver.Initialize));
        
        // Clone.
        var result = new MemberCloner(module)
            .Include(resolverType.Methods)
            .Clone();

        // Add to <Module> type.
        var moduleType = module.GetOrCreateModuleType();
        foreach (var method in result.ClonedMembers.OfType<MethodDefinition>())
        {
            moduleType.Methods.Add(method);

            // Replace all occurrences of RUNTIME with the new runtime name.
            foreach (var instruction in method.CilMethodBody!.Instructions)
            {
                if (instruction.OpCode.Code == CilCode.Ldstr && instruction.Operand!.Equals("RUNTIME"))
                    instruction.Operand = embeddedAssemblyName;
            }
        }

        // Make sure the resolver is initialized upon module load.
        var cctor = moduleType.GetOrCreateStaticConstructor();
        cctor.CilMethodBody!.Instructions.Insert(0, CilOpCodes.Call, result.GetClonedMember(initMethod));
    }
}