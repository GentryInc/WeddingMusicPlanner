using System.ComponentModel.DataAnnotations;

namespace WeddingMusicData.Models;

/// <summary>
/// Booking/pipeline status for a wedding event, from initial enquiry through to a
/// confirmed booking. Kept lightweight for a wedding-specific event CRM.
/// </summary>
public enum EventStatus
{
    Lead = 0,
    ContractSent = 1,
    DepositPaid = 2,
    Booked = 3,
    Completed = 4,
    Cancelled = 5
}

/// <summary>
/// Which planning bucket a <see cref="PlanningEntry"/> belongs to, e.g. the couple's
/// must-play list, do-not-play list, or a keyed questionnaire answer (First Dance, etc.).
/// </summary>
public enum PlanningEntryKind
{
    MustPlay = 0,
    DoNotPlay = 1,
    SpecialMoment = 2,
    Note = 3
}

/// <summary>
/// A wedding-specific CRM record: the couple, their contact details, venue, date,
/// booking status and the planning form entries. Optionally linked to a
/// <see cref="PlaylistSection"/> so the couple's profile ties into the segment builder.
/// </summary>
public class EventProfile
{
    public int Id { get; set; }

    [Required, MaxLength(256)]
    public string CoupleNames { get; set; } = string.Empty;

    /// <summary>Date of the wedding/event.</summary>
    public DateTimeOffset? EventDate { get; set; }

    [MaxLength(256)] public string? VenueName { get; set; }
    [MaxLength(512)] public string? VenueAddress { get; set; }

    [MaxLength(128)] public string? ContactName { get; set; }
    [MaxLength(256)] public string? ContactEmail { get; set; }
    [MaxLength(64)] public string? ContactPhone { get; set; }

    public EventStatus Status { get; set; } = EventStatus.Lead;

    [MaxLength(2048)] public string? Notes { get; set; }

    /// <summary>
    /// Optional link to the playlist section that this event's music is planned in, so
    /// the CRM profile connects directly to the segment builder.
    /// </summary>
    public int? PlaylistSectionId { get; set; }
    public PlaylistSection? PlaylistSection { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedUtc { get; set; }

    /// <summary>Planning-form entries (must-play, do-not-play, questionnaire answers).</summary>
    public ICollection<PlanningEntry> PlanningEntries { get; set; } = new List<PlanningEntry>();
}

/// <summary>
/// A single planning-form line item belonging to an <see cref="EventProfile"/>, e.g. a
/// must-play song, a do-not-play artist, or a keyed questionnaire answer.
/// </summary>
public class PlanningEntry
{
    public int Id { get; set; }

    public int EventProfileId { get; set; }
    public EventProfile? EventProfile { get; set; }

    public PlanningEntryKind Kind { get; set; } = PlanningEntryKind.MustPlay;

    /// <summary>Optional label/prompt, e.g. "First Dance", "Father-Daughter Dance".</summary>
    [MaxLength(128)] public string? Label { get; set; }

    /// <summary>The free-text value (song title/artist, request note, answer).</summary>
    [MaxLength(512)] public string Value { get; set; } = string.Empty;

    public int Position { get; set; }
}
