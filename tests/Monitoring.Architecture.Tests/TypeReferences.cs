using System.Reflection;

namespace Monitoring.Architecture.Tests;

/// <summary>Finds the types that a type mentions in its declaration: base types, interfaces, members and their signatures.</summary>
/// <remarks>shortcut: method bodies are not inspected, only signatures; upgrade to reading IL when a rule is broken through a local variable.</remarks>
internal static class TypeReferences
{
    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static IEnumerable<Type> Of(Type type)
    {
        var found = new HashSet<Type>();
        void Add(Type? candidate)
        {
            foreach (var part in Expand(candidate))
            {
                found.Add(part);
            }
        }

        Add(type.BaseType);
        foreach (var contract in type.GetInterfaces())
        {
            Add(contract);
        }

        foreach (var field in type.GetFields(AllMembers))
        {
            Add(field.FieldType);
        }

        foreach (var property in type.GetProperties(AllMembers))
        {
            Add(property.PropertyType);
        }

        foreach (var method in type.GetMethods(AllMembers).Cast<MethodBase>().Concat(type.GetConstructors(AllMembers)))
        {
            foreach (var parameter in method.GetParameters())
            {
                Add(parameter.ParameterType);
            }

            if (method is MethodInfo { ReturnType: var returns })
            {
                Add(returns);
            }
        }

        return found;
    }

    /// <summary>The type itself and, for arrays, references and generics, the types inside it.</summary>
    private static IEnumerable<Type> Expand(Type? type)
    {
        if (type is null)
        {
            yield break;
        }

        if (type.HasElementType)
        {
            foreach (var inner in Expand(type.GetElementType()))
            {
                yield return inner;
            }

            yield break;
        }

        yield return type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var inner in Expand(argument))
            {
                yield return inner;
            }
        }
    }
}
