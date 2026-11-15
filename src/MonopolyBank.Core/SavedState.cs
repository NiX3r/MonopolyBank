using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonopolyBank.Core;

/// <summary>Everything persisted on the device (localStorage). Never leaves the device.</summary>
public sealed class SavedState
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public GameMode? Mode { get; set; }
    public SharedGame? Shared { get; set; }
    public WalletGame? Wallet { get; set; }

    [JsonIgnore] public bool HasGame => Mode switch
    {
        GameMode.Shared => Shared is not null,
        GameMode.Wallet => Wallet is not null,
        _ => false,
    };

    public string ToJson() => JsonSerializer.Serialize(this, StateJson.Default.SavedState);

    public static SavedState FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new SavedState();
        try
        {
            var state = JsonSerializer.Deserialize(json, StateJson.Default.SavedState) ?? new SavedState();
            return state.Version > CurrentVersion ? new SavedState() : state;
        }
        catch (JsonException)
        {
            return new SavedState();
        }
    }
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SavedState))]
internal partial class StateJson : JsonSerializerContext;
