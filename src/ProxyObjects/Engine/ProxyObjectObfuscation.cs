using System.Diagnostics;

namespace ProxyObjects.Engine;

/// <summary>
/// The main interface for applying proxy object obfuscation to a .NET module.
/// </summary>
public class ProxyObjectObfuscation
{
    private readonly ModuleDefinition _module;
    private readonly MemberReference _typeProxyConstructor;
    private readonly TypeSignature _typeType;
    private readonly ProxyFactory _proxyFactory;
    private readonly bool _annotateTypes;

    private ProxyObjectObfuscation(ModuleDefinition module, ProxyFactory proxyFactory, bool annotateTypes)
    {
        _module = module;
        _proxyFactory = proxyFactory;
        _annotateTypes = annotateTypes;

        var factory = module.CorLibTypeFactory;

        _typeType = factory.CorLibScope.CreateTypeReference("System", "Type").ToTypeSignature();
        _typeProxyConstructor = factory.CorLibScope
            .CreateTypeReference("System.Diagnostics", "DebuggerTypeProxyAttribute")
            .CreateMemberReference(".ctor", MethodSignature.CreateInstance(factory.Void, _typeType))
            .ImportWith(module.DefaultImporter);
    }

    /// <summary>
    /// Replaces the local variables in all method bodies with their proxy type counterpart where possible.
    /// </summary>
    /// <param name="module">The module to apply the obfuscation to.</param>
    /// <param name="factory">The proxy type factory to use.</param>
    /// <param name="annotateTypes">
    /// <c>true</c> if existing types should be annotated with a<see cref="DebuggerTypeProxyAttribute"/> attribute,
    /// <c>false</c> otherwise.
    /// </param>
    public static void ApplyToModule(ModuleDefinition module, ProxyFactory factory, bool annotateTypes)
    {
        new ProxyObjectObfuscation(module, factory, annotateTypes).Run();
    }
    
    /// <summary>
    /// Applies the obfuscation.
    /// </summary>
    public void Run()
    {
        foreach (var type in _module.GetAllTypes().ToArray())
        {
            if (_annotateTypes)
                InjectProxyAttribute(type);
            
            foreach (var method in type.Methods)
                InjectProxyObjectCalls(method);
        }
    }

    private void InjectProxyAttribute(TypeDefinition type)
    {
        // Don't create proxies for static classes, attributes or types that do not define a constructor.
        // They never appear as variable types anyway.
        if (type is {IsAbstract: true, IsSealed: true}
            || (type.BaseType?.IsTypeOf("System", "Attribute") ?? false)
            || !type.Methods.Any(x => x is {IsConstructor: true, IsStatic: false}))
        {
            return;
        }

        // Obtain proxy type for this type.
        var proxyType = _proxyFactory.GetProxyType(type.ToTypeSignature());
        
        // Add debugger type proxy attribute.
        type.CustomAttributes.Add(new CustomAttribute(
            _typeProxyConstructor,
            new CustomAttributeSignature(new CustomAttributeArgument(_typeType, proxyType.ToTypeSignature()))));
    }

    private void InjectProxyObjectCalls(MethodDefinition method)
    {
        var body = method.CilMethodBody;
        if (body is null)
            return;

        var instructions = body.Instructions;
        instructions.ExpandMacros();

        // Collect local variables that can be proxied.
        var proxiedLocals = new HashSet<CilLocalVariable>();
        foreach (var local in body.LocalVariables)
        {
            // Exclude locals that use a type definition in the target module as their variable type, since these types
            // are already annotated with the type proxy attribute, and thus do not need to be proxied.
            if (_annotateTypes && local.VariableType.GetUnderlyingTypeDefOrRef() is TypeDefinition
                || local.VariableType.ElementType == ElementType.SzArray)
            {
                continue;
            }

            proxiedLocals.Add(local);
        }
        
        // We cannot change the type of locals that are passed on as a reference (e.g., due to a ref or out parameter)
        // as it would be an unsafe operation (i.e., it could lead to a dereference while assuming the wrong type).
        for (int i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            
            switch (instruction.OpCode.Code)
            {
                case CilCode.Ldloca:
                case CilCode.Ldloca_S:
                    proxiedLocals.Remove(instruction.GetLocalVariable(body.LocalVariables));
                    break;
            }
        }

        // Go over all instructions, and insert box/unbox operator calls when possible.
        for (int i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];

            // Check if this is an instruction operating on a local variable.
            bool isLoad = instruction.IsLdloc();
            if (!isLoad && !instruction.IsStloc())
                continue;

            // Check if the referenced variable is proxied.
            var local = instruction.GetLocalVariable(body.LocalVariables);
            if (!proxiedLocals.Contains(local))
                continue;
            
            // Insert appropriate box/unbox call.
            var type = local.VariableType;
            if (isLoad)
                instructions.Insert(++i, CilOpCodes.Call, _proxyFactory.GetUnboxMethod(type).ImportWith(_module.DefaultImporter));
            else
                instructions.Insert(i++, CilOpCodes.Call, _proxyFactory.GetBoxMethod(type).ImportWith(_module.DefaultImporter));
        }

        // Change all local variable types to their proxy counterparts.
        foreach (var variable in proxiedLocals)
        {
            var proxyType = _proxyFactory.GetProxyType(variable.VariableType);
            
            // Note: we cannot use .ToTypeSignature(), since AsmResolver maps signatures with known corlib type names
            // into CorLibTypeSignatures. Normally this is favourable, but since we are deliberately making explicit
            // copies of every type, we have to manually use the TypeDefOrRefSignature constructor to force the full
            // signature to be created instead.
            variable.VariableType = new TypeDefOrRefSignature(
                proxyType.ImportWith(_module.DefaultImporter),
                proxyType.IsValueType);
        }

        instructions.OptimizeMacros();
    }
}