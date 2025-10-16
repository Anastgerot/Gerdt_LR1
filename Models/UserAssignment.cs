using System.Text.Json.Serialization;

namespace Gerdt_LR1.Models
{
    public class UserAssignment
    {
        public int Id { get; set; }

        public string UserLogin { get; set; } = "";  
        public int AssignmentId { get; set; }       

        [JsonIgnore] 
        public User? User { get; set; }

        [JsonIgnore] 
        public Assignment? Assignment { get; set; }

        public bool IsSolved { get; set; } = false;
        public DateTime? SolvedAt { get; set; }

        public int Attempts { get; set; } = 0;            
        public DateTime? LastAnsweredAt { get; set; }
    }
}
