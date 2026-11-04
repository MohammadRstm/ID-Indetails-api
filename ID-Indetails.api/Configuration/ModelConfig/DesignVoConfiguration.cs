using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class DesignVoConfiguration : IEntityTypeConfiguration<DesignVo>
{
    public void Configure(EntityTypeBuilder<DesignVo> builder)
    {
        builder.ToTable("design_vos", t =>
        {
            t.HasCheckConstraint("ck_design_vos_status", SnakeCaseEnum.CheckSql<DesignVoStatus>("status"));
            t.HasCheckConstraint("ck_design_vos_split", SnakeCaseEnum.CheckSql<DesignVoSplit>("split"));
        });

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => new { x.ProjectId, x.Status });

        builder.Property(x => x.Number).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.TotalUsd).HasPrecision(14, 2);
        builder.Property(x => x.TvaRate).HasPrecision(5, 4);

        builder.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.SentById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.AppliedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CancelledById).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.DesignVoId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Allocations).WithOne().HasForeignKey(a => a.DesignVoId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class DesignVoLineConfiguration : IEntityTypeConfiguration<DesignVoLine>
{
    public void Configure(EntityTypeBuilder<DesignVoLine> builder)
    {
        builder.ToTable("design_vo_lines");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.DesignVoId, x.Position }).IsUnique();
        builder.Property(x => x.Description).IsRequired().HasMaxLength(500);
        builder.Property(x => x.Amount).HasPrecision(14, 2);
    }
}

public class DesignVoAllocationConfiguration : IEntityTypeConfiguration<DesignVoAllocation>
{
    public void Configure(EntityTypeBuilder<DesignVoAllocation> builder)
    {
        builder.ToTable("design_vo_allocations");
        builder.HasKey(x => new { x.DesignVoId, x.TeamId });
        builder.HasIndex(x => x.TeamId);
        builder.Property(x => x.Amount).HasPrecision(14, 2);
        // a department with a VO's money stays: rename it instead
        builder.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
    }
}
