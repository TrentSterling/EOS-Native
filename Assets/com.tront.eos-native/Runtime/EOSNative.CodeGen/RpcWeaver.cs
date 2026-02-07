using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace EOSNative.CodeGen
{
    /// <summary>
    /// Core RPC weaver. For each [NetRpc] method on a NetworkBehaviour subclass:
    ///
    /// 1. Moves original body → UserCode_MethodName
    /// 2. Replaces original → dispatch stub (serialize args, call SendRPCWeaved)
    /// 3. Creates __InvokeNetRpc_MethodName → deserialize args, call UserCode_
    /// 4. Generates/extends __RegisterNetRPCs → register invoke handlers
    ///
    /// Uses the same FNV-1a hash as NetworkManager.FnvHash for method name hashing.
    /// </summary>
    internal sealed class RpcWeaver
    {
        private readonly ModuleDefinition _module;
        private readonly WeaverTypes _types;
        private readonly List<DiagnosticMessage> _diagnostics;

        // Supported parameter types for [NetRpc] methods (must match NetSerializers builtins)
        private static readonly HashSet<string> SupportedTypes = new()
        {
            "System.Byte",
            "System.Boolean",
            "System.Int16",
            "System.UInt16",
            "System.Int32",
            "System.UInt32",
            "System.Int64",
            "System.UInt64",
            "System.Single",
            "System.Double",
            "System.String",
            "UnityEngine.Vector2",
            "UnityEngine.Vector3",
            "UnityEngine.Quaternion",
            "UnityEngine.Color",
            "UnityEngine.Color32",
            "Epic.OnlineServices.ProductUserId",
            "System.Byte[]",
            "EOSNative.Net.NetworkObject",
        };

        public RpcWeaver(ModuleDefinition module, WeaverTypes types, List<DiagnosticMessage> diagnostics)
        {
            _module = module;
            _types = types;
            _diagnostics = diagnostics;
        }

        /// <summary>
        /// Process all types in the module. Returns true if any modifications were made.
        /// </summary>
        public bool Process()
        {
            bool modified = false;

            foreach (var type in _module.GetAllTypes().ToList())
            {
                if (!IsNetworkBehaviour(type)) continue;

                var rpcMethods = FindRpcMethods(type);
                if (rpcMethods.Count == 0) continue;

                var invokeHandlers = new List<(uint hash, string name, MethodDefinition invoker)>();

                foreach (var rpcMethod in rpcMethods)
                {
                    // Already weaved guard
                    if (type.Methods.Any(m => m.Name == "UserCode_" + rpcMethod.Name))
                        continue;

                    // Validate
                    if (!ValidateRpcMethod(rpcMethod, type))
                        continue;

                    // Read attribute
                    var attr = GetNetRpcAttribute(rpcMethod);
                    int target = 0; // RPCTarget.All
                    if (attr.HasConstructorArguments && attr.ConstructorArguments.Count > 0)
                        target = (int)attr.ConstructorArguments[0].Value;

                    uint methodHash = FnvHash(rpcMethod.Name);

                    // 1. Create UserCode_ method (move original body)
                    var userCode = CreateUserCodeMethod(type, rpcMethod);
                    type.Methods.Add(userCode);

                    // 2. Create __InvokeNetRpc_ method (deserialize + call UserCode_)
                    var invoker = CreateInvokerMethod(type, rpcMethod, userCode);
                    type.Methods.Add(invoker);
                    invokeHandlers.Add((methodHash, rpcMethod.Name, invoker));

                    // 3. Rewrite original method body (dispatch stub)
                    RewriteAsDispatchStub(rpcMethod, methodHash, target);

                    modified = true;
                }

                if (invokeHandlers.Count > 0)
                {
                    // 4. Generate/extend __RegisterNetRPCs
                    GenerateRegisterMethod(type, invokeHandlers);
                }
            }

            return modified;
        }

        #region Discovery & Validation

        private bool IsNetworkBehaviour(TypeDefinition type)
        {
            if (type == null || !type.IsClass || type.IsAbstract) return false;

            var baseType = type.BaseType;
            while (baseType != null)
            {
                if (baseType.FullName == "EOSNative.Net.NetworkBehaviour")
                    return true;

                try
                {
                    var resolved = baseType.Resolve();
                    baseType = resolved?.BaseType;
                }
                catch
                {
                    break;
                }
            }
            return false;
        }

        private List<MethodDefinition> FindRpcMethods(TypeDefinition type)
        {
            var result = new List<MethodDefinition>();
            foreach (var method in type.Methods)
            {
                if (GetNetRpcAttribute(method) != null)
                    result.Add(method);
            }
            return result;
        }

        private CustomAttribute GetNetRpcAttribute(MethodDefinition method)
        {
            foreach (var attr in method.CustomAttributes)
            {
                if (attr.AttributeType.FullName == "EOSNative.Net.NetRpcAttribute")
                    return attr;
            }
            return null;
        }

        private bool ValidateRpcMethod(MethodDefinition method, TypeDefinition type)
        {
            // Must return void
            if (method.ReturnType.FullName != "System.Void")
            {
                AddError(method, $"[NetRpc] method '{method.Name}' on '{type.Name}' must return void.");
                return false;
            }

            // No generics
            if (method.HasGenericParameters)
            {
                AddError(method, $"[NetRpc] method '{method.Name}' on '{type.Name}' cannot be generic.");
                return false;
            }

            // No abstract
            if (method.IsAbstract)
            {
                AddError(method, $"[NetRpc] method '{method.Name}' on '{type.Name}' cannot be abstract.");
                return false;
            }

            // No ref/out parameters
            foreach (var param in method.Parameters)
            {
                if (param.ParameterType.IsByReference)
                {
                    AddError(method, $"[NetRpc] method '{method.Name}' on '{type.Name}': parameter '{param.Name}' cannot be ref/out.");
                    return false;
                }

                // Check param type is supported
                if (!IsSerializableType(param.ParameterType))
                {
                    AddError(method, $"[NetRpc] method '{method.Name}' on '{type.Name}': parameter type '{param.ParameterType.FullName}' is not supported. " +
                        "Supported: byte, bool, short, ushort, int, uint, long, ulong, float, double, string, Vector2, Vector3, Quaternion, Color, Color32, ProductUserId, byte[], NetworkObject, or INetSerializable.");
                    return false;
                }
            }

            return true;
        }

        private bool IsSerializableType(TypeReference type)
        {
            if (SupportedTypes.Contains(type.FullName))
                return true;

            // Check for INetSerializable
            try
            {
                var resolved = type.Resolve();
                if (resolved != null)
                {
                    foreach (var iface in resolved.Interfaces)
                    {
                        if (iface.InterfaceType.FullName == "EOSNative.Net.INetSerializable")
                            return true;
                    }
                }
            }
            catch { }

            return false;
        }

        #endregion

        #region Code Generation

        /// <summary>
        /// Create UserCode_MethodName — a copy of the original method body.
        /// </summary>
        private MethodDefinition CreateUserCodeMethod(TypeDefinition type, MethodDefinition original)
        {
            var userCode = new MethodDefinition(
                "UserCode_" + original.Name,
                MethodAttributes.Private | MethodAttributes.HideBySig,
                original.ReturnType);

            // Copy parameters
            foreach (var param in original.Parameters)
                userCode.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes, param.ParameterType));

            // Copy body
            var oldBody = original.Body;
            var newBody = userCode.Body;
            newBody.InitLocals = oldBody.InitLocals;
            newBody.MaxStackSize = oldBody.MaxStackSize;

            // Copy local variables
            foreach (var local in oldBody.Variables)
                newBody.Variables.Add(new VariableDefinition(local.VariableType));

            // Copy instructions
            var il = newBody.GetILProcessor();
            var instrMap = new Dictionary<Instruction, Instruction>();

            foreach (var instr in oldBody.Instructions)
            {
                var newInstr = CloneInstruction(instr);
                instrMap[instr] = newInstr;
                il.Append(newInstr);
            }

            // Fix branch targets
            foreach (var instr in newBody.Instructions)
            {
                if (instr.Operand is Instruction target && instrMap.TryGetValue(target, out var newTarget))
                    instr.Operand = newTarget;
                else if (instr.Operand is Instruction[] targets)
                {
                    var newTargets = new Instruction[targets.Length];
                    for (int i = 0; i < targets.Length; i++)
                        newTargets[i] = instrMap.TryGetValue(targets[i], out var nt) ? nt : targets[i];
                    instr.Operand = newTargets;
                }
            }

            // Copy exception handlers
            foreach (var handler in oldBody.ExceptionHandlers)
            {
                var newHandler = new ExceptionHandler(handler.HandlerType)
                {
                    CatchType = handler.CatchType,
                    TryStart = instrMap.TryGetValue(handler.TryStart, out var ts) ? ts : null,
                    TryEnd = instrMap.TryGetValue(handler.TryEnd, out var te) ? te : null,
                    HandlerStart = instrMap.TryGetValue(handler.HandlerStart, out var hs) ? hs : null,
                    HandlerEnd = instrMap.TryGetValue(handler.HandlerEnd, out var he) ? he : null,
                };
                if (handler.FilterStart != null && instrMap.TryGetValue(handler.FilterStart, out var fs))
                    newHandler.FilterStart = fs;
                newBody.ExceptionHandlers.Add(newHandler);
            }

            return userCode;
        }

        /// <summary>
        /// Rewrite the original method body to be a dispatch stub that serializes args
        /// and calls NetworkManager.Instance.SendRPCWeaved(Net, hash, target, argData).
        /// </summary>
        private void RewriteAsDispatchStub(MethodDefinition method, uint methodHash, int rpcTarget)
        {
            method.Body.Instructions.Clear();
            method.Body.Variables.Clear();
            method.Body.ExceptionHandlers.Clear();
            method.Body.InitLocals = true;

            var il = method.Body.GetILProcessor();

            // var writer = NetWriterPool.Get();
            var writerVar = new VariableDefinition(_types.NetWriterType);
            method.Body.Variables.Add(writerVar);
            il.Emit(OpCodes.Call, _types.NetWriterPool_Get);
            il.Emit(OpCodes.Stloc, writerVar);

            // Serialize each parameter: NetSerializers.Write<ParamType>(writer, arg_N)
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                var param = method.Parameters[i];
                var writeMethod = _types.GetSerializerWrite(param.ParameterType);

                il.Emit(OpCodes.Ldloc, writerVar);
                il.Emit(OpCodes.Ldarg, i + 1); // +1 because arg_0 is 'this'
                il.Emit(OpCodes.Call, writeMethod);
            }

            // byte[] data = writer.ToArray();
            var dataVar = new VariableDefinition(_types.ByteArrayType);
            method.Body.Variables.Add(dataVar);
            il.Emit(OpCodes.Ldloc, writerVar);
            il.Emit(OpCodes.Callvirt, _types.NetWriter_ToArray);
            il.Emit(OpCodes.Stloc, dataVar);

            // NetWriterPool.Return(writer);
            il.Emit(OpCodes.Ldloc, writerVar);
            il.Emit(OpCodes.Call, _types.NetWriterPool_Return);

            // NetworkManager.Instance.SendRPCWeaved(this.Net, hash, target, data);
            il.Emit(OpCodes.Call, _types.NetworkManager_get_Instance);  // NetworkManager.Instance
            il.Emit(OpCodes.Ldarg_0);                                   // this
            il.Emit(OpCodes.Call, _types.NetworkBehaviour_get_Net);      // .Net
            il.Emit(OpCodes.Ldc_I4, unchecked((int)methodHash));        // hash (uint as int literal)
            il.Emit(OpCodes.Ldc_I4, rpcTarget);                        // RPCTarget enum value
            il.Emit(OpCodes.Ldloc, dataVar);                            // argData
            il.Emit(OpCodes.Callvirt, _types.NetworkManager_SendRPCWeaved);

            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Create __InvokeNetRpc_MethodName(NetReader reader) that deserializes args
        /// and calls UserCode_MethodName(arg0, arg1, ...).
        /// </summary>
        private MethodDefinition CreateInvokerMethod(TypeDefinition type, MethodDefinition original, MethodDefinition userCode)
        {
            var invoker = new MethodDefinition(
                "__InvokeNetRpc_" + original.Name,
                MethodAttributes.Private | MethodAttributes.HideBySig,
                _types.VoidType);

            // Single parameter: NetReader
            invoker.Parameters.Add(new ParameterDefinition("reader", ParameterAttributes.None, _types.NetReaderType));

            invoker.Body.InitLocals = true;
            var il = invoker.Body.GetILProcessor();

            // Load 'this' for the UserCode_ call
            il.Emit(OpCodes.Ldarg_0);

            // Deserialize each parameter: var argN = NetSerializers.Read<ParamType>(reader)
            for (int i = 0; i < original.Parameters.Count; i++)
            {
                var param = original.Parameters[i];
                var readMethod = _types.GetSerializerRead(param.ParameterType);

                il.Emit(OpCodes.Ldarg_1); // reader
                il.Emit(OpCodes.Call, readMethod);
            }

            // Call UserCode_MethodName(args...)
            il.Emit(OpCodes.Call, userCode);
            il.Emit(OpCodes.Ret);

            return invoker;
        }

        /// <summary>
        /// Generate or extend __RegisterNetRPCs override to register all invoke handlers.
        ///
        /// internal override void __RegisterNetRPCs()
        /// {
        ///     base.__RegisterNetRPCs();
        ///     NetworkManager.Instance.RegisterRPC(Net, hash, "name", __InvokeNetRpc_Name);
        ///     ...
        /// }
        /// </summary>
        private void GenerateRegisterMethod(TypeDefinition type,
            List<(uint hash, string name, MethodDefinition invoker)> handlers)
        {
            // Remove existing __RegisterNetRPCs if the weaver already generated one
            var existing = type.Methods.FirstOrDefault(m => m.Name == "__RegisterNetRPCs");
            if (existing != null)
                type.Methods.Remove(existing);

            var method = new MethodDefinition(
                "__RegisterNetRPCs",
                MethodAttributes.Assembly | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                _types.VoidType);

            method.Body.InitLocals = true;
            var il = method.Body.GetILProcessor();

            // base.__RegisterNetRPCs();
            var baseType = type.BaseType.Resolve();
            MethodReference baseRegister = null;
            while (baseType != null)
            {
                var baseMethod = baseType.Methods.FirstOrDefault(m => m.Name == "__RegisterNetRPCs");
                if (baseMethod != null)
                {
                    baseRegister = _module.ImportReference(baseMethod);
                    break;
                }
                try { baseType = baseType.BaseType?.Resolve(); }
                catch { break; }
            }

            if (baseRegister != null)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, baseRegister);
            }

            // For each RPC: NetworkManager.Instance.RegisterRPC(Net, hash, "name", __InvokeNetRpc_Name)
            foreach (var (hash, name, invoker) in handlers)
            {
                // NetworkManager.Instance
                il.Emit(OpCodes.Call, _types.NetworkManager_get_Instance);
                // this.Net
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, _types.NetworkBehaviour_get_Net);
                // hash
                il.Emit(OpCodes.Ldc_I4, unchecked((int)hash));
                // "name"
                il.Emit(OpCodes.Ldstr, name);
                // new Action<NetReader>(this, __InvokeNetRpc_Name)
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldftn, invoker);
                il.Emit(OpCodes.Newobj, _types.ActionOfNetReader_Ctor);
                // RegisterRPC(NetworkObject, uint, string, Action<NetReader>)
                il.Emit(OpCodes.Callvirt, _types.NetworkManager_RegisterRPC_Hash);
            }

            il.Emit(OpCodes.Ret);
            type.Methods.Add(method);
        }

        #endregion

        #region Utilities

        private static uint FnvHash(string str)
        {
            if (string.IsNullOrEmpty(str)) return 0;
            uint hash = 2166136261u;
            for (int i = 0; i < str.Length; i++)
            {
                hash ^= str[i];
                hash *= 16777619u;
            }
            return hash;
        }

        private static Instruction CloneInstruction(Instruction instr)
        {
            if (instr.Operand == null)
                return Instruction.Create(instr.OpCode);
            if (instr.Operand is byte b)
                return Instruction.Create(instr.OpCode, b);
            if (instr.Operand is sbyte sb)
                return Instruction.Create(instr.OpCode, sb);
            if (instr.Operand is int i)
                return Instruction.Create(instr.OpCode, i);
            if (instr.Operand is long l)
                return Instruction.Create(instr.OpCode, l);
            if (instr.Operand is float f)
                return Instruction.Create(instr.OpCode, f);
            if (instr.Operand is double d)
                return Instruction.Create(instr.OpCode, d);
            if (instr.Operand is string s)
                return Instruction.Create(instr.OpCode, s);
            if (instr.Operand is TypeReference type)
                return Instruction.Create(instr.OpCode, type);
            if (instr.Operand is MethodReference method)
                return Instruction.Create(instr.OpCode, method);
            if (instr.Operand is FieldReference field)
                return Instruction.Create(instr.OpCode, field);
            if (instr.Operand is Instruction target)
                return Instruction.Create(instr.OpCode, target); // will be re-mapped
            if (instr.Operand is Instruction[] targets)
                return Instruction.Create(instr.OpCode, targets); // will be re-mapped
            if (instr.Operand is VariableDefinition variable)
                return Instruction.Create(instr.OpCode, variable);
            if (instr.Operand is ParameterDefinition parameter)
                return Instruction.Create(instr.OpCode, parameter);
            if (instr.Operand is CallSite callSite)
                return Instruction.Create(instr.OpCode, callSite);

            // Fallback: create without operand (shouldn't happen with valid IL)
            return Instruction.Create(instr.OpCode);
        }

        private void AddError(MethodDefinition method, string message)
        {
            _diagnostics.Add(new DiagnosticMessage
            {
                DiagnosticType = DiagnosticType.Error,
                MessageData = message,
            });
        }

        #endregion
    }
}
