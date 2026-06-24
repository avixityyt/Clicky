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
        var candidates = events
            .GroupBy(record => BuildCandidateKey(record), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var latest = group.First();
                var isSelected = selectedDevice != null && selectedDevice.Matches(latest);
                var isSharedReceiver = string.Equals(latest.ConnectionType, "receiver-shared", StringComparison.OrdinalIgnoreCase);

                return new ClickyPointerDeviceCandidate
                {
                    SourceKey = BuildCandidateKey(latest),
                    DeviceLabel = latest.DeviceLabel,
                    DevicePath = latest.DevicePath,
                    VendorId = latest.VendorId,
                    ProductId = latest.ProductId,
                    ConnectionType = latest.ConnectionType,
                    IsMxMaster4 = latest.IsMxMaster4,
                    IsSharedReceiver = isSharedReceiver,
                    IsLikelyAmbiguous = isSharedReceiver,
                    IsSelected = isSelected,
                    SeenCount = group.Count(),
                    LastButton = latest.Button,
                    FirstSeenUtc = group.Min(item => item.OccurredAtUtc),
                    LastSeenUtc = latest.OccurredAtUtc,
                    IdentityKind = GetIdentityKind(latest, isSharedReceiver),
                };
            })
            .ToList();

        var duplicateFingerprints = candidates
            .GroupBy(BuildFingerprintKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var hasDuplicateFingerprint = duplicateFingerprints.Contains(BuildFingerprintKey(candidate));
            candidate.IsLikelyAmbiguous = candidate.IsLikelyAmbiguous || hasDuplicateFingerprint;
            candidate.Warning = BuildWarning(candidate, hasDuplicateFingerprint);
        }

        return candidates
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

    private static string BuildFingerprintKey(ClickyPointerDeviceCandidate candidate)
    {
        return string.Join(
            "|",
            candidate.VendorId?.Trim().ToUpperInvariant() ?? string.Empty,
            candidate.ProductId?.Trim().ToUpperInvariant() ?? string.Empty,
            candidate.ConnectionType?.Trim().ToLowerInvariant() ?? string.Empty);
    }

    private static string GetIdentityKind(ClickyInputEventRecord record, bool isSharedReceiver)
    {
        if (isSharedReceiver)
        {
            return "shared_receiver";
        }

        if (!string.IsNullOrWhiteSpace(record.DevicePath))
        {
            return "exact_path";
        }

        return "fingerprint";
    }

    private static string BuildWarning(ClickyPointerDeviceCandidate candidate, bool hasDuplicateFingerprint)
    {
        if (candidate.IsSharedReceiver)
        {
            return "This looks like a shared Logitech receiver identity and may include more than one paired device.";
        }

        if (hasDuplicateFingerprint)
        {
            return "This input fingerprint overlaps with another observed device, so binding may not be unique.";
        }

        return string.Empty;
    }
}
