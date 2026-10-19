// Flint 静态站点生成器
// 双引擎隔离装配验证（FLUID-MIGRATION-PLAN P0-5 / CONVENTIONS §8.4.2）：
// Fluid 热路径的类型 IL 中不得出现 Scriban 程序集的实体引用——用
// System.Reflection.Metadata 静态扫描方法体 token 的解析结果，捕获
// "在 Fluid 热路径里调用 Scriban" 的意外耦合。
//
// 扫描范围（CONVENTIONS §8.4.2 的 Fluid 热路径定义）：FluidTemplateRenderer、
// FlintMemberAccessStrategy、FluidBuiltinTemplateProvider。
// **不含 TemplateEngineRouter**——router 是路由入口，按设计同时持有两个
// 渲染器（引用 ScribanTemplateRenderer 是其职责，见 TemplateEngineRouter.cs）。
//
// 与 P6-3 运行期探针的分工：本测试是确定性静态门禁（CI 可跑）；
// 运行期"渲染时不加载 Scriban 程序集"探针因测试进程被其他用例先载入
// Scriban 而不可靠，归 Phase 6 独立进程探针。

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

public sealed class EngineIsolationTests
{
    /// <summary>Fluid 热路径的类型名（这些类型的 IL 不得引用 Scriban）</summary>
    private static readonly string[] FluidHotPathTypes =
    [
        "FluidTemplateRenderer",
        "FlintMemberAccessStrategy",
        "FluidBuiltinTemplateProvider"
    ];

    [Fact]
    public void Fluid_热路径_IL_不引用_Scriban_程序集()
    {
        var assemblyPath = typeof(FluidTemplateRenderer).Assembly.Location;
        Assert.False(string.IsNullOrEmpty(assemblyPath), "Flint.Core 程序集路径不可用");

        using var peStream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(peStream);
        var reader = peReader.GetMetadataReader();

        var violations = new List<string>();
        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            var name = reader.GetString(typeDef.Name);
            if (!FluidHotPathTypes.Contains(name) ||
                reader.GetString(typeDef.Namespace) != "Flint.Core.Templates")
            {
                continue;
            }

            ScanType(peReader, reader, typeDef, name, violations);
            // 嵌套类型（如 CachedTemplate record）同属热路径，一并扫描
            foreach (var nestedHandle in typeDef.GetNestedTypes())
            {
                var nested = reader.GetTypeDefinition(nestedHandle);
                ScanType(peReader, reader, nested, name + "+" + reader.GetString(nested.Name), violations);
            }
        }

        Assert.True(violations.Count == 0,
            $"Fluid 热路径引用 Scriban 程序集（隔离硬规则违反）：\n{string.Join("\n", violations)}");
    }

    private static void ScanType(
        PEReader peReader,
        MetadataReader reader,
        TypeDefinition typeDef,
        string typeName,
        List<string> violations)
    {
        // 基类/接口签名检查
        if (ResolveEntityName(reader, typeDef.BaseType) is { } baseName && IsScribanEntity(baseName, out var m1))
        {
            violations.Add($"{typeName} 基类引用 Scriban: {m1}");
        }
        foreach (var interfaceHandle in typeDef.GetInterfaceImplementations())
        {
            var iface = reader.GetInterfaceImplementation(interfaceHandle);
            if (ResolveEntityName(reader, iface.Interface) is { } ifaceName && IsScribanEntity(ifaceName, out var m2))
            {
                violations.Add($"{typeName} 接口引用 Scriban: {m2}");
            }
        }

        // 字段/方法签名检查（DecodeSignature 解析嵌套类型引用）
        var provider = new Provider();
        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if (IsScribanEntity(field.DecodeSignature(provider, null), out var fieldTypeName))
            {
                violations.Add($"{typeName} 字段 {reader.GetString(field.Name)} 类型引用 Scriban: {fieldTypeName}");
            }
        }
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            var methodName = reader.GetString(method.Name);
            // DecodeSignature 返回 MethodSignature<string>：返回类型与各参数类型
            // 逐一判 Scriban（不能用 IsScribanEntity 直接接签名对象）
            var signature = method.DecodeSignature(provider, null);
            if (IsScribanEntity(signature.ReturnType, out var retName))
            {
                violations.Add($"{typeName} 方法 {methodName} 返回类型引用 Scriban: {retName}");
            }
            foreach (var paramType in signature.ParameterTypes)
            {
                if (IsScribanEntity(paramType, out var paramName))
                {
                    violations.Add($"{typeName} 方法 {methodName} 参数类型引用 Scriban: {paramName}");
                }
            }

            // IL 体 token 扫描：call/newobj/ldfld 等指令的操作数解析到
            // Scriban 命名空间的实体即违规
            if (method.RelativeVirtualAddress == 0)
            {
                continue;
            }
            var body = peReader.GetMethodBody(method.RelativeVirtualAddress);
            var il = body.GetILBytes();
            if (il is null)
            {
                continue;
            }
            foreach (var (token, resolved) in ScanIlTokens(il, reader))
            {
                if (IsScribanEntity(resolved, out var ilName))
                {
                    violations.Add(
                        $"{typeName}.{reader.GetString(method.Name)} IL token 0x{token:X8} 引用 Scriban: {ilName}");
                }
            }
        }
    }

    /// <summary>
    /// IL 字节流中解析出的元数据 token 及其实体名。逐 4 字节窗口尝试解析，
    /// token 高字节须为已知表号（TypeRef/TypeDef/MethodDef/MemberRef/FieldDef/
    /// TypeSpec/MethodSpec）且能解析到命名空间实体才产出——立即数极少同时满足
    /// "token 形态 + 可解析 + Scriban 命名空间"，误报方向是"多报待人工确认"
    /// 而非漏报（本检查是防意外耦合的门禁，不是安全边界）
    /// </summary>
    private static IEnumerable<(int Token, string Entity)> ScanIlTokens(byte[] il, MetadataReader reader)
    {
        for (var i = 0; i + 4 <= il.Length; i++)
        {
            var token = BitConverter.ToInt32(il, i);
            if (token <= 0)
            {
                continue;
            }
            var table = token >> 24;
            if (table is not (0x01 or 0x02 or 0x04 or 0x06 or 0x0A or 0x1B or 0x2B))
            {
                continue;
            }
            var resolved = ResolveToken(reader, token);
            if (resolved is not null)
            {
                yield return (token, resolved);
            }
        }
    }

    private static string? ResolveToken(MetadataReader reader, int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        try
        {
            return handle.Kind switch
            {
                HandleKind.TypeReference => EntityName(reader,
                    reader.GetTypeReference((TypeReferenceHandle)handle).Namespace,
                    reader.GetTypeReference((TypeReferenceHandle)handle).Name),
                HandleKind.TypeDefinition => EntityName(reader,
                    reader.GetTypeDefinition((TypeDefinitionHandle)handle).Namespace,
                    reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name),
                HandleKind.MemberReference => ResolveEntityName(reader,
                    reader.GetMemberReference((MemberReferenceHandle)handle).Parent),
                HandleKind.MethodDefinition => ResolveEntityName(reader,
                    reader.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType()),
                HandleKind.FieldDefinition => ResolveEntityName(reader,
                    reader.GetFieldDefinition((FieldDefinitionHandle)handle).GetDeclaringType()),
                _ => null
            };
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static string? ResolveEntityName(MetadataReader reader, EntityHandle handle)
    {
        try
        {
            return handle.Kind switch
            {
                HandleKind.TypeReference => EntityName(reader,
                    reader.GetTypeReference((TypeReferenceHandle)handle).Namespace,
                    reader.GetTypeReference((TypeReferenceHandle)handle).Name),
                HandleKind.TypeDefinition => EntityName(reader,
                    reader.GetTypeDefinition((TypeDefinitionHandle)handle).Namespace,
                    reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name),
                _ => null
            };
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static string EntityName(MetadataReader reader, StringHandle namespaceHandle, StringHandle nameHandle)
    {
        var ns = namespaceHandle.IsNil ? "" : reader.GetString(namespaceHandle);
        var name = reader.GetString(nameHandle);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static bool IsScribanEntity(string? fullName, out string matched)
    {
        matched = fullName ?? "";
        return fullName is not null &&
               (fullName.StartsWith("Scriban.", StringComparison.Ordinal) ||
                fullName.Equals("Scriban", StringComparison.Ordinal));
    }

    private sealed class Provider : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string elementType, ArrayShape shape) => elementType;
        public string GetByReferenceType(string elementType) => elementType;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "";
        public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments) => genericType;
        public string GetGenericMethodParameter(object? genericContext, int index) => "";
        public string GetGenericTypeParameter(object? genericContext, int index) => "";
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType;
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "";
        public string GetSZArrayType(string elementType) => elementType;
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            var ns = typeDef.Namespace.IsNil ? "" : reader.GetString(typeDef.Namespace);
            var name = reader.GetString(typeDef.Name);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var typeRef = reader.GetTypeReference(handle);
            var ns = typeRef.Namespace.IsNil ? "" : reader.GetString(typeRef.Namespace);
            var name = reader.GetString(typeRef.Name);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => "";
    }
}
