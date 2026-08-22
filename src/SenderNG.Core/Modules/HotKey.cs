namespace SenderNG.Core.Modules;

/// <summary>A global keyboard shortcut that triggers a module action.</summary>
public record HotKey(string Gesture, string ActionId);
