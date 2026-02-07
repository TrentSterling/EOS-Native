using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace EOSNative.CodeGen
{
    /// <summary>
    /// Unity ILPostProcessor entry point. Scans compiled assemblies for [NetRpc] methods
    /// on NetworkBehaviour subclasses and rewrites them into dispatch stubs + invoke handlers.
    ///
    /// Only processes assemblies that reference EOSNative (skips SDK/Editor/unrelated assemblies).
    /// </summary>
    public class EOSNetRpcPostProcessor : ILPostProcessor
    {
        public override ILPostProcessor GetInstance() => new EOSNetRpcPostProcessor();

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            // Only process assemblies that reference EOSNative
            return compiledAssembly.References.Any(r =>
                Path.GetFileNameWithoutExtension(r) == "EOSNative");
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var diagnostics = new System.Collections.Generic.List<DiagnosticMessage>();

            // Read the assembly via Cecil
            var resolver = new PostProcessorAssemblyResolver(compiledAssembly);
            var readerParams = new ReaderParameters
            {
                AssemblyResolver = resolver,
                ReadSymbols = true,
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                ReadingMode = ReadingMode.Immediate
            };

            var peData = compiledAssembly.InMemoryAssembly.PeData;
            var pdbData = compiledAssembly.InMemoryAssembly.PdbData;
            var asmStream = new MemoryStream(peData);

            AssemblyDefinition assembly;
            try
            {
                if (pdbData != null && pdbData.Length > 0)
                {
                    readerParams.SymbolStream = new MemoryStream(pdbData);
                    assembly = AssemblyDefinition.ReadAssembly(asmStream, readerParams);
                }
                else
                {
                    readerParams.ReadSymbols = false;
                    assembly = AssemblyDefinition.ReadAssembly(asmStream, readerParams);
                }
            }
            catch
            {
                // If we can't read the assembly, skip silently
                resolver.Dispose();
                return null;
            }

            resolver.SetSelfAssembly(assembly);

            // Resolve all type/method references the weaver needs
            var weaverTypes = new WeaverTypes(assembly.MainModule);
            if (!weaverTypes.IsValid)
            {
                // Can't resolve types — likely the EOSNative assembly isn't compiled yet
                // This is normal during first compilation passes
                assembly.Dispose();
                resolver.Dispose();
                return null;
            }

            // Run the weaver
            var weaver = new RpcWeaver(assembly.MainModule, weaverTypes, diagnostics);
            bool modified = weaver.Process();

            if (!modified)
            {
                assembly.Dispose();
                resolver.Dispose();
                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, diagnostics);
            }

            // Write modified assembly back
            var writerParams = new WriterParameters
            {
                WriteSymbols = true,
                SymbolWriterProvider = new PortablePdbWriterProvider()
            };

            var peOut = new MemoryStream();
            var pdbOut = new MemoryStream();
            writerParams.SymbolStream = pdbOut;

            assembly.Write(peOut, writerParams);

            var result = new ILPostProcessResult(
                new InMemoryAssembly(peOut.ToArray(), pdbOut.ToArray()),
                diagnostics);

            assembly.Dispose();
            resolver.Dispose();

            return result;
        }
    }
}
