using System.Text.Json.Serialization;

namespace KyzWheel.Core.Config;

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppConfig))]
internal sealed partial class ConfigJson : JsonSerializerContext;
