using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Tree-phase cue on the Timeline's marker row, received by <see cref="SequenceController"/>.
/// Retroactive + emit-once: starting or skipping past a marker still fires it, exactly once.
/// </summary>
[DisplayName("Sequence Marker")]
public sealed class SequenceMarker : Marker, INotification, INotificationOptionProvider
{
    public enum Kind
    {
        [Tooltip("EEG starts collecting its resting baseline; growth stays on the timeline.")]
        StartBaseline,
        [Tooltip("The EEG takes over tree growth from its current value.")]
        Interactive,
        [Tooltip("Glitch into the outro now, grown or not.")]
        Timeout,
    }

    public Kind kind;

    public PropertyName id => new PropertyName($"SequenceMarker.{kind}");
    NotificationFlags INotificationOptionProvider.flags => NotificationFlags.Retroactive | NotificationFlags.TriggerOnce;
}
