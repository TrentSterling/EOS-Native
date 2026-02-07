using System;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace EOSNative.CodeGen
{
    /// <summary>
    /// Resolves and caches Cecil TypeReference/MethodReference for all types
    /// the weaver needs to emit in generated IL code.
    /// </summary>
    internal sealed class WeaverTypes
    {
        // Core types
        public TypeReference VoidType;
        public TypeReference ByteArrayType;
        public TypeReference IntType;
        public TypeReference UIntType;
        public TypeReference ByteType;

        // EOSNative types
        public TypeReference NetworkManagerType;
        public TypeReference NetworkObjectType;
        public TypeReference NetworkBehaviourType;
        public TypeReference NetWriterType;
        public TypeReference NetReaderType;
        public TypeReference NetWriterPoolType;
        public TypeReference NetSerializersType;
        public TypeReference RPCTargetType;
        public TypeReference NetRpcAttributeType;
        public TypeReference ActionOfNetReaderType;

        // Methods
        public MethodReference NetworkManager_get_Instance;
        public MethodReference NetworkManager_RegisterRPC_Hash;
        public MethodReference NetworkManager_SendRPCWeaved;
        public MethodReference NetworkBehaviour_get_Net;
        public MethodReference NetWriterPool_Get;
        public MethodReference NetWriterPool_Return;
        public MethodReference NetWriter_ToArray;

        // Action<NetReader> constructor
        public MethodReference ActionOfNetReader_Ctor;

        private readonly ModuleDefinition _module;
        private TypeDefinition _netSerializersDef;

        public bool IsValid { get; private set; }
        public string Error { get; private set; }

        public WeaverTypes(ModuleDefinition module)
        {
            _module = module;
            try
            {
                ResolveAll();
                IsValid = true;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                IsValid = false;
            }
        }

        private void ResolveAll()
        {
            // Core types from corlib
            VoidType = _module.TypeSystem.Void;
            IntType = _module.TypeSystem.Int32;
            UIntType = _module.TypeSystem.UInt32;
            ByteType = _module.TypeSystem.Byte;
            ByteArrayType = new ArrayType(_module.TypeSystem.Byte);

            // Find EOSNative assembly reference
            var eosNativeAsm = FindAssemblyByTypeName("EOSNative.Net.NetworkManager");
            if (eosNativeAsm == null)
                throw new Exception("Cannot find EOSNative assembly (needed for NetworkManager)");

            // Find P2P assembly types (NetWriter/NetReader are in EOSNative namespace too)
            var p2pAsm = eosNativeAsm; // Same assembly

            // Resolve core types
            NetworkManagerType = ResolveTypeRef(eosNativeAsm, "EOSNative.Net.NetworkManager");
            NetworkObjectType = ResolveTypeRef(eosNativeAsm, "EOSNative.Net.NetworkObject");
            NetworkBehaviourType = ResolveTypeRef(eosNativeAsm, "EOSNative.Net.NetworkBehaviour");
            NetWriterType = ResolveTypeRef(p2pAsm, "EOSNative.P2P.NetWriter");
            NetReaderType = ResolveTypeRef(p2pAsm, "EOSNative.P2P.NetReader");
            NetWriterPoolType = ResolveTypeRef(p2pAsm, "EOSNative.P2P.NetWriterPool");
            NetSerializersType = ResolveTypeRef(eosNativeAsm, "EOSNative.Net.NetSerializers");
            RPCTargetType = ResolveTypeRef(eosNativeAsm, "EOSNative.Net.RPCTarget");
            NetRpcAttributeType = ResolveTypeRef(eosNativeAsm, "EOSNative.Net.NetRpcAttribute");

            // Resolve the underlying TypeDefinition for NetSerializers (needed for generic method resolution)
            _netSerializersDef = ResolveTypeDef(eosNativeAsm, "EOSNative.Net.NetSerializers");

            // Resolve methods
            var nmDef = ResolveTypeDef(eosNativeAsm, "EOSNative.Net.NetworkManager");

            // NetworkManager.Instance getter
            NetworkManager_get_Instance = _module.ImportReference(
                FindProperty(nmDef, "Instance").GetMethod);

            // NetworkManager.RegisterRPC(NetworkObject, uint, string, Action<NetReader>)
            NetworkManager_RegisterRPC_Hash = _module.ImportReference(
                FindMethod(nmDef, "RegisterRPC", 4,
                    "EOSNative.Net.NetworkObject",
                    "System.UInt32",
                    "System.String",
                    "System.Action`1<EOSNative.P2P.NetReader>"));

            // NetworkManager.SendRPCWeaved(NetworkObject, uint, RPCTarget, byte[])
            NetworkManager_SendRPCWeaved = _module.ImportReference(
                FindMethod(nmDef, "SendRPCWeaved", 4,
                    "EOSNative.Net.NetworkObject",
                    "System.UInt32",
                    "EOSNative.Net.RPCTarget",
                    "System.Byte[]"));

            // NetworkBehaviour.Net getter
            var nbDef = ResolveTypeDef(eosNativeAsm, "EOSNative.Net.NetworkBehaviour");
            NetworkBehaviour_get_Net = _module.ImportReference(
                FindProperty(nbDef, "Net").GetMethod);

            // NetWriterPool.Get/Return
            var poolDef = ResolveTypeDef(p2pAsm, "EOSNative.P2P.NetWriterPool");
            NetWriterPool_Get = _module.ImportReference(FindMethod(poolDef, "Get", 0));
            NetWriterPool_Return = _module.ImportReference(FindMethod(poolDef, "Return", 1));

            // NetWriter.ToArray
            var writerDef = ResolveTypeDef(p2pAsm, "EOSNative.P2P.NetWriter");
            NetWriter_ToArray = _module.ImportReference(FindMethod(writerDef, "ToArray", 0));

            // Action<NetReader> constructor
            var actionOpenType = _module.ImportReference(typeof(Action<>));
            var actionClosed = new GenericInstanceType(actionOpenType);
            actionClosed.GenericArguments.Add(NetReaderType);
            ActionOfNetReaderType = actionClosed;

            // Find Action<T> .ctor(object, IntPtr)
            var actionTypeDef = actionOpenType.Resolve();
            var actionCtor = FindMethod(actionTypeDef, ".ctor", 2);
            ActionOfNetReader_Ctor = _module.ImportReference(actionCtor);
            ActionOfNetReader_Ctor.DeclaringType = actionClosed;
        }

        /// <summary>
        /// Get the imported MethodReference for NetSerializers.Write&lt;T&gt;(NetWriter, T).
        /// </summary>
        public MethodReference GetSerializerWrite(TypeReference paramType)
        {
            var openMethod = FindMethod(_netSerializersDef, "Write", 2);
            var importedOpen = _module.ImportReference(openMethod);
            var genericInstance = new GenericInstanceMethod(importedOpen);
            genericInstance.GenericArguments.Add(_module.ImportReference(paramType));
            return genericInstance;
        }

        /// <summary>
        /// Get the imported MethodReference for NetSerializers.Read&lt;T&gt;(NetReader).
        /// </summary>
        public MethodReference GetSerializerRead(TypeReference paramType)
        {
            var openMethod = FindMethod(_netSerializersDef, "Read", 1);
            var importedOpen = _module.ImportReference(openMethod);
            var genericInstance = new GenericInstanceMethod(importedOpen);
            genericInstance.GenericArguments.Add(_module.ImportReference(paramType));
            return genericInstance;
        }

        #region Resolution Helpers

        private AssemblyDefinition FindAssemblyByTypeName(string fullTypeName)
        {
            // First check the module itself
            foreach (var type in _module.Types)
            {
                if (type.FullName == fullTypeName)
                    return _module.Assembly;
            }

            // Then check referenced assemblies
            foreach (var asmRef in _module.AssemblyReferences)
            {
                try
                {
                    var asm = _module.AssemblyResolver.Resolve(asmRef);
                    if (asm == null) continue;
                    foreach (var type in asm.MainModule.Types)
                    {
                        if (type.FullName == fullTypeName)
                            return asm;
                    }
                }
                catch { /* skip unresolvable assemblies */ }
            }

            return null;
        }

        private TypeReference ResolveTypeRef(AssemblyDefinition asm, string fullName)
        {
            var def = ResolveTypeDef(asm, fullName);
            return _module.ImportReference(def);
        }

        private TypeDefinition ResolveTypeDef(AssemblyDefinition asm, string fullName)
        {
            foreach (var type in asm.MainModule.GetAllTypes())
            {
                if (type.FullName == fullName)
                    return type;
            }
            throw new Exception($"Cannot resolve type '{fullName}' in assembly '{asm.Name.Name}'");
        }

        private static PropertyDefinition FindProperty(TypeDefinition type, string name)
        {
            foreach (var prop in type.Properties)
            {
                if (prop.Name == name) return prop;
            }
            throw new Exception($"Cannot find property '{name}' on type '{type.FullName}'");
        }

        private static MethodDefinition FindMethod(TypeDefinition type, string name, int paramCount)
        {
            foreach (var method in type.Methods)
            {
                if (method.Name == name && method.Parameters.Count == paramCount)
                    return method;
            }
            throw new Exception($"Cannot find method '{name}' with {paramCount} params on type '{type.FullName}'");
        }

        private static MethodDefinition FindMethod(TypeDefinition type, string name, int paramCount,
            params string[] paramTypeNames)
        {
            foreach (var method in type.Methods)
            {
                if (method.Name != name || method.Parameters.Count != paramCount) continue;
                bool match = true;
                for (int i = 0; i < paramCount; i++)
                {
                    if (!MatchesTypeName(method.Parameters[i].ParameterType, paramTypeNames[i]))
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return method;
            }
            throw new Exception($"Cannot find method '{name}' with matching param types on type '{type.FullName}'");
        }

        private static bool MatchesTypeName(TypeReference type, string expectedName)
        {
            // Handle generics like Action`1<NetReader>
            if (expectedName.Contains('`'))
            {
                string baseName = expectedName.Substring(0, expectedName.IndexOf('<'));
                return type.FullName.StartsWith(baseName);
            }
            return type.FullName == expectedName;
        }

        #endregion
    }
}
