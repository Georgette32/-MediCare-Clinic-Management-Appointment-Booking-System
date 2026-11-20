
using MediCare.Domain.Entities;
using MediCare.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration
	: IEntityTypeConfiguration<Notification>
{
	public void Configure(EntityTypeBuilder<Notification> builder)
	{
		builder.ToTable("Notifications");

		builder.HasKey(n => n.Id);

		builder.Property(n => n.Title)
			.HasMaxLength(200)
			.IsRequired();

		builder.Property(n => n.Message)
			.HasMaxLength(2000)
			.IsRequired();

		builder.Property(n => n.Type)
			.HasConversion<int>();

		builder.HasIndex(n => new { n.UserId, n.CreatedAtUtc });

		builder.HasIndex(n => new { n.UserId, n.IsRead });

		builder.HasOne<ApplicationUser>()
			.WithMany()
			.HasForeignKey(n => n.UserId)
			.OnDelete(DeleteBehavior.Cascade);
	}
}
