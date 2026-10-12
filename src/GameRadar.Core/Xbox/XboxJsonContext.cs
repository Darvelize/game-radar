using System.Text.Json.Serialization;

namespace GameRadar.Core.Xbox;

/// <summary>
/// Every type Microsoft sign-in and Xbox Live JSON is read into or written
/// from, generated at build time: no reflection, so the exe can be trimmed.
/// Names stay the explicit [JsonPropertyName] of the DTOs: Xbox Live compares
/// them case-sensitively ("RelyingParty"). Reading is case-insensitive and
/// accepts numbers sent as strings ("42"): unofficial APIs are not consistent.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
// The saved session (encrypted by SecretStore)
[JsonSerializable(typeof(SavedXboxAccount))]
[JsonSerializable(typeof(TitleHistoryResponse))]
[JsonSerializable(typeof(MicrosoftTokenResponse))]
[JsonSerializable(typeof(XboxUserTokenRequest))]
[JsonSerializable(typeof(XstsTokenRequest))]
[JsonSerializable(typeof(XboxTokenResponse))]
[JsonSerializable(typeof(XstsErrorResponse))]
internal sealed partial class XboxJsonContext : JsonSerializerContext;
