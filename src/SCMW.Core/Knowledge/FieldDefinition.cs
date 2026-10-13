// src/SCMW.Core/Knowledge/FieldDefinition.cs
// What a field IS, as plain facts: the technical name, the owner-facing label, the
// value type, the unit shown beside it, how risky a wrong value is, whether we can
// write it at all, and a one-line plain meaning. This is data only - no rules live here.
namespace SCMW.Core.Knowledge;

/// <summary>The kind of value a field holds. <see cref="Unknown"/> is the honest default.</summary>
public enum FieldType { Unknown, Number, Boolean, Text, Choice, Reference }

/// <summary>How much a wrong value can hurt, shown as a dot: a measure for advice, not a padlock.
/// <see cref="Unknown"/> means we have not worked it out yet.</summary>
public enum Risk { Unknown, Low, Medium, High }

/// <summary>One field of one kind of asset, explained. <paramref name="Name"/> is the technical
/// property name; <paramref name="Label"/> is what the owner reads.</summary>
public sealed record FieldDefinition(
    string Name,
    string Label,
    FieldType Type,
    string Unit,
    Risk Risk,
    bool Writable,
    string Meaning);
