namespace Loupedeck.ClickyPlugin;

using System;
using System.Collections.Generic;

internal sealed class ClickyAllowedInputAuthorizer
{
    private static readonly TimeSpan AuthorizationWindow = TimeSpan.FromMilliseconds(220);

    private readonly object _sync = new();
    private readonly Dictionary<GlobalMouseButton, Queue<DateTimeOffset>> _authorizations = new();
    private readonly ClickySelectedPointerDeviceService _selectedPointerDeviceService;
    private readonly Func<bool> _isHelperResponsive;

    public ClickyAllowedInputAuthorizer(ClickySelectedPointerDeviceService selectedPointerDeviceService, Func<bool> isHelperResponsive)
    {
        ArgumentNullException.ThrowIfNull(selectedPointerDeviceService);
        ArgumentNullException.ThrowIfNull(isHelperResponsive);
        this._selectedPointerDeviceService = selectedPointerDeviceService;
        this._isHelperResponsive = isHelperResponsive;
    }

    public ClickyInputEventRecord Process(ClickyInputEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var processed = record.Clone();
        // Once a device is selected, only that device can authorize haptics.
        var selectedDevice = this._selectedPointerDeviceService.GetSnapshot();
        processed.AllowedForHaptics = selectedDevice != null
            ? selectedDevice.Matches(processed)
            : processed.IsMxMaster4;

        if (processed.AllowedForHaptics && TryParseButton(processed.Button, out var button))
        {
            lock (this._sync)
            {
                if (!this._authorizations.TryGetValue(button, out var queue))
                {
                    queue = new Queue<DateTimeOffset>();
                    this._authorizations[button] = queue;
                }

                PruneExpired(queue, processed.OccurredAtUtc);
                queue.Enqueue(processed.OccurredAtUtc);
            }
        }

        return processed;
    }

    public bool ShouldTrigger(GlobalMouseButton button)
    {
        if (!this._isHelperResponsive())
        {
            // If the helper is down, fall back to the plain global hook behavior.
            return true;
        }

        lock (this._sync)
        {
            if (!this._authorizations.TryGetValue(button, out var queue))
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            PruneExpired(queue, now);
            if (queue.Count == 0)
            {
                return false;
            }

            queue.Dequeue();
            return true;
        }
    }

    private static bool TryParseButton(string buttonName, out GlobalMouseButton button)
    {
        switch ((buttonName ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "left":
                button = GlobalMouseButton.Left;
                return true;
            case "right":
                button = GlobalMouseButton.Right;
                return true;
            case "middle":
                button = GlobalMouseButton.Middle;
                return true;
            default:
                button = default;
                return false;
        }
    }

    private static void PruneExpired(Queue<DateTimeOffset> queue, DateTimeOffset referenceTime)
    {
        while (queue.Count > 0 && referenceTime - queue.Peek() > AuthorizationWindow)
        {
            queue.Dequeue();
        }
    }
}
