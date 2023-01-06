using static AsmResolver.PE.DotNet.Cil.CilOpCodes;

namespace ProxyObjects.Engine;

/// <summary>
/// A factory that generates proxy types with display strings that once accessed crashes the debuggee process with a
/// <see cref="StackOverflowException" /> by calling itself over and over again.
/// </summary>
public class StackOverflowProxyFactory : ProxyFactory
{
    /// <summary>
    /// Initializes the proxy factory.
    /// </summary>
    /// <param name="targetModule">The module to insert the proxy types into.</param>
    public StackOverflowProxyFactory(ModuleDefinition targetModule) 
        : base(targetModule)
    {
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

        // Call getter itself to cause a stack overflow.
        getter.CilMethodBody = new CilMethodBody(getter)
        {
            Instructions =
            {
                Ldarg_0,
                {Call, getter},
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