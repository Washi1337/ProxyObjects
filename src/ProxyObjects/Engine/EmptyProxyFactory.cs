using static AsmResolver.PE.DotNet.Cil.CilOpCodes;

namespace ProxyObjects.Engine;

/// <summary>
/// A factory that generates empty proxy types with a random display string.
/// </summary>
public class EmptyProxyFactory : ProxyFactory
{
    private readonly TypeSignature _typeType;
    private readonly MemberReference _attributeCtor;
    
    /// <summary>
    /// Initializes the proxy factory.
    /// </summary>
    /// <param name="targetModule">The module to insert the proxy types into.</param>
    public EmptyProxyFactory(ModuleDefinition targetModule)
        : base(targetModule)
    {
        var factory = targetModule.CorLibTypeFactory;

        _typeType = factory.CorLibScope.CreateTypeReference("System", "Type").ToTypeSignature();
        _attributeCtor = factory.CorLibScope
            .CreateTypeReference("System.Diagnostics", "DebuggerTypeProxyAttribute")
            .CreateMemberReference(".ctor", MethodSignature.CreateInstance(factory.Void, _typeType))
            .ImportWith(targetModule.DefaultImporter);
    }

    /// <inheritdoc />
    protected override void PostProcessType(TypeSignature originalType, TypeDefinition proxyType)
    {
        AddRandomDisplayString(originalType, proxyType);
        AddEmptyDisplayType(proxyType);
    }

    private void AddEmptyDisplayType(TypeDefinition proxyType)
    {
        var displayType = ConstructDisplayType(proxyType);
        var attribute = new CustomAttribute(_attributeCtor, new CustomAttributeSignature(
            new CustomAttributeArgument(_typeType, displayType.ToTypeSignature())));
        proxyType.CustomAttributes.Add(attribute);
        proxyType.NestedTypes.Add(displayType);
    }

    private TypeDefinition ConstructDisplayType(TypeDefinition proxyType)
    {
        var factory = TargetModule.CorLibTypeFactory;

        var displayType = new TypeDefinition(
            null,
            "DisplayType",
            TypeAttributes.NestedPrivate,
            factory.Object.Type.ImportWith(TargetModule.DefaultImporter));

        var ctor = new MethodDefinition(".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName |
            MethodAttributes.RuntimeSpecialName,
            MethodSignature.CreateInstance(factory.Void, new TypeDefOrRefSignature(proxyType)));

        ctor.MethodBody = new CilMethodBody(ctor)
        {
            Instructions =
            {
                Ret
            }
        };

        displayType.Methods.Add(ctor);
        return displayType;
    }
}