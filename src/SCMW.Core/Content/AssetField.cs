// src/SCMW.Core/Content/AssetField.cs
// A single readable/editable field of an asset, with the friendly/technical
// name pair, the edit-status classification the UI colors by, and the UE property
// type hint the honesty engine uses to judge write capability.
namespace SCMW.Core.Content;

public sealed record AssetField(string FriendlyName, string TechnicalName,
    StatusKind Status, string CurrentValue, string? ValueTypeHint = null);
