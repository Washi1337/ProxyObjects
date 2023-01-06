using static AsmResolver.PE.DotNet.Cil.CilOpCodes;

namespace ProxyObjects.Engine;

/// <summary>
/// A factory that generates proxy types with display strings that once accessed crashes the debuggee process with a
/// call to <see cref="Environment.FailFast(string?)"/>.
/// </summary>
public class CrashProxyFactory : ProxyFactory
{
    private readonly MemberReference _failFast;

    /// <summary>
    /// Initializes the proxy factory.
    /// </summary>
    /// <param name="targetModule">The module to insert the proxy types into.</param>
    public CrashProxyFactory(ModuleDefinition targetModule) 
        : base(targetModule)
    {
        var factory = targetModule.CorLibTypeFactory;
        
        _failFast = factory.CorLibScope
            .CreateTypeReference("System", "Environment")
            .CreateMemberReference("FailFast", MethodSignature.CreateStatic(factory.Void, factory.String))
            .ImportWith(targetModule.DefaultImporter);
    }

    /// <inheritdoc />
    protected override void PostProcessType(TypeSignature originalType, TypeDefinition proxyType)
    {
        var factory = TargetModule.CorLibTypeFactory;

        // Create rogue display property.
        var property = new PropertyDefinition(
            "Display",
            PropertyAttributes.None,
            PropertySignature.CreateInstance(factory.String));

        // Create rogue getter.
        var getter = new MethodDefinition(
            "get_Display",
            MethodAttributes.Public, 
            MethodSignature.CreateInstance(factory.String));

        // Call Environment.FailFast inside getter.
        getter.CilMethodBody = new CilMethodBody(getter)
        {
            Instructions =
            {
                {Ldstr, "The CLR encountered an internal limitation."},
                {Call, _failFast},
                Ldnull,
                Ret
            }
        };
        
        // Add to proxy type.
        property.Semantics.Add(new MethodSemantics(getter, MethodSemanticsAttributes.Getter));
        proxyType.Methods.Add(getter);
        proxyType.Properties.Add(property);
        
        MarkAsNeverDebuggerBrowsable(property);
        AddDisplayString(proxyType, "{" + property.Name + "}");
    }
}