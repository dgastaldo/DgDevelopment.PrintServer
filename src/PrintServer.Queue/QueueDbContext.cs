using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PrintServer.Core.Queue;

namespace PrintServer.Queue;

public sealed class QueueDbContext(DbContextOptions<QueueDbContext> options) : DbContext(options)
{
    public DbSet<JobEntity> Jobs => Set<JobEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var ticks = new ValueConverter<DateTimeOffset, long>(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
        var state = new ValueConverter<JobState, string>(
            value => JobStateNames.ToDatabase(value),
            value => JobStateNames.FromDatabase(value));

        modelBuilder.Entity<JobEntity>(job =>
        {
            job.ToTable("jobs");
            job.HasKey(j => j.Id);

            job.Property(j => j.Id).HasColumnName("id");
            job.Property(j => j.OriginDirectory).HasColumnName("origin_directory").IsRequired();
            job.Property(j => j.FileName).HasColumnName("file_name").IsRequired();
            job.Property(j => j.SourcePath).HasColumnName("source_path").IsRequired();
            job.Property(j => j.DocumentPath).HasColumnName("document_path").IsRequired();
            job.Property(j => j.PrinterName).HasColumnName("printer_name");
            job.Property(j => j.FileCreatedAt).HasColumnName("file_created_at").HasConversion(ticks);
            job.Property(j => j.FileHash).HasColumnName("file_hash").IsRequired();
            job.Property(j => j.FileSize).HasColumnName("file_size");
            job.Property(j => j.SourceFormat).HasColumnName("source_format").IsRequired();
            job.Property(j => j.DocumentFormat).HasColumnName("document_format").IsRequired();
            job.Property(j => j.OptionsJson).HasColumnName("options").IsRequired();
            job.Property(j => j.State).HasColumnName("state").HasConversion(state).IsRequired();
            job.Property(j => j.Attempts).HasColumnName("attempts");
            job.Property(j => j.NextAttemptAt).HasColumnName("next_attempt_at").HasConversion(ticks);
            job.Property(j => j.LastError).HasColumnName("last_error");
            job.Property(j => j.BackendJobId).HasColumnName("backend_job_id");
            job.Property(j => j.CreatedAt).HasColumnName("created_at").HasConversion(ticks);
            job.Property(j => j.StartedAt).HasColumnName("started_at").HasConversion(ticks);
            job.Property(j => j.FinishedAt).HasColumnName("finished_at").HasConversion(ticks);
            job.Property(j => j.LastBlockedAt).HasColumnName("last_blocked_at").HasConversion(ticks);

            job.HasIndex(j => new { j.OriginDirectory, j.FileName, j.FileCreatedAt, j.FileHash })
                .HasDatabaseName("ux_jobs_active_file")
                .IsUnique()
                .HasFilter("state IN ('in_attesa', 'in_conversione', 'in_stampa')");
            job.HasIndex(j => new { j.OriginDirectory, j.FileName }).HasDatabaseName("ix_jobs_origin_file");
            job.HasIndex(j => new { j.State, j.NextAttemptAt, j.CreatedAt }).HasDatabaseName("ix_jobs_state_next");
        });
    }
}
