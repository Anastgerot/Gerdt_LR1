namespace Gerdt_LR1.Models
{
    public enum Direction { EnToRu, RuToEn }
    public enum TermDomain
    {
        General,      // общее
        Drilling,     // бурение
        Geology,      // геология
        Equipment,    // оборудование
        Safety        // безопасность
    }
    public class Term
    {
        public int Id { get; set; }
        public string En { get; set; } = "";
        public string Ru { get; set; } = "";
        public TermDomain Domain { get; set; }


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
