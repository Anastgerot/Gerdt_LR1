using System.Text.Json.Serialization;
namespace Gerdt_LR1.Models
{
    public enum Direction { EnToRu, RuToEn }
    public class Term
    {
        public int Id { get; set; }
        public string En { get; set; } = "";
        public string Ru { get; set; } = "";
        public string? Domain { get; set; }


        // Бизнес-логика: обновить перевод с учётом направления
        public void UpdateTranslation(Direction direction, string newValue)
        {
            if (string.IsNullOrWhiteSpace(newValue)) return;
            if (direction == Direction.EnToRu) Ru = newValue.Trim();
            else En = newValue.Trim();
        }

        // Бизнес-логика: получить перевод в нужном направлении
        public string Translate(Direction direction) => direction == Direction.EnToRu ? Ru : En;

        // Бизнес-логика: проверить, совпадает ли ответ с нужным переводом
        public bool CheckTranslation(Direction direction, string value)
        {
            var expected = Translate(direction);
            return string.Equals(expected, value?.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
