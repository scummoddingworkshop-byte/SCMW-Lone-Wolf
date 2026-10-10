// src/SCMW.Core/Content/ContentSourceStatus.cs
// Lifecycle state of the content source (the real SCUM pak reader or a fallback),
// surfaced to the UI so it can render "configure path / enter key / scanning / ready".
namespace SCMW.Core.Content;

public enum ContentSourceState { NotConfigured, NoKey, Scanning, Ready, Error }

public sealed record ContentSourceStatus(ContentSourceState State, string Message, string? GameBuildId);
