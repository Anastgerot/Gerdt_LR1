using Gerdt_LR1.Models;
using Microsoft.EntityFrameworkCore;

namespace Gerdt_LR1.Data

{
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<Term> Terms => Set<Term>();
        public DbSet<Assignment> Assignments => Set<Assignment>();

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder b)
        {
            // ----- User -----
            b.Entity<User>(e =>
            {
                e.HasKey(x => x.Login);                          // PK
                e.Property(x => x.Login).HasMaxLength(64).IsRequired();
                e.Property(x => x.Points).HasDefaultValue(0);    // дефолт 0 для очков
                e.Property(x => x.PasswordHash)
                .HasMaxLength(128)
                .IsRequired();
            });

            // ----- Term -----
            b.Entity<Term>(e =>
            {
                e.HasKey(x => x.Id);                             // PK
                e.Property(x => x.En).HasMaxLength(256).IsRequired();
                e.Property(x => x.Ru).HasMaxLength(256).IsRequired();
                e.Property(x => x.Domain).HasMaxLength(128);

                e.Property(x => x.OwnerLogin).HasMaxLength(64);
                e.HasOne(x => x.Owner)                           // FK: OwnerLogin -> Users(Login)
                 .WithMany()
                 .HasForeignKey(x => x.OwnerLogin)
                 .OnDelete(DeleteBehavior.SetNull);              // при удалении пользователя владелец станет NULL

                e.HasIndex(x => new { x.En, x.Ru }).IsUnique();  // уникальная пара (En,Ru)
                e.HasIndex(x => x.En);                           // индексы для поиска
                e.HasIndex(x => x.Ru);
            });

            // ----- Assignment -----
            b.Entity<Assignment>(e =>
            {
                e.HasKey(x => x.Id);

                e.HasOne(x => x.Term)                            // FK: TermId -> Terms(Id)
                 .WithMany()
                 .HasForeignKey(x => x.TermId)
                 .OnDelete(DeleteBehavior.Cascade);              // удалили Term — удалились его карточки

                e.Property(x => x.AssignedToLogin).HasMaxLength(64).IsRequired();
                e.HasOne(x => x.AssignedTo)                      // FK: AssignedToLogin -> Users(Login)
                 .WithMany()
                 .HasForeignKey(x => x.AssignedToLogin)
                 .OnDelete(DeleteBehavior.Restrict);             // нельзя удалить пользователя, пока есть задания на него

                e.HasIndex(x => new { x.AssignedToLogin, x.IsSolved });  // ускорит выборки "задания пользователя/решённые"
                                                                         
            });
        }
    }
}
