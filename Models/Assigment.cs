using System.Text.Json.Serialization;
namespace Gerdt_LR1.Models
{
    public class Assignment
    {
        public int Id { get; set; }

        public int TermId { get; set; }

        [JsonIgnore] 
        public Term? Term { get; set; }

        public Direction Direction { get; set; } = Direction.EnToRu;

        [JsonIgnore]
        public string ExpectedAnswer => Term!.Translate(Direction);

        // Бизнес-логика: проверка ответа
        public bool CheckAnswer(string userAnswer) =>
            Term != null && Term.CheckTranslation(Direction, userAnswer);
    }
}

