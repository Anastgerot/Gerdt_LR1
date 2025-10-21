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
        public DbSet<UserAssignment> UserAssignments => Set<UserAssignment>();

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder b)
        {

            b.Entity<User>(e =>
            {
                e.HasKey(x => x.Login);                       
                e.Property(x => x.Login).HasMaxLength(64).IsRequired();
                e.Property(x => x.Points).HasDefaultValue(0);    
                e.Property(x => x.PasswordHash).HasMaxLength(128).IsRequired();

            });


            b.Entity<Term>(e =>
            {
                e.HasKey(x => x.Id);                            
                e.Property(x => x.En).HasMaxLength(256).IsRequired();
                e.Property(x => x.Ru).HasMaxLength(256).IsRequired();

                e.Property(x => x.Domain)
                 .HasConversion<string>()        
                 .HasMaxLength(32)
                 .IsRequired();


                e.HasIndex(x => new { x.En, x.Ru }).IsUnique();
                e.HasIndex(x => x.En);                          
                e.HasIndex(x => x.Ru);
            });


            b.Entity<Assignment>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();

                e.HasOne(x => x.Term)
                 .WithMany()
                 .HasForeignKey(x => x.TermId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => new { x.TermId, x.Direction }).IsUnique();
            });


            b.Entity<UserAssignment>(e =>
            {
                e.HasKey(x => x.Id);

                e.Property(x => x.UserLogin).HasMaxLength(64).IsRequired();

                e.HasOne(x => x.User)
                 .WithMany(u => u.UserAssignments)
                 .HasForeignKey(x => x.UserLogin)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Assignment)
                 .WithMany()
                 .HasForeignKey(x => x.AssignmentId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => new { x.UserLogin, x.AssignmentId }).IsUnique();
            });


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
