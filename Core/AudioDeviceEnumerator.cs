using System.Collections.Generic;
using System.Linq;
using LibVLCSharp.Shared;

namespace ShowCast.Core;

public static class AudioDeviceEnumerator
{
    /// <summary>
    /// Enumerates hardware output devices via LibVLC WASAPI.
    /// Returns fresh <see cref="AudioDestination"/> objects — call
    /// <see cref="MergeHardware"/> to reconcile with stored destinations.
    /// </summary>
    public static IReadOnlyList<AudioDestination> EnumerateHardware(LibVLC libVlc)
    {
        return libVlc.AudioOutputDevices("wasapi")
            .Select(d => new AudioDestination
            {
                DeviceId    = d.DeviceIdentifier,
                SystemName  = d.Description,
                DisplayName = d.Description,
                Type        = AudioRouteType.Hardware
            })
            .ToList();
    }

    /// <summary>
    /// Returns NDI destinations derived from the show file's NDI outputs.
    /// </summary>
    public static IReadOnlyList<AudioDestination> EnumerateNdi(ShowFile showFile)
    {
        return showFile.Outputs
            .Where(o => o.Type == OutputType.NDI)
            .Select(o => new AudioDestination
            {
                DeviceId    = o.NdiStreamName,
                SystemName  = o.Name,
                DisplayName = o.Name,
                Type        = AudioRouteType.Ndi
            })
            .ToList();
    }

    /// <summary>
    /// Reconciles freshly enumerated hardware devices into the stored list:
    /// - New devices are appended.
    /// - Existing devices have their <see cref="AudioDestination.SystemName"/> updated.
    /// - <see cref="AudioDestination.DisplayName"/> (user-editable alias) is never touched.
    /// - Devices no longer present remain in the list (greyed out in the UI).
    /// </summary>
    public static void MergeHardware(
        List<AudioDestination> existing,
        IReadOnlyList<AudioDestination> fresh)
    {
        foreach (var freshDest in fresh)
        {
            var match = existing.FirstOrDefault(e =>
                e.Type == AudioRouteType.Hardware &&
                e.DeviceId == freshDest.DeviceId);

            if (match is null)
            {
                existing.Add(freshDest);
            }
            else
            {
                // Only update the system-assigned name — preserve user DisplayName
                match.SystemName = freshDest.SystemName;
            }
        }
    }
}
