using System;
using System.Collections.Generic;
using System.Reflection;

namespace MovieTracker.Evals;

public static class CheckDiscovery
{
    public static IReadOnlyDictionary<string, IEvalCheck> DiscoverAll()
    {
        Dictionary<string, IEvalCheck> map = new Dictionary<string, IEvalCheck>(StringComparer.Ordinal);
        Assembly assembly = typeof(IEvalCheck).Assembly;

        foreach (Type type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface)
            {
                continue;
            }

            if (!typeof(IEvalCheck).IsAssignableFrom(type))
            {
                continue;
            }

            object? instance = Activator.CreateInstance(type);
            if (instance is not IEvalCheck check)
            {
                continue;
            }

            if (map.ContainsKey(check.Name))
            {
                throw new InvalidOperationException($"Duplicate IEvalCheck name '{check.Name}' (from {type.FullName})");
            }

            map[check.Name] = check;
        }

        return map;
    }
}
