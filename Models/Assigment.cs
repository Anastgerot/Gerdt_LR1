using System.Text.Json.Serialization;
namespace Gerdt_LR1.Models
{
    public class Assignment
    {
        public int Id { get; set; }
        public int TermId { get; set; }
        [JsonIgnore] public Term? Term { get; set; }             

        public string AssignedToLogin { get; set; } = "";
        [JsonIgnore] public User? AssignedTo { get; set; }
        public Direction Direction { get; set; } = Direction.EnToRu;
        [JsonIgnore] public string ExpectedAnswer => Term.Translate(Direction);
        public bool IsSolved { get; private set; }

        // Бизнес-логика: проверить ответ
        public bool CheckAnswer(string userAnswer)
        {
            if (Term.CheckTranslation(Direction, userAnswer))
            {
                IsSolved = true;
                return true;
            }
            return false;
        }

        public void Reset() => IsSolved = false;

        // Бизнес-логика: переключить направление перевода для карточки
        public void SwitchDirection()
        {
            Direction = Direction == Direction.EnToRu ? Direction.RuToEn : Direction.EnToRu;
            Reset();
        }
    }
}

