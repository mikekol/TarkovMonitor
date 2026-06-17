using System.Text.Json.Serialization;

namespace TarkovMonitor;

/// <summary>
/// Source-generated JSON serialization context for all types that cross the gRPC boundary or are
/// deserialized from EFT log lines.  Required for Native AOT: the runtime code-gen path
/// (<c>JsonSerializer.Deserialize&lt;T&gt;(string)</c> without a type-info argument) uses reflection
/// and is not AOT-safe.
/// </summary>
/// <remarks>
/// If you add a new type that is serialized/deserialized in GameWatcher or GameEventBroadcasterService,
/// add a corresponding <c>[JsonSerializable]</c> attribute here so the source generator emits the
/// necessary metadata at build time.
/// </remarks>
[JsonSerializable(typeof(GroupLogContent))]
[JsonSerializable(typeof(GroupMatchUserLeaveLogContent))]
[JsonSerializable(typeof(GroupRaidSettingsLogContent))]
[JsonSerializable(typeof(GroupMatchRaidReadyLogContent))]
[JsonSerializable(typeof(ChatMessageLogContent))]
[JsonSerializable(typeof(SystemChatMessageLogContent))]
[JsonSerializable(typeof(FleaSoldMessageLogContent))]
[JsonSerializable(typeof(FleaExpiredMessageLogContent))]
[JsonSerializable(typeof(TaskStatusMessageLogContent))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class CoreJsonContext : JsonSerializerContext { }
