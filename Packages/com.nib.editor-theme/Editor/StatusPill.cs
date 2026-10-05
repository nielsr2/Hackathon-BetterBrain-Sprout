using UnityEngine.UIElements;

namespace Nib.EditorTheme
{
    /// <summary>Severity of a <see cref="StatusPill"/>.</summary>
    public enum StatusPillLevel
    {
        /// <summary>Neutral / inactive.</summary>
        Off,
        /// <summary>Healthy / passing.</summary>
        Ok,
        /// <summary>Needs attention.</summary>
        Warn,
        /// <summary>Broken / failing.</summary>
        Error,
    }

    /// <summary>
    /// Small rounded status chip ("HDRP ✓", "unmapped", "connected") used across Nib windows.
    /// Styling comes from Controls.uss (<c>.nib-pill</c> and its level modifiers).
    /// </summary>
    [UxmlElement]
    public partial class StatusPill : Label
    {
        StatusPillLevel _level = StatusPillLevel.Off;

        public StatusPill()
        {
            AddToClassList("nib-pill");
            Level = StatusPillLevel.Off;
        }

        /// <summary>Severity shown by the pill; switches its color class.</summary>
        [UxmlAttribute]
        public StatusPillLevel Level
        {
            get => _level;
            set
            {
                _level = value;
                EnableInClassList("nib-pill--ok", value == StatusPillLevel.Ok);
                EnableInClassList("nib-pill--warn", value == StatusPillLevel.Warn);
                EnableInClassList("nib-pill--err", value == StatusPillLevel.Error);
            }
        }
    }
}
