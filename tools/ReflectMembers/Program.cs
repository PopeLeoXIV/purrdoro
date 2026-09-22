using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

// Loads Purrdoro.Core.dll and prints { "TypeName": ["Member", ...] } for its ViewModels.
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
var result = assembly.GetTypes()
    .Where(t => t.IsPublic && t.Namespace == "Purrdoro.Core.ViewModels")
    .ToDictionary(
        t => t.Name,
        t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).Distinct().OrderBy(n => n).ToArray());
Console.WriteLine(JsonSerializer.Serialize(result));
