using System.Reflection;
using System.Text;

namespace Plover.Ui.Tests.Contracts;

/// <summary>Writes the TypeScript types of the dashboard's JSON from the C# records that produce it, so the page and the controller cannot drift apart.</summary>
/// <remarks>
/// Follows the rules of the dashboard's JSON: camel-case names, enums as their names, and null written out for what has no value.
/// Only what the dashboard sends is supported: records, enums, lists, dictionaries with text keys, and the usual numbers, text and dates.
/// </remarks>
internal sealed class ContractWriter
{
    private readonly SortedDictionary<string, string> _declarations = new(StringComparer.Ordinal);
    private readonly NullabilityInfoContext _nullability = new();

    /// <summary>Adds a type and everything it refers to.</summary>
    /// <param name="type">The root type of a response.</param>
    /// <returns>This writer, for chaining.</returns>
    public ContractWriter Add(Type type)
    {
        Reference(type);
        return this;
    }

    /// <summary>Writes the file.</summary>
    /// <returns>The TypeScript source.</returns>
    public string Write()
    {
        var text = new StringBuilder();
        text.AppendLine("// GENERATED from the C# types of the dashboard API. Do not edit.");
        text.AppendLine("// Regenerate with: UPDATE_CONTRACTS=1 dotnet test --project tests/Plover.Ui.Tests");
        foreach (var declaration in _declarations.Values)
        {
            text.AppendLine();
            text.Append(declaration);
        }

        return text.ToString();
    }

    /// <summary>The TypeScript for a mention of a type, declaring it first when it is a record or an enum.</summary>
    private string Reference(Type type, NullabilityInfo? nullability = null)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var isNullable = underlying is not null || nullability?.ReadState == NullabilityState.Nullable;
        var core = underlying ?? type;
        var text = Core(core, nullability);
        return isNullable ? $"{text} | null" : text;
    }

    private string Core(Type type, NullabilityInfo? nullability)
    {
        if (type == typeof(string) || type == typeof(DateTimeOffset) || type == typeof(DateTime) || type == typeof(Guid))
        {
            return "string";
        }

        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (type.IsPrimitive || type == typeof(decimal))
        {
            return "number";
        }

        if (type.IsEnum)
        {
            DeclareEnum(type);
            return type.Name;
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();
            var inner = nullability?.GenericTypeArguments;
            if (definition == typeof(IReadOnlyList<>) || definition == typeof(List<>) || definition == typeof(IEnumerable<>))
            {
                var element = Reference(arguments[0], inner?.FirstOrDefault());
                return element.Contains('|', StringComparison.Ordinal) ? $"({element})[]" : $"{element}[]";
            }

            if (definition == typeof(IReadOnlyDictionary<,>) || definition == typeof(Dictionary<,>))
            {
                return $"Record<string, {Reference(arguments[1], inner?.ElementAtOrDefault(1))}>";
            }

            throw new NotSupportedException($"The generic type {type} is not part of the dashboard's JSON.");
        }

        DeclareRecord(type);
        return type.Name;
    }

    private void DeclareEnum(Type type)
    {
        if (_declarations.ContainsKey(type.Name))
        {
            return;
        }

        var names = string.Join(" | ", Enum.GetNames(type).Select(name => $"\"{name}\""));
        _declarations[type.Name] = $"export type {type.Name} = {names};\n";
    }

    private void DeclareRecord(Type type)
    {
        if (_declarations.ContainsKey(type.Name))
        {
            return;
        }

        // Reserved first, so a type that refers to itself does not loop.
        _declarations[type.Name] = "";

        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var members = new StringBuilder();
        foreach (var parameter in constructor.GetParameters())
        {
            var property = type.GetProperty(parameter.Name!, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new NotSupportedException($"{type.Name}.{parameter.Name} is not a property.");
            var name = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
            members.AppendLine($"  {name}: {Reference(property.PropertyType, _nullability.Create(property))};");
        }

        _declarations[type.Name] = $"export interface {type.Name} {{\n{members}}}\n";
    }
}
