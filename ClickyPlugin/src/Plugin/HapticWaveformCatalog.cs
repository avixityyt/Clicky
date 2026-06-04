namespace Loupedeck.ClickyPlugin;

using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class HapticWaveformDefinition
{
    public HapticWaveformDefinition(string id, string label, string description)
    {
        this.Id = id;
        this.Label = label;
        this.Description = description;
    }

    public string Id { get; }

    public string Label { get; }

    public string Description { get; }
}

internal static class HapticWaveformCatalog
{
    public const string SharpStateChange = "sharp_state_change";
    public const string DampStateChange = "damp_state_change";
    public const string SharpCollision = "sharp_collision";
    public const string DampCollision = "damp_collision";
    public const string SubtleCollision = "subtle_collision";
    public const string HappyAlert = "happy_alert";
    public const string AngryAlert = "angry_alert";
    public const string Completed = "completed";
    public const string Square = "square";
    public const string Wave = "wave";
    public const string Firework = "firework";
    public const string Mad = "mad";
    public const string Knock = "knock";
    public const string Jingle = "jingle";
    public const string Ringing = "ringing";

    private static readonly HapticWaveformDefinition[] Definitions =
    [
        new(SharpStateChange, "Sharp State Change", "Clean and crisp."),
        new(DampStateChange, "Damp State Change", "Soft and restrained."),
        new(SharpCollision, "Sharp Collision", "Firm and punchy."),
        new(DampCollision, "Damp Collision", "Controlled impact."),
        new(SubtleCollision, "Subtle Collision", "Light and quiet."),
        new(HappyAlert, "Happy Alert", "Bright, positive pulse."),
        new(AngryAlert, "Angry Alert", "Strong warning feel."),
        new(Completed, "Completed", "Short resolved finish."),
        new(Square, "Square", "Flat, steady pulse."),
        new(Wave, "Wave", "Rolling pulse."),
        new(Firework, "Firework", "Expanding pop."),
        new(Mad, "Mad", "Aggressive energy."),
        new(Knock, "Knock", "Double knock feel."),
        new(Jingle, "Jingle", "Light sequence."),
        new(Ringing, "Ringing", "Repeating ring feel."),
    ];

    private static readonly HashSet<string> KnownIds = new(Definitions.Select(definition => definition.Id), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<HapticWaveformDefinition> All => Definitions;

    public static string DefaultWaveform => SharpStateChange;

    public static string Normalize(string? waveform)
    {
        var normalized = waveform?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DefaultWaveform;
        }

        return normalized switch
        {
            "subtle" => SubtleCollision,
            "balanced" => DefaultWaveform,
            "punchy" => SharpCollision,
            _ when KnownIds.Contains(normalized) => normalized,
            _ => DefaultWaveform,
        };
    }

    public static string ToEventSuffix(string waveform) =>
        Normalize(waveform) switch
        {
            SharpStateChange => "SharpStateChange",
            DampStateChange => "DampStateChange",
            SharpCollision => "SharpCollision",
            DampCollision => "DampCollision",
            SubtleCollision => "SubtleCollision",
            HappyAlert => "HappyAlert",
            AngryAlert => "AngryAlert",
            Completed => "Completed",
            Square => "Square",
            Wave => "Wave",
            Firework => "Firework",
            Mad => "Mad",
            Knock => "Knock",
            Jingle => "Jingle",
            Ringing => "Ringing",
            _ => "SharpStateChange",
        };
}
