using Gerdt_LR1.Models;
using Microsoft.EntityFrameworkCore;

namespace Gerdt_LR1.Data

{
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<Term> Terms => Set<Term>();
        public DbSet<Assignment> Assignments => Set<Assignment>();
        public DbSet<UserTerm> UserTerms => Set<UserTerm>();

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder b)
        {
            // ----- User -----
            b.Entity<User>(e =>
            {
                e.HasKey(x => x.Login);                       
                e.Property(x => x.Login).HasMaxLength(64).IsRequired();
                e.Property(x => x.Points).HasDefaultValue(0);    
                e.Property(x => x.PasswordHash).HasMaxLength(128).IsRequired();

            });

            // ----- Term -----
            b.Entity<Term>(e =>
            {
                e.HasKey(x => x.Id);                            
                e.Property(x => x.En).HasMaxLength(256).IsRequired();
                e.Property(x => x.Ru).HasMaxLength(256).IsRequired();
                e.Property(x => x.Domain).HasMaxLength(128);


                e.HasIndex(x => new { x.En, x.Ru }).IsUnique();
                e.HasIndex(x => x.En);                          
                e.HasIndex(x => x.Ru);
            });

            b.Entity<Assignment>(e =>
            {
                e.HasKey(x => x.Id);

                e.HasOne(x => x.Term).WithMany().HasForeignKey(x => x.TermId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.Property(x => x.AssignedToLogin).HasMaxLength(64).IsRequired();
                e.HasOne(x => x.AssignedTo).WithMany().HasForeignKey(x => x.AssignedToLogin)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(x => new { x.AssignedToLogin, x.TermId, x.Direction }).IsUnique();
            });

            // ----- UserTerm (многие-ко-многим) -----
            b.Entity<UserTerm>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.UserLogin, x.TermId }).IsUnique();

                e.Property(x => x.UserLogin).HasMaxLength(64).IsRequired();

                e.HasOne(x => x.User).WithMany(u => u.UserTerms)
                    .HasForeignKey(x => x.UserLogin)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Term).WithMany()
                    .HasForeignKey(x => x.TermId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

        }
    }
}
