using System.Diagnostics;
using static AsmResolver.PE.DotNet.Cil.CilOpCodes;

namespace ProxyObjects.Engine;

public abstract class ProxyFactory
{
    private readonly record struct ProxyTypeInfo(TypeDefinition Type, MethodDefinition Box, MethodDefinition Unbox);

    private readonly Dictionary<TypeSignature, ProxyTypeInfo> _cache = new(SignatureComparer.Default);
    private readonly ITypeDefOrRef _objectType;
    private readonly MemberReference _objectCtor;
    private readonly ITypeDefOrRef _valueType;
    private readonly MemberReference _compilerGeneratorCtor;
    private readonly MemberReference _displayStringCtor;
    private readonly TypeSignature _debuggerBrowsableStateType;
    private readonly MemberReference _debuggerBrowsableCtor;

    protected ProxyFactory(ModuleDefinition targetModule)
    {
        TargetModule = targetModule;

        var factory = targetModule.CorLibTypeFactory;
        
        _objectType = factory.Object.Type.ImportWith(targetModule.DefaultImporter);

        _objectCtor = _objectType
            .CreateMemberReference(".ctor", MethodSignature.CreateInstance(factory.Void))
            .ImportWith(targetModule.DefaultImporter);

        _valueType = factory.CorLibScope
            .CreateTypeReference("System", "ValueType")
            .ImportWith(targetModule.DefaultImporter);
        
        _compilerGeneratorCtor = factory.CorLibScope
            .CreateTypeReference("System.Runtime.CompilerServices", "CompilerGeneratedAttribute")
            .CreateMemberReference(".ctor", MethodSignature.CreateInstance(factory.Void))
            .ImportWith(targetModule.DefaultImporter);
        
        _displayStringCtor = factory.CorLibScope
            .CreateTypeReference("System.Diagnostics", "DebuggerDisplayAttribute")
            .CreateMemberReference(".ctor", MethodSignature.CreateInstance(factory.Void, factory.String))
            .ImportWith(targetModule.DefaultImporter);

        _debuggerBrowsableStateType = factory.CorLibScope
            .CreateTypeReference("System.Diagnostics", "DebuggerBrowsableState")
            .ToTypeSignature()
            .ImportWith(targetModule.DefaultImporter);
        
        _debuggerBrowsableCtor = factory.CorLibScope
            .CreateTypeReference("System.Diagnostics", "DebuggerBrowsableAttribute")
            .CreateMemberReference(".ctor", MethodSignature.CreateInstance(factory.Void, _debuggerBrowsableStateType))
            .ImportWith(targetModule.DefaultImporter);
    }

    public ModuleDefinition TargetModule
    {
        get;
    }

    protected Random Randomizer
    {
        get;
    } = new();

    public MethodDefinition GetBoxMethod(TypeSignature type) => GetProxyTypeInfo(type).Box;

    public MethodDefinition GetUnboxMethod(TypeSignature type) => GetProxyTypeInfo(type).Unbox;

    public TypeDefinition GetProxyType(TypeSignature type) => GetProxyTypeInfo(type).Type;

    private ProxyTypeInfo GetProxyTypeInfo(TypeSignature type)
    {
        if (!_cache.TryGetValue(type, out var info))
        {
            info = ConstructProxyType(type);
            _cache.Add(type, info);
        }

        return info;
    }

    private ProxyTypeInfo ConstructProxyType(TypeSignature originalType)
    {
        var importedOriginalType = originalType.ImportWith(TargetModule.DefaultImporter);

        // Create the proxy type.
        bool shouldObfuscateName = (importedOriginalType.Namespace?.StartsWith("System") ?? false)
                               && originalType.GetUnderlyingTypeDefOrRef() is not TypeDefinition;

        string? name = shouldObfuscateName
            ? importedOriginalType.Name
            : NameObfuscation.ApplyHomoglyphs(importedOriginalType.Name);

        var proxyType = new TypeDefinition(
            originalType.Namespace,
            name,
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            importedOriginalType.IsValueType
                ? _valueType
                : _objectType);

        if (importedOriginalType.IsValueType) 
            proxyType.IsSequentialLayout = true;

        // Inject into module. We do this early such that its resolution scope is set, allowing PostProcessType to
        // import it safely.
        TargetModule.TopLevelTypes.Add(proxyType);

        // Create the field containing the wrapped object. We use the format <xxx>k__BackingField to trick decompilers
        // into thinking it is a compiler generated field that should be hidden.
        var valueField = new FieldDefinition(
            "<0this>k__BackingField",
            FieldAttributes.Private | FieldAttributes.InitOnly,
            importedOriginalType);
        
        MarkAsCompilerGenerated(valueField);
        MarkAsNeverDebuggerBrowsable(valueField);

        // Create the default operation methods.
        var ctor = CreateConstructor(importedOriginalType, valueField);
        var box = CreateBoxMethod(importedOriginalType, proxyType, ctor);
        var unbox = CreateUnboxMethod(importedOriginalType, proxyType, valueField);

        // Add all to the proxy type.
        proxyType.Fields.Add(valueField);
        proxyType.Methods.Add(ctor);
        proxyType.Methods.Add(box);
        proxyType.Methods.Add(unbox);

        // Post process the type.
        PostProcessType(originalType, proxyType);

        return new ProxyTypeInfo(proxyType, box, unbox);
    }

    private MethodDefinition CreateConstructor(TypeSignature originalType, FieldDefinition valueField)
    {
        // Create constructor
        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName |
            MethodAttributes.RuntimeSpecialName,
            MethodSignature.CreateInstance(TargetModule.CorLibTypeFactory.Void, originalType));

        // Initialize the wrapped value field.
        ctor.CilMethodBody = new CilMethodBody(ctor)
        {
            Instructions =
            {
                Ldarg_0,
                Ldarg_1,
                {Stfld, valueField},
                Ret
            }
        };

        // If the type is a ref type, make sure the object is initialized.
        if (originalType.IsValueType)
        {
            ctor.CilMethodBody.Instructions.InsertRange(0, new[]
            {
                new CilInstruction(Ldarg_0),
                new CilInstruction(Call, _objectCtor)
            });
        }

        return ctor;
    }

    private static MethodDefinition CreateBoxMethod(
        TypeSignature originalType,
        TypeDefinition proxyType,
        MethodDefinition ctor)
    {
        var signatureProxyType = new TypeDefOrRefSignature(proxyType);

        var create = new MethodDefinition(
            "op_Implicit",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig |
            MethodAttributes.SpecialName,
            MethodSignature.CreateStatic(signatureProxyType, originalType));

        if (originalType.IsValueType)
        {
            // For value types, we need to initialize it with an explicit `call` on a local variable.
            var local = new CilLocalVariable(signatureProxyType);
            create.CilMethodBody = new CilMethodBody(create)
            {
                LocalVariables = {local},
                Instructions =
                {
                    {Ldloca_S, local},
                    Ldarg_0,
                    {Call, ctor},
                    Ldloc_0,
                    Ret
                }
            };
        }
        else
        {
            // For reference types, we can simply use `newobj`.
            create.CilMethodBody = new CilMethodBody(create)
            {
                Instructions =
                {
                    Ldarg_0,
                    {Newobj, ctor},
                    Ret
                }
            };
        }

        return create;
    }

    private static MethodDefinition CreateUnboxMethod(
        TypeSignature originalType, 
        TypeDefinition proxyType, 
        FieldDefinition valueField)
    {
        var from = new MethodDefinition(
            "op_Implicit",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
            MethodSignature.CreateStatic(originalType, new TypeDefOrRefSignature(proxyType, proxyType.IsValueType)));
        
        from.CilMethodBody = new CilMethodBody(from)
        {
            Instructions =
            {
                Ldarg_0,
                {Ldfld, valueField},
                Ret
            }
        };

        return from;
    }

    protected abstract void PostProcessType(TypeSignature originalType, TypeDefinition proxyType);
    
    protected object? GenerateTypicalRandomValue(TypeSignature type)
    {
        switch (type.ElementType)
        {
            case ElementType.Boolean:
                return Randomizer.Next(2) == 1;

            case ElementType.I1:
            case ElementType.I2:
            case ElementType.I4:
            case ElementType.U1:
            case ElementType.U2:
            case ElementType.U4:
                return Randomizer.Next(100);

            case ElementType.I8:
            case ElementType.U8:
                return Randomizer.Next(100000);

            case ElementType.String:
                return "Hey no peeping in the debugger! (╯°□°)╯︵ ┻━┻";

            case ElementType.R4:
                return Randomizer.NextSingle();

            case ElementType.R8:
                return Randomizer.NextDouble();

            default:
                return null;
        }
    }

    protected string? FormatString(TypeSignature originalType, object? value)
    {
        switch (originalType.ElementType)
        {
            case ElementType.Boolean:
                // Booleans are displayed as either "true" or "false".
                return value!.ToString()!.ToLower();

            case ElementType.Char:
                // Chars are surrounded by single-quotes.
                return $"'{value}'";

            case ElementType.I1:
            case ElementType.U1:
            case ElementType.I2:
            case ElementType.U2:
            case ElementType.I4:
            case ElementType.U4:
            case ElementType.I8:
            case ElementType.U8:
            case ElementType.I:
            case ElementType.U:
            case ElementType.R4:
            case ElementType.R8:
                // Numerical values are displayed as-is.
                return value!.ToString()!;

            case ElementType.String:
                // Strings are surrounded by double-quotes.
                return value is null ?
                    "null"
                    : $"\"{value}\"";

            case ElementType.Array:
            case ElementType.SzArray:
                // Arrays are displayed as Type[count].
                return $"\\{{{((TypeSpecificationSignature) originalType).BaseType.FullName}[0]\\}}";

            case ElementType.CModReqD:
            case ElementType.CModOpt:
            case ElementType.ByRef:
            case ElementType.Pinned:
                // Ignore any modifiers.
                return FormatString(((TypeSpecificationSignature) originalType).BaseType, value);

            case ElementType.FnPtr:
            case ElementType.Object:
            case ElementType.Ptr:
            case ElementType.ValueType:
            case ElementType.Class:
            case ElementType.Var:
            case ElementType.MVar:
            case ElementType.GenericInst:
            case ElementType.TypedByRef:
            case ElementType.Internal:
            case ElementType.Modifier:
            case ElementType.Type:
            case ElementType.Boxed:
            case ElementType.Enum:
            default:
                // Anything else does not need a custom display string -> abort.
                return null;
        }
    }
    
    protected static CilInstruction CreateTypicalInstruction(TypeSignature type, object? value)
    {
        var opcode = type.ElementType switch
        {
            ElementType.Boolean
                or ElementType.I1
                or ElementType.I2
                or ElementType.I4
                or ElementType.U1
                or ElementType.U2
                or ElementType.U4 => Ldc_I4,

            ElementType.I8 or
                ElementType.U8 => Ldc_I8,

            ElementType.R4 => Ldc_R4,

            ElementType.R8 => Ldc_R8,

            ElementType.String => Ldstr,
            
            _ => Ldnull
        };

        if (type.ElementType == ElementType.Boolean)
            value = Convert.ToInt32(value);

        return new CilInstruction(opcode, value);
    }

    protected void AddRandomDisplayString(TypeSignature originalType, TypeDefinition proxyType)
    {
        object? value = GenerateTypicalRandomValue(originalType);
        
        switch (originalType.ElementType)
        {
            case ElementType.Void:
            case ElementType.Boolean:
            case ElementType.Char:
            case ElementType.I1:
            case ElementType.U1:
            case ElementType.I2:
            case ElementType.U2:
            case ElementType.I4:
            case ElementType.U4:
            case ElementType.I8:
            case ElementType.U8:
            case ElementType.R4:
            case ElementType.R8:
            case ElementType.String:
            case ElementType.I:
            case ElementType.U:
                // We can improve the display of intrinsic types by introducing a display property.  
                var property = AddDisplayProperty(originalType, proxyType, value);
                AddDisplayString(proxyType, "{" + property.Name + "}");
                break;
            
            default:
                // Otherwise, come up with a string representation and just use that.
                AddDisplayString(proxyType, FormatString(originalType, value));
                break;
        }
    }

    private PropertyDefinition AddDisplayProperty(
        TypeSignature originalType,
        TypeDefinition proxyType, 
        object? value)
    {
        // Create property.
        var property = new PropertyDefinition(
            "Display",
            PropertyAttributes.None,
            PropertySignature.CreateInstance(originalType));

        // Add getter.
        var getter = new MethodDefinition(
            "get_Display",
            MethodAttributes.Public,
            MethodSignature.CreateInstance(originalType));

        getter.CilMethodBody = new CilMethodBody(getter)
        {
            Instructions =
            {
                CreateTypicalInstruction(originalType, value),
                Ret
            }
        };
        
        property.Semantics.Add(new MethodSemantics(getter, MethodSemanticsAttributes.Getter));
        
        // Add [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        MarkAsNeverDebuggerBrowsable(property);

        // Add property to type.
        proxyType.Properties.Add(property);
        proxyType.Methods.Add(getter);

        return property;
    }

    protected void AddDisplayString(TypeDefinition type, string? value)
    {
        if (value is null)
            return;
        
        type.CustomAttributes.Add(new CustomAttribute(_displayStringCtor, new CustomAttributeSignature(
            new CustomAttributeArgument(TargetModule.CorLibTypeFactory.String, value)
        )));
    }

    protected void MarkAsNeverDebuggerBrowsable(IHasCustomAttribute property)
    {
        property.CustomAttributes.Add(new CustomAttribute(_debuggerBrowsableCtor, new CustomAttributeSignature(
            new CustomAttributeArgument(_debuggerBrowsableStateType, (int) DebuggerBrowsableState.Never)))
        );
    }

    private void MarkAsCompilerGenerated(IHasCustomAttribute valueField)
    {
        valueField.CustomAttributes.Add(new CustomAttribute(_compilerGeneratorCtor));
    }
}