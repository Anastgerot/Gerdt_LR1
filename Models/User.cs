using System.Security.Cryptography;
using System.Text;

namespace Gerdt_LR1.Models
{
    public class User
    {
        public string Login { get; set; } = "";
        public string PasswordHash { get; private set; } = "";
        public int Points { get; private set; }
        public bool IsAdmin => Login == "admin";

        public void SetPassword(string raw)
        {
            using var sha = SHA256.Create();
            PasswordHash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw ?? ""))).ToLowerInvariant();
        }

        public bool CheckPassword(string raw)
        {
            using var sha = SHA256.Create();
            var hex = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw ?? ""))).ToLowerInvariant();
            return hex == PasswordHash;
        }

        // Бизнес-логика: начислить очки за верные ответы
        public void AddPoints(int correctAnswers)
        {
            if (correctAnswers > 0) Points += correctAnswers * 10;
        }

        // Бизнес-логика: создать карточку для конкретного термина и направления
        public Assignment CreateAssignmentForTerm(Term term, Direction direction = Direction.EnToRu)
        {
            return new Assignment
            {
                TermId = term.Id,
                Term = term,
                AssignedToLogin = this.Login,
                Direction = direction
            };
        }
    }
}
