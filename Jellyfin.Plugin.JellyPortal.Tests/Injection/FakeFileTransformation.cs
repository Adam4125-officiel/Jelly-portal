using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;

// This namespace and type name are the ones the real File Transformation plugin exposes. The fake below
// behaves like the real one for what matters here: it takes a Newtonsoft JObject.
namespace Jellyfin.Plugin.FileTransformation
{
    public static class PluginInterface
    {
        public static JObject? LastPayload { get; set; }

        public static void RegisterTransformation(JObject payload) => LastPayload = payload;
    }
}

namespace Jellyfin.Plugin.JellyPortal.Tests.Injection
{
    /// <summary>Stand-ins for File Transformation, and a re-enactment of how it calls a callback.</summary>
    internal static class FakeFileTransformation
    {
        /// <summary>The real File Transformation, as far as the bridge can tell.</summary>
        public static Assembly Working => typeof(Jellyfin.Plugin.FileTransformation.PluginInterface).Assembly;

        /// <summary>
        /// An assembly shaped like File Transformation but with a different RegisterTransformation: none at all,
        /// two parameters, a parameter that cannot be built from JSON, or one that throws.
        /// </summary>
        public static Assembly Shaped(string variant)
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("FakeFileTransformation." + variant), AssemblyBuilderAccess.Run);
            var module = assembly.DefineDynamicModule("m");
            var type = module.DefineType("Jellyfin.Plugin.FileTransformation.PluginInterface", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            switch (variant)
            {
                case "no-method":
                    break;
                case "two-parameters":
                    type.DefineMethod("RegisterTransformation", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [typeof(JObject), typeof(int)])
                        .GetILGenerator().Emit(OpCodes.Ret);
                    break;
                case "unparseable-parameter":
                    type.DefineMethod("RegisterTransformation", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [typeof(Uri)])
                        .GetILGenerator().Emit(OpCodes.Ret);
                    break;
                case "throws":
                    var il = type.DefineMethod("RegisterTransformation", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [typeof(JObject)]).GetILGenerator();
                    il.Emit(OpCodes.Ldstr, "File Transformation is broken");
                    il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
                    il.Emit(OpCodes.Throw);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(variant));
            }

            type.CreateType();
            return assembly;
        }

        /// <summary>
        /// Does what File Transformation's TransformationHelper does with a registered callback: finds the assembly by
        /// its full name among all load contexts, the class, the method, builds the first parameter from
        /// {"contents": ...} with Newtonsoft's ToObject, and invokes the static method.
        /// </summary>
        public static string? Invoke(JObject registration, string contents)
        {
            var assemblyName = (string)registration["callbackAssembly"]!;
            var assembly = AssemblyLoadContext.All
                .FirstOrDefault(x => x.Assemblies.Select(y => y.FullName).Contains(assemblyName))?
                .Assemblies.FirstOrDefault(x => x.FullName == assemblyName)
                ?? throw new InvalidOperationException($"Assembly '{assemblyName}' not found");
            var type = assembly.GetType((string)registration["callbackClass"]!)
                ?? throw new InvalidOperationException("callback class not found");
            var method = type.GetMethod((string)registration["callbackMethod"]!)
                ?? throw new InvalidOperationException("callback method not found");
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                throw new InvalidOperationException("Callback method has no parameters");
            }

            var argument = new JObject { { "contents", contents } }.ToObject(parameters[0].ParameterType);
            return method.Invoke(null, [argument]) as string;
        }
    }
}
