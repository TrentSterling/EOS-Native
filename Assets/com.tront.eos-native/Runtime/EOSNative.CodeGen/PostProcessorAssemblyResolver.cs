using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace EOSNative.CodeGen
{
    /// <summary>
    /// Custom IAssemblyResolver for the ILPostProcessor.
    /// Searches the compiled assembly references and Library/ScriptAssemblies/ for type resolution.
    /// Caches resolved assemblies to avoid redundant I/O.
    /// </summary>
    internal sealed class PostProcessorAssemblyResolver : IAssemblyResolver
    {
        private readonly Dictionary<string, AssemblyDefinition> _cache = new();
        private readonly ICompiledAssembly _compiledAssembly;
        private AssemblyDefinition _selfAssembly;

        public PostProcessorAssemblyResolver(ICompiledAssembly compiledAssembly)
        {
            _compiledAssembly = compiledAssembly;
        }

        public void Dispose()
        {
            foreach (var asm in _cache.Values)
                asm.Dispose();
            _cache.Clear();
            _selfAssembly?.Dispose();
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            return Resolve(name, new ReaderParameters(ReadingMode.Deferred));
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            if (name.Name == _compiledAssembly.Name)
                return _selfAssembly;

            if (_cache.TryGetValue(name.Name, out var cached))
                return cached;

            // Search compiled assembly references
            var refPath = _compiledAssembly.References
                .FirstOrDefault(r => Path.GetFileNameWithoutExtension(r) == name.Name);

            if (refPath != null)
            {
                var asm = ReadAssembly(refPath, parameters);
                if (asm != null)
                {
                    _cache[name.Name] = asm;
                    return asm;
                }
            }

            // Fallback: search Library/ScriptAssemblies/
            var scriptAsmPath = FindInScriptAssemblies(name.Name);
            if (scriptAsmPath != null)
            {
                var asm = ReadAssembly(scriptAsmPath, parameters);
                if (asm != null)
                {
                    _cache[name.Name] = asm;
                    return asm;
                }
            }

            return null;
        }

        internal void SetSelfAssembly(AssemblyDefinition self)
        {
            _selfAssembly = self;
        }

        private static AssemblyDefinition ReadAssembly(string path, ReaderParameters parameters)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var bytes = File.ReadAllBytes(path);
                var stream = new MemoryStream(bytes);
                parameters.ReadSymbols = false;
                return AssemblyDefinition.ReadAssembly(stream, parameters);
            }
            catch
            {
                return null;
            }
        }

        private static string FindInScriptAssemblies(string assemblyName)
        {
            // Unity stores compiled assemblies in Library/ScriptAssemblies/
            var candidates = new[]
            {
                Path.Combine("Library", "ScriptAssemblies", assemblyName + ".dll"),
                Path.Combine("Library", "ScriptAssemblies", assemblyName + ".exe"),
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }
    }
}
