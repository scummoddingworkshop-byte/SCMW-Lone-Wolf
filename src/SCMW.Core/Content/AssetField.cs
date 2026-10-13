// src/SCMW.Core/Content/AssetField.cs
// A single readable/editable field of an asset, with the friendly/technical
// name pair, the edit-status classification the UI colors by, and the UE property
// type hint the honesty engine uses to judge write capability.
namespace SCMW.Core.Content;

/// <summary>
/// One field of an asset. <paramref name="TechnicalName"/> is the plain property name for a
/// top-level field, or a dotted path (<c>a.B.c</c>) for a value hidden inside a struct.
/// </summary>
/// <param name="Nested">True for a value read from inside a struct — addressed by a dotted path.</param>
public sealed record AssetField(string FriendlyName, string TechnicalName,
    StatusKind Status, string CurrentValue, string? ValueTypeHint = null, bool Nested = false);
