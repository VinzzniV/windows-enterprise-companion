using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wec.Core.Messaging;

const string generatedHeader = """
    /**
     * GENERATED FILE. Do not edit by hand.
     * Run: dotnet run --project tools/Wec.ContractGenerator
     *
     * These declarations are generated from every payload/result type reachable
     * from IActionHandler<TPayload, TResult> implementations.
     */

    """;

string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
string outputPath = Path.Combine(repositoryRoot, "frontend", "src", "shared", "api-types.generated.ts");
bool checkOnly = args.Contains("--check", StringComparer.Ordinal);

LoadWecAssemblies(AppContext.BaseDirectory);
(IReadOnlyList<Type> contractTypes, ISet<Type> requestTypes) = DiscoverContractTypes();
string generated = Generate(contractTypes, requestTypes);

if (checkOnly)
{
    if (!File.Exists(outputPath)
        || !string.Equals(File.ReadAllText(outputPath), generated, StringComparison.Ordinal))
    {
        Console.Error.WriteLine(
            "Generated bridge contracts are stale. Run 'dotnet run --project tools/Wec.ContractGenerator' and commit the result.");
        return 1;
    }

    Console.WriteLine($"Bridge contracts are current ({contractTypes.Count} generated types).");
    return 0;
}

File.WriteAllText(outputPath, generated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
Console.WriteLine($"Generated {contractTypes.Count} bridge contract types at {Path.GetRelativePath(repositoryRoot, outputPath)}.");
return 0;

static string FindRepositoryRoot(string startPath)
{
    var directory = new DirectoryInfo(startPath);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WindowsEnterpriseCompanion.sln")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName
        ?? throw new InvalidOperationException("Could not locate the repository root.");
}

static void LoadWecAssemblies(string directory)
{
    foreach (string assemblyPath in Directory.EnumerateFiles(directory, "Wec.*.dll"))
    {
        string name = Path.GetFileNameWithoutExtension(assemblyPath);
        if (name.EndsWith(".Tests", StringComparison.Ordinal)
            || name.Equals("Wec.ContractGenerator", StringComparison.Ordinal))
        {
            continue;
        }

        Assembly.LoadFrom(assemblyPath);
    }
}

static (IReadOnlyList<Type> Types, ISet<Type> RequestTypes) DiscoverContractTypes()
{
    Type openHandler = typeof(IActionHandler<,>);
    Type[][] roots = AppDomain.CurrentDomain.GetAssemblies()
        .Where(assembly => assembly.GetName().Name?.StartsWith("Wec.", StringComparison.Ordinal) == true)
        .SelectMany(GetLoadableTypes)
        .Where(type => type is { IsAbstract: false, IsInterface: false })
        .SelectMany(type => type.GetInterfaces())
        .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == openHandler)
        .Select(type => type.GetGenericArguments())
        .ToArray();

    var discovered = new HashSet<Type>();
    var requestTypes = new HashSet<Type>();
    foreach (Type[] rootPair in roots)
    {
        Discover(rootPair[0], discovered, requestTypes);
        Discover(rootPair[1], discovered, directionTypes: null);
    }

    foreach (Type eventContract in AppDomain.CurrentDomain.GetAssemblies()
        .Where(assembly => assembly.GetName().Name?.StartsWith("Wec.", StringComparison.Ordinal) == true)
        .SelectMany(GetLoadableTypes)
        .Where(type => type.GetCustomAttribute<BridgeContractAttribute>() is not null))
    {
        Discover(eventContract, discovered, directionTypes: null);
    }

    var duplicateNames = discovered
        .GroupBy(GetTypeScriptName, StringComparer.Ordinal)
        .Where(group => group.Count() > 1)
        .ToArray();
    if (duplicateNames.Length > 0)
    {
        string details = string.Join(", ", duplicateNames.Select(group =>
            $"{group.Key}: {string.Join(" / ", group.Select(type => type.FullName))}"));
        throw new InvalidOperationException($"TypeScript contract name collision: {details}");
    }

    return (discovered.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray(), requestTypes);
}

static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
{
    try
    {
        return assembly.GetTypes();
    }
    catch (ReflectionTypeLoadException exception)
    {
        return exception.Types.OfType<Type>();
    }
}

static void Discover(Type type, ISet<Type> discovered, ISet<Type>? directionTypes)
{
    type = Nullable.GetUnderlyingType(type) ?? type;
    if (type.IsGenericParameter || IsScalar(type) || type == typeof(object) || type == typeof(JsonElement))
    {
        return;
    }

    if (TryGetDictionaryTypes(type, out Type? keyType, out Type? valueType))
    {
        Discover(keyType, discovered, directionTypes);
        Discover(valueType, discovered, directionTypes);
        return;
    }

    if (TryGetSequenceElement(type, out Type? elementType))
    {
        Discover(elementType, discovered, directionTypes);
        return;
    }

    if (type.IsConstructedGenericType)
    {
        foreach (Type argument in type.GetGenericArguments())
        {
            Discover(argument, discovered, directionTypes);
        }

        type = type.GetGenericTypeDefinition();
    }

    if (type.Namespace?.StartsWith("Wec.", StringComparison.Ordinal) != true)
    {
        return;
    }

    bool isNewDirection = directionTypes?.Add(type) == true;
    bool isNew = discovered.Add(type);
    if ((isNew || isNewDirection) && !type.IsEnum)
    {
        foreach (PropertyInfo property in SerializableProperties(type, forRequest: false))
        {
            Discover(property.PropertyType, discovered, directionTypes);
        }
    }
}

static string Generate(IReadOnlyList<Type> types, ISet<Type> requestTypes)
{
    var builder = new StringBuilder(generatedHeader);
    var nullability = new NullabilityInfoContext();

    foreach (Type type in types)
    {
        if (type.IsEnum)
        {
            GenerateEnum(builder, type);
        }
        else
        {
            GenerateInterface(builder, type, nullability, requestTypes.Contains(type));
        }
    }

    return builder.ToString().TrimEnd().ReplaceLineEndings("\n") + "\n";
}

static void GenerateEnum(StringBuilder builder, Type type)
{
    builder.Append("export type ").Append(GetTypeScriptName(type)).Append(" = ");
    string[] values = Enum.GetNames(type)
        .Select(JsonNamingPolicy.SnakeCaseUpper.ConvertName)
        .ToArray();
    builder.Append(string.Join(" | ", values.Select(value => $"'{value}'"))).AppendLine(";").AppendLine();
}

static void GenerateInterface(
    StringBuilder builder,
    Type type,
    NullabilityInfoContext nullability,
    bool forRequest)
{
    builder.Append("export interface ").Append(GetTypeScriptName(type));
    if (type.IsGenericTypeDefinition)
    {
        builder.Append('<').Append(string.Join(", ", type.GetGenericArguments().Select(argument => argument.Name))).Append('>');
    }

    builder.AppendLine(" {");
    foreach (PropertyInfo property in SerializableProperties(type, forRequest))
    {
        string propertyName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
            ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
        NullabilityInfo info = nullability.Create(property);
        builder.Append("  ").Append(propertyName);
        if (forRequest && HasDefaultConstructorParameter(type, property))
        {
            builder.Append('?');
        }

        builder.Append(": ")
            .Append(MapType(property.PropertyType, info)).AppendLine(";");
    }

    builder.AppendLine("}").AppendLine();
}

static IEnumerable<PropertyInfo> SerializableProperties(Type type, bool forRequest) =>
    type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property.GetMethod is not null
            && property.GetIndexParameters().Length == 0
            && property.GetCustomAttribute<JsonIgnoreAttribute>() is null
            && (!forRequest || property.SetMethod is not null || HasConstructorParameter(type, property)))
        .OrderBy(property => property.MetadataToken);

static bool HasConstructorParameter(Type type, PropertyInfo property) =>
    type.GetConstructors().SelectMany(constructor => constructor.GetParameters()).Any(parameter =>
        string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase));

static bool HasDefaultConstructorParameter(Type type, PropertyInfo property) =>
    type.GetConstructors().SelectMany(constructor => constructor.GetParameters()).Any(parameter =>
        parameter.HasDefaultValue
        && string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase));

static string MapType(Type type, NullabilityInfo? nullability)
{
    Type? nullableValueType = Nullable.GetUnderlyingType(type);
    if (nullableValueType is not null)
    {
        return $"{MapType(nullableValueType, null)} | null";
    }

    string mapped;
    if (type == typeof(byte[]))
    {
        mapped = "string";
    }
    else if (type == typeof(string) || type == typeof(char) || type == typeof(Guid)
        || type == typeof(Uri) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
        || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan))
    {
        mapped = "string";
    }
    else if (type == typeof(bool))
    {
        mapped = "boolean";
    }
    else if (type.IsPrimitive || type == typeof(decimal))
    {
        mapped = "number";
    }
    else if (type == typeof(object) || type == typeof(JsonElement))
    {
        mapped = "unknown";
    }
    else if (TryGetDictionaryTypes(type, out Type? keyType, out Type? valueType))
    {
        string key = keyType == typeof(string) ? "string" : MapType(keyType, null);
        mapped = $"Record<{key}, {MapType(valueType, ChildNullability(nullability, 1))}>";
    }
    else if (TryGetSequenceElement(type, out Type? elementType))
    {
        string element = MapType(elementType, ChildNullability(nullability, 0));
        mapped = element.Contains('|', StringComparison.Ordinal) ? $"({element})[]" : $"{element}[]";
    }
    else
    {
        mapped = GetTypeScriptName(type);
        if (type.IsConstructedGenericType)
        {
            mapped += $"<{string.Join(", ", type.GetGenericArguments().Select(argument => MapType(argument, null)))}>";
        }
    }

    return !type.IsGenericParameter
        && nullability?.ReadState == NullabilityState.Nullable
        && !mapped.EndsWith(" | null", StringComparison.Ordinal)
        ? $"{mapped} | null"
        : mapped;
}

static NullabilityInfo? ChildNullability(NullabilityInfo? parent, int index) =>
    parent is not null && parent.GenericTypeArguments.Length > index
        ? parent.GenericTypeArguments[index]
        : null;

static bool TryGetSequenceElement(Type type, out Type elementType)
{
    if (type.IsArray)
    {
        elementType = type.GetElementType()!;
        return true;
    }

    Type? sequence = type.GetInterfaces().Append(type).FirstOrDefault(candidate =>
        candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
    elementType = sequence?.GetGenericArguments()[0] ?? typeof(void);
    return sequence is not null && type != typeof(string);
}

static bool TryGetDictionaryTypes(Type type, out Type keyType, out Type valueType)
{
    Type? dictionary = type.GetInterfaces().Append(type).FirstOrDefault(candidate =>
        candidate.IsGenericType
        && (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>)
            || candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
    if (dictionary is null)
    {
        keyType = typeof(void);
        valueType = typeof(void);
        return false;
    }

    Type[] arguments = dictionary.GetGenericArguments();
    keyType = arguments[0];
    valueType = arguments[1];
    return true;
}

static bool IsScalar(Type type) =>
    type == typeof(string) || type == typeof(char) || type == typeof(Guid)
    || type == typeof(Uri) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
    || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan)
    || type.IsPrimitive || type == typeof(decimal);

static string GetTypeScriptName(Type type)
{
    if (type.IsGenericParameter)
    {
        return type.Name;
    }

    string name = type.Name;
    int aritySeparator = name.IndexOf('`', StringComparison.Ordinal);
    return aritySeparator >= 0 ? name[..aritySeparator] : name;
}
