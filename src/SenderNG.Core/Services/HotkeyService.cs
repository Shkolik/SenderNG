/*

Copyright (c) 2026 Andrew Shkolik

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

· Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

· Neither the name of the copyright holder nor the names of its contributors may
be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using CNC.Core.Services;
using SenderNG.Core.Modules;

namespace SenderNG.Core.Services;

/// <summary>One registered action plus its default and effective gesture.</summary>
public sealed class HotkeyBinding
{
    internal HotkeyBinding(IHotkeyAction action, string defaultGesture, string? gesture)
    {
        Action = action;
        DefaultGesture = defaultGesture;
        Gesture = gesture;
    }

    public IHotkeyAction Action { get; }
    public string DefaultGesture { get; }
    public string? Gesture { get; internal set; }
    public string ActionId => Action.ActionId;
}

/// <summary>
/// The hotkey catalog: registered actions, their persisted gestures, scope-aware
/// conflict detection and scope-aware resolution. Framework-agnostic.
/// </summary>
public sealed class HotkeyService
{
    private readonly SettingsService _settings;
    private readonly Dictionary<string, HotkeyBinding> _byId = new(StringComparer.Ordinal);
    private readonly List<HotkeyBinding> _bindings = new();

    public HotkeyService(SettingsService settings)
        => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <summary>Raised after every mutation so the dispatcher and editor stay live.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<HotkeyBinding> Bindings => _bindings;

    public HotkeyBinding? Find(string actionId) => _byId.GetValueOrDefault(actionId);

    /// <summary>Register an action; the first registration for an id wins.</summary>
    public void RegisterAction(IHotkeyAction action, string? defaultGesture)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_byId.ContainsKey(action.ActionId))
            return;

        var fallback = string.IsNullOrWhiteSpace(defaultGesture) ? null : HotkeyGrammar.Normalize(defaultGesture);
        var gesture = HotkeyMapStore.Read(_settings.Global, action.ActionId, fallback, out var mutated);
        if (mutated)
            _settings.SaveGlobal(_settings.Global);

        var binding = new HotkeyBinding(action, fallback ?? string.Empty, gesture);
        _byId[action.ActionId] = binding;
        _bindings.Add(binding);
    }

    public string? GestureFor(string actionId) => _byId.GetValueOrDefault(actionId)?.Gesture;

    /// <summary>The compiled-in default, or null when the action ships unbound.</summary>
    public string? DefaultGestureFor(string actionId)
    {
        var binding = _byId.GetValueOrDefault(actionId);
        return binding is null || binding.DefaultGesture.Length == 0 ? null : binding.DefaultGesture;
    }

    /// <summary>Bind (normalized) or unbind (null/empty) an action.</summary>
    public void SetGesture(string actionId, string? gesture)
    {
        var binding = Require(actionId);
        if (string.IsNullOrWhiteSpace(gesture))
        {
            HotkeyMapStore.Write(_settings.Global, actionId, null);
            binding.Gesture = null;
        }
        else
        {
            if (!HotkeyGrammar.TryNormalize(gesture, out var normalized))
                throw new ArgumentException($"Invalid gesture '{gesture}'", nameof(gesture));
            HotkeyMapStore.Write(_settings.Global, actionId, normalized);
            binding.Gesture = normalized;
        }

        Persist();
    }

    public void ClearGesture(string actionId) => SetGesture(actionId, null);

    /// <summary>Restore an action's compiled-in default (entry removed).</summary>
    public void ResetOne(string actionId)
    {
        var binding = Require(actionId);
        HotkeyMapStore.Remove(_settings.Global, actionId);
        binding.Gesture = binding.DefaultGesture.Length == 0 ? null : binding.DefaultGesture;
        Persist();
    }

    /// <summary>Restore every action's default (user keymap cleared).</summary>
    public void ResetAll()
    {
        HotkeyMapStore.Clear(_settings.Global);
        foreach (var binding in _bindings)
            binding.Gesture = binding.DefaultGesture.Length == 0 ? null : binding.DefaultGesture;
        Persist();
    }

    /// <summary>The other action already bound to <paramref name="gesture"/> in the
    /// same scope, or null. Cross-scope duplicates are legal.</summary>
    public string? FindConflict(string gesture, HotkeyScope scope, string? moduleId, string exceptActionId)
    {
        if (!HotkeyGrammar.TryNormalize(gesture, out var normalized))
            return null;

        foreach (var binding in _bindings)
        {
            if (binding.ActionId == exceptActionId)
                continue;
            if (!string.Equals(binding.Gesture, normalized, StringComparison.Ordinal))
                continue;
            if (binding.Action.Scope == scope
                && string.Equals(binding.Action.ModuleId, moduleId, StringComparison.Ordinal))
                return binding.ActionId;
        }
        return null;
    }

    public IReadOnlyList<HotkeyBinding> CandidatesFor(string gesture)
    {
        if (!HotkeyGrammar.TryNormalize(gesture, out var normalized))
            return [];
        return _bindings.Where(b => string.Equals(b.Gesture, normalized, StringComparison.Ordinal)).ToList();
    }

    /// <summary>First candidate whose scope is satisfied: a focused Module action
    /// wins over a Global one (ioSender's contextual collision resolution).</summary>
    public IHotkeyAction? Resolve(string gesture, Func<string, bool> moduleHasFocus)
    {
        var candidates = CandidatesFor(gesture);
        foreach (var binding in candidates)
            if (binding.Action.Scope == HotkeyScope.Module
                && binding.Action.ModuleId is { } moduleId
                && moduleHasFocus(moduleId))
                return binding.Action;
        foreach (var binding in candidates)
            if (binding.Action.Scope == HotkeyScope.Global)
                return binding.Action;
        return null;
    }

    private HotkeyBinding Require(string actionId)
        => _byId.TryGetValue(actionId, out var binding)
            ? binding
            : throw new ArgumentException($"Unknown hotkey action '{actionId}'", nameof(actionId));

    private void Persist()
    {
        _settings.SaveGlobal(_settings.Global);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}