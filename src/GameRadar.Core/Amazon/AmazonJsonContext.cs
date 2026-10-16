using System.Text.Json.Serialization;

namespace GameRadar.Core.Amazon;

/// <summary>
/// Every type Amazon's JSON is read into or written from, generated at build
/// time: no reflection, so the exe can be trimmed.
/// </summary>
[JsonSerializable(typeof(GraphQlRequest))]
[JsonSerializable(typeof(GraphQlEnvelope))]
internal sealed partial class AmazonJsonContext : JsonSerializerContext;
