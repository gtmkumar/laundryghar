using laundryghar.SharedDataModel.Entities.EngagementCms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.EngagementCms;

public sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> b)
    {
        b.ToTable("support_tickets", "engagement_cms");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.TicketNumber).HasColumnName("ticket_number").HasMaxLength(40).IsRequired();
        b.Property(e => e.RequesterType).HasColumnName("requester_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.RequesterId).HasColumnName("requester_id").IsRequired();
        b.Property(e => e.CustomerId).HasColumnName("customer_id");
        b.Property(e => e.RiderId).HasColumnName("rider_id");
        b.Property(e => e.OrderId).HasColumnName("order_id");
        b.Property(e => e.Subject).HasColumnName("subject").HasMaxLength(200).IsRequired();
        b.Property(e => e.Category).HasColumnName("category").HasMaxLength(40).IsRequired();
        b.Property(e => e.Priority).HasColumnName("priority").HasMaxLength(20).IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.AssignedTo).HasColumnName("assigned_to");
        b.Property(e => e.LastMessageAt).HasColumnName("last_message_at").IsRequired();
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasMany(e => e.Messages).WithOne(m => m.Ticket).HasForeignKey(m => m.TicketId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("ticket_messages_ticket_id_fkey");
    }
}

public sealed class TicketMessageConfiguration : IEntityTypeConfiguration<TicketMessage>
{
    public void Configure(EntityTypeBuilder<TicketMessage> b)
    {
        b.ToTable("ticket_messages", "engagement_cms");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.TicketId).HasColumnName("ticket_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.SenderType).HasColumnName("sender_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.SenderId).HasColumnName("sender_id");
        b.Property(e => e.Body).HasColumnName("body").IsRequired();
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
    }
}
