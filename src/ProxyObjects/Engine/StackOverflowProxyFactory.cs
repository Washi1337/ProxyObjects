using static AsmResolver.PE.DotNet.Cil.CilOpCodes;

namespace ProxyObjects.Engine;

public class StackOverflowProxyFactory : ProxyFactory
{
    public StackOverflowProxyFactory(ModuleDefinition targetModule) 
        : base(targetModule)
    {
    }

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