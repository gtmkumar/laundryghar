namespace laundryghar.SharedDataModel.Entities.EngagementCms;

/// <summary>A customer or rider support ticket (engagement_cms.support_tickets) with a
/// threaded conversation (<see cref="TicketMessage"/>).</summary>
public class SupportTicket
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string TicketNumber { get; set; } = null!;

    /// <summary>customer|rider — who opened it.</summary>
    public string RequesterType { get; set; } = null!;
    /// <summary>User id of the requester.</summary>
    public Guid RequesterId { get; set; }

    public Guid? CustomerId { get; set; }
    public Guid? RiderId { get; set; }
    public Guid? OrderId { get; set; }

    public string Subject { get; set; } = null!;
    public string Category { get; set; } = "general";
    public string Priority { get; set; } = "normal";
    /// <summary>open|in_progress|resolved|closed.</summary>
    public string Status { get; set; } = "open";
    public Guid? AssignedTo { get; set; }
    public DateTimeOffset LastMessageAt { get; set; }

    public string Metadata { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    public ICollection<TicketMessage> Messages { get; set; } = [];
}

/// <summary>One message in a support ticket thread (engagement_cms.ticket_messages).</summary>
public class TicketMessage
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public Guid BrandId { get; set; }
    /// <summary>customer|rider|agent|system.</summary>
    public string SenderType { get; set; } = null!;
    public Guid? SenderId { get; set; }
    public string Body { get; set; } = null!;
    public string Metadata { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    public SupportTicket Ticket { get; set; } = null!;
}
