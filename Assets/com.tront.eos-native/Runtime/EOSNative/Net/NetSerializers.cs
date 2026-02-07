using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Implement on custom types to enable automatic serialization via <see cref="NetSerializers"/>.
    /// </summary>
    public interface INetSerializable
    {
        void Serialize(NetWriter writer);
        void Deserialize(NetReader reader);
    }

    /// <summary>
    /// Static type serializer registry. Provides Write/Read for common types and
    /// allows registration of custom serializers. Used by SyncVar and RPC systems.
    /// </summary>
    public static class NetSerializers
    {
        // Per-type serializer cache via generic static class (resolved once per T)
        private static class Serializer<T>
        {
            public static Action<NetWriter, T> Write;
            public static Func<NetReader, T> Read;
            public static byte TypeId;
        }

        private static readonly Dictionary<Type, byte> _typeIdMap = new();
        private static byte _nextTypeId;
        private static bool _initialized;

        /// <summary>Write a value of type T to the writer.</summary>
        public static void Write<T>(NetWriter writer, T value)
        {
            EnsureInitialized();
            if (Serializer<T>.Write != null)
            {
                Serializer<T>.Write(writer, value);
                return;
            }

            // Fallback: INetSerializable
            if (value is INetSerializable serializable)
            {
                serializable.Serialize(writer);
                return;
            }

            throw new InvalidOperationException(
                $"NetSerializers: No serializer registered for type {typeof(T).Name}. " +
                $"Call NetSerializers.Register<{typeof(T).Name}>() or implement INetSerializable.");
        }

        /// <summary>Read a value of type T from the reader.</summary>
        public static T Read<T>(NetReader reader)
        {
            EnsureInitialized();
            if (Serializer<T>.Read != null)
                return Serializer<T>.Read(reader);

            // Fallback: INetSerializable
            if (typeof(INetSerializable).IsAssignableFrom(typeof(T)))
            {
                T instance = Activator.CreateInstance<T>();
                ((INetSerializable)instance).Deserialize(reader);
                return instance;
            }

            throw new InvalidOperationException(
                $"NetSerializers: No deserializer registered for type {typeof(T).Name}. " +
                $"Call NetSerializers.Register<{typeof(T).Name}>() or implement INetSerializable.");
        }

        /// <summary>Register a custom serializer for type T.</summary>
        public static void Register<T>(Action<NetWriter, T> write, Func<NetReader, T> read)
        {
            EnsureInitialized();
            Serializer<T>.Write = write;
            Serializer<T>.Read = read;
            Serializer<T>.TypeId = _nextTypeId;
            _typeIdMap[typeof(T)] = _nextTypeId;
            _nextTypeId++;
        }

        /// <summary>Get the type ID for a registered type. Returns false if unregistered.</summary>
        public static bool TryGetTypeId<T>(out byte typeId)
        {
            EnsureInitialized();
            typeId = Serializer<T>.TypeId;
            return Serializer<T>.Write != null;
        }

        /// <summary>Get the type ID for a registered type by Type object.</summary>
        public static bool TryGetTypeId(Type type, out byte typeId)
        {
            EnsureInitialized();
            return _typeIdMap.TryGetValue(type, out typeId);
        }

        /// <summary>Write a value by runtime Type and boxed value. Used by RPC arg serialization.</summary>
        public static void WriteBoxed(NetWriter writer, Type type, object value)
        {
            EnsureInitialized();
            if (!_typeWriters.TryGetValue(type, out var write))
                throw new InvalidOperationException($"NetSerializers: No serializer for type {type.Name}");
            write(writer, value);
        }

        /// <summary>Read a value by type ID. Used by RPC arg deserialization.</summary>
        public static object ReadBoxed(NetReader reader, byte typeId)
        {
            EnsureInitialized();
            if (typeId >= _typeReaders.Count)
                throw new InvalidOperationException($"NetSerializers: Unknown type ID {typeId}");
            return _typeReaders[typeId](reader);
        }

        // Boxed readers/writers for RPC support
        private static readonly Dictionary<Type, Action<NetWriter, object>> _typeWriters = new();
        private static readonly List<Func<NetReader, object>> _typeReaders = new();

        private static void RegisterBuiltin<T>(Action<NetWriter, T> write, Func<NetReader, T> read)
        {
            byte id = _nextTypeId;
            Serializer<T>.Write = write;
            Serializer<T>.Read = read;
            Serializer<T>.TypeId = id;
            _typeIdMap[typeof(T)] = id;
            _typeWriters[typeof(T)] = (w, v) => write(w, (T)v);
            _typeReaders.Add(r => read(r));
            _nextTypeId++;
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            RegisterBuiltinTypes();
        }

        private static void RegisterBuiltinTypes()
        {
            // Primitives (IDs 0-9)
            RegisterBuiltin<byte>((w, v) => w.WriteByte(v), r => r.ReadByte());           // 0
            RegisterBuiltin<bool>((w, v) => w.WriteBool(v), r => r.ReadBool());           // 1
            RegisterBuiltin<short>((w, v) => w.WriteInt16(v), r => r.ReadInt16());        // 2
            RegisterBuiltin<ushort>((w, v) => w.WriteUInt16(v), r => r.ReadUInt16());     // 3
            RegisterBuiltin<int>((w, v) => w.WriteInt32(v), r => r.ReadInt32());          // 4
            RegisterBuiltin<uint>((w, v) => w.WriteUInt32(v), r => r.ReadUInt32());       // 5
            RegisterBuiltin<long>((w, v) => w.WriteInt64(v), r => r.ReadInt64());         // 6
            RegisterBuiltin<ulong>((w, v) => w.WriteUInt64(v), r => r.ReadUInt64());      // 7
            RegisterBuiltin<float>((w, v) => w.WriteFloat(v), r => r.ReadFloat());        // 8
            RegisterBuiltin<double>((w, v) => w.WriteDouble(v), r => r.ReadDouble());     // 9

            // String (ID 10)
            RegisterBuiltin<string>((w, v) => w.WriteString(v ?? string.Empty), r => r.ReadString()); // 10

            // Unity types (IDs 11-16)
            RegisterBuiltin<Vector2>((w, v) => w.WriteVector2(v), r => r.ReadVector2());              // 11
            RegisterBuiltin<Vector3>((w, v) => w.WriteVector3(v), r => r.ReadVector3());              // 12
            RegisterBuiltin<Quaternion>((w, v) => w.WriteQuaternion(v), r => r.ReadQuaternion());     // 13
            RegisterBuiltin<Color>((w, v) => w.WriteColor(v), r => r.ReadColor());                    // 14
            RegisterBuiltin<Color32>((w, v) => w.WriteColor32(v), r => r.ReadColor32());              // 15

            // EOS types (ID 16)
            RegisterBuiltin<ProductUserId>((w, v) => w.WriteProductUserId(v), r => r.ReadProductUserId()); // 16

            // Byte array (ID 17)
            RegisterBuiltin<byte[]>((w, v) => w.WriteBytes(v), r => r.ReadBytes());                   // 17

            // NetworkObject reference — serialized as NetworkId (uint), resolved on receive (ID 18)
            RegisterBuiltin<NetworkObject>(
                (w, v) => w.WriteUInt32(v != null ? v.NetworkId : 0u),
                r =>
                {
                    uint netId = r.ReadUInt32();
                    if (netId == 0) return null;
                    NetworkManager.Instance.Objects.TryGetValue(netId, out var obj);
                    return obj;
                }
            );  // 18
        }
    }
}
