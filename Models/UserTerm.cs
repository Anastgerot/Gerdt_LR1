using System.Text.Json.Serialization;
namespace Gerdt_LR1.Models
{
    public class UserTerm
    {
        public int Id { get; set; }              
        public string UserLogin { get; set; } = ""; 
        public int TermId { get; set; }           

        [JsonIgnore] public User? User { get; set; }
        [JsonIgnore] public Term? Term { get; set; }
            
        public DateTime LastViewedAt { get; set; } = DateTime.UtcNow;
    }
}
