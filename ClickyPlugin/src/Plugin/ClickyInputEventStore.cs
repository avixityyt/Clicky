namespace Loupedeck.ClickyPlugin;

using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class ClickyInputEventStore
{
    private const int MaxEvents = 32;

    private readonly object _sync = new();
    private readonly List<ClickyInputEventRecord> _events = [];
    private int _totalRecorded;
    private DateTimeOffset? _lastInputEventUtc;

    public void Record(ClickyInputEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (this._sync)
        {
            // Keep the newest events first so the dev view and picker stay responsive.
            this._events.Insert(0, record);
            if (this._events.Count > MaxEvents)
            {
                this._events.RemoveRange(MaxEvents, this._events.Count - MaxEvents);
            }

            this._totalRecorded++;
            this._lastInputEventUtc = record.OccurredAtUtc;
        }
    }

    public ClickyInputEventsSnapshot CreateSnapshot(bool helperRunning)
    {
        lock (this._sync)
        {
            return new ClickyInputEventsSnapshot
            {
                HelperRunning = helperRunning,
                LastInputEventUtc = this._lastInputEventUtc,
                TotalRecorded = this._totalRecorded,
                Events = [.. this._events],
            };
        }
    }

    public ClickyDeviceBindingSnapshot CreateBindingSnapshot(bool helperRunning, ClickySelectedPointerDevice? selectedDevice)
    {
        lock (this._sync)
        {
            return new ClickyDeviceBindingSnapshot
            {
                HelperRunning = helperRunning,
                RequiresSelection = selectedDevice == null,
                SelectedDevice = selectedDevice?.Clone(),
                Candidates = BuildCandidates(this._events, selectedDevice),
            };
        }
    }

    public void Clear()
    {
        lock (this._sync)
        {
            this._events.Clear();
            this._totalRecorded = 0;
            this._lastInputEventUtc = null;
        }
    }

    private static ClickyPointerDeviceCandidate[] BuildCandidates(IReadOnlyList<ClickyInputEventRecord> events, ClickySelectedPointerDevice? selectedDevice)
    {
        // Collapse repeated events from the same pointer into a short list of recent candidates.
        return events
            .GroupBy(record => BuildCandidateKey(record), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var latest = group.First();
                var isSelected = selectedDevice != null && selectedDevice.Matches(latest);

                return new ClickyPointerDeviceCandidate
                {
                    DeviceLabel = latest.DeviceLabel,
                    DevicePath = latest.DevicePath,
                    VendorId = latest.VendorId,
                    ProductId = latest.ProductId,
                    ConnectionType = latest.ConnectionType,
                    IsMxMaster4 = latest.IsMxMaster4,
                    IsSelected = isSelected,
                    SeenCount = group.Count(),
                    LastButton = latest.Button,
                    LastSeenUtc = latest.OccurredAtUtc,
                };
            })
            .OrderByDescending(candidate => candidate.IsSelected)
            .ThenByDescending(candidate => candidate.IsMxMaster4)
            .ThenByDescending(candidate => candidate.LastSeenUtc)
            .Take(8)
            .ToArray();
    }

    private static string BuildCandidateKey(ClickyInputEventRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.DevicePath))
        {
            return record.DevicePath.Trim();
        }

        return string.Join(
            "|",
            record.DeviceLabel?.Trim() ?? string.Empty,
            record.VendorId?.Trim() ?? string.Empty,
            record.ProductId?.Trim() ?? string.Empty,
            record.ConnectionType?.Trim() ?? string.Empty);
    }
}
