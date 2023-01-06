using static AsmResolver.PE.DotNet.Cil.CilOpCodes;

namespace ProxyObjects.Engine;

public class StateChangerProxyFactory : ProxyFactory
{
    public StateChangerProxyFactory(ModuleDefinition targetModule) 
        : base(targetModule)
    {
    }

    protected override void PostProcessType(TypeSignature originalType, TypeDefinition proxyType)
    {
        switch (originalType.ElementType)
        {
            case ElementType.CModOpt:
            case ElementType.CModReqD:
                PostProcessType(((CustomModifierTypeSignature) originalType).BaseType, proxyType);
                break;
            
            case ElementType.Class:
            case ElementType.ValueType:
                if (originalType.GetUnderlyingTypeDefOrRef()?.Resolve() is { } definition)
                    AddStatefulDisplayProperty(originalType, proxyType, definition);
                break;
        }
    }

    private void AddStatefulDisplayProperty(TypeSignature originalType, TypeDefinition proxyType, TypeDefinition definition)
    {
        var boxedValueField = proxyType.Fields.First();
        var factory = TargetModule.CorLibTypeFactory;

        // Create rogue display property.
        var displayProperty = new PropertyDefinition(
            "Display",
            PropertyAttributes.None,
            PropertySignature.CreateInstance(factory.String));

        // Create rogue getter.
        var displayGetter = new MethodDefinition(
            "get_Display",
            MethodAttributes.Public,
            MethodSignature.CreateInstance(factory.String));

        displayGetter.CilMethodBody = new CilMethodBody(displayGetter);
        var instructions = displayGetter.CilMethodBody.Instructions;

        // Mutate all public fields of the boxed value.
        foreach (var field in definition.Fields)
        {
            if (!field.IsPublic)
                continue;

            var fieldType = field.Signature!.FieldType;
            instructions.Add(Ldarg_0);
            instructions.Add(Ldfld, boxedValueField);
            instructions.Add(CreateTypicalInstruction(fieldType, GenerateTypicalRandomValue(fieldType)));
            instructions.Add(Stfld, field);
        }

        // Call all setter methods of the boxed value.
        foreach (var property in definition.Properties)
        {
            var setter = property.Semantics
                .FirstOrDefault(m => m.Attributes == MethodSemanticsAttributes.Setter)?
                .Method;
            
            if (setter is not { IsPublic: true, Parameters.Count: 1 })
                continue;

            var propertyType = property.Signature!.ReturnType;
            instructions.Add(Ldarg_0);
            instructions.Add(Ldfld, boxedValueField);
            instructions.Add(CreateTypicalInstruction(propertyType, GenerateTypicalRandomValue(propertyType)));
            instructions.Add(Callvirt, setter);
        }

        // Return some random display value.
        instructions.Add(CreateTypicalInstruction(originalType, GenerateTypicalRandomValue(originalType)));
        instructions.Add(Ret);

        // Add to proxy type.
        displayProperty.Semantics.Add(new MethodSemantics(displayGetter, MethodSemanticsAttributes.Getter));
        proxyType.Methods.Add(displayGetter);
        proxyType.Properties.Add(displayProperty);
        
        MarkAsNeverDebuggerBrowsable(displayProperty);
        AddDisplayString(proxyType, "{" + displayProperty.Name + "}");
    }
}