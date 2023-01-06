using static AsmResolver.PE.DotNet.Cil.CilOpCodes;

namespace ProxyObjects.Engine;

public class MimicProxyFactory : ProxyFactory
{
    public MimicProxyFactory(ModuleDefinition targetModule) 
        : base(targetModule)
    {
    }

    protected override void PostProcessType(TypeSignature originalType, TypeDefinition proxyType)
    {
        AddRandomDisplayString(originalType, proxyType);
        MimicOriginalType(originalType, proxyType);
    }

    private void MimicOriginalType(TypeSignature originalType, TypeDefinition mimicType)
    {
        switch (originalType.ElementType)
        {
            case ElementType.CModOpt:
            case ElementType.CModReqD:
                // We can ignore the modifier types, they don't affect how the underlying type is displayed in
                // the locals window of the debugger.
                MimicOriginalType(((TypeSpecificationSignature) originalType).BaseType, mimicType);
                break;

            case ElementType.ValueType:
            case ElementType.Class:
                // Mimic all the fields and properties in the class.
                if (originalType.Resolve() is { } definition)
                {
                    MimicFields(mimicType, definition);
                    MimicProperties(mimicType, definition);
                }
                break;
        }
    }

    private void MimicFields(TypeDefinition mimicType, TypeDefinition originalType)
    {
        foreach (var originalField in originalType.Fields)
            MimicField(mimicType, originalField);
    }

    private void MimicField(TypeDefinition mimicType, FieldDefinition originalField)
    {
        if (originalField.Name is null || originalField.Signature is null)
            return;

        // Construct new field.
        var fieldType = originalField.Signature.FieldType;
        var newField = new FieldDefinition(originalField.Name, FieldAttributes.Public, fieldType);
        
        // Add initializer to constructor.
        var ctor = mimicType.Methods.First(m => m.IsConstructor);
        var instructions = ctor.CilMethodBody!.Instructions;
        instructions.InsertRange(instructions.Count - 1, new[]
        {
            new CilInstruction(Ldarg_0),
            CreateTypicalInstruction(fieldType, GenerateTypicalRandomValue(fieldType)),
            new CilInstruction(Stfld, newField)
        });

        // Add to display type.
        mimicType.Fields.Add(newField);
    }

    private void MimicProperties(TypeDefinition mimicType, TypeDefinition originalType)
    {
        foreach (var originalProperty in originalType.Properties)
            MimicProperty(mimicType, originalProperty);
    }

    private void MimicProperty(TypeDefinition mimicType, PropertyDefinition originalProperty)
    {
        if (originalProperty.Name is null || originalProperty.Signature is null)
            return;
        
        // Construct new property.
        var propertyType = originalProperty.Signature.ReturnType;
        var newProperty = new PropertyDefinition(
            originalProperty.Name,
            originalProperty.Attributes,
            new PropertySignature(
                originalProperty.Signature.Attributes,
                propertyType,
                originalProperty.Signature.ParameterTypes));

        // Construct new getter method.
        var newGetter = new MethodDefinition($"get_{newProperty}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            MethodSignature.CreateInstance(propertyType));

        newGetter.CilMethodBody = new CilMethodBody(newGetter);
        var instructions = newGetter.CilMethodBody.Instructions;
        instructions.Add(CreateTypicalInstruction(propertyType, GenerateTypicalRandomValue(propertyType)));
        instructions.Add(Ret);

        // Attach getter to property.
        newProperty.Semantics.Add(new MethodSemantics(newGetter, MethodSemanticsAttributes.Getter));

        // Add members to display type.
        mimicType.Methods.Add(newGetter);
        mimicType.Properties.Add(newProperty);
    }

}