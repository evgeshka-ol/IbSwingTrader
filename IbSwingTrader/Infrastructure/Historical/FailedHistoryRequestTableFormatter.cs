using System.Text;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Infrastructure.Historical
{
    public class FailedHistoryRequestTableFormatter : IFailedHistoryRequestTableFormatter
    {
        public string Format(IEnumerable<FailedHistoryRequest> items)
        {
            return FormatInternal(items);
        }

        private static string FormatInternal(IEnumerable<FailedHistoryRequest> items)
        {
            var list = items.ToList();

            if (list.Count == 0)
                return "No failed history requests.";

            const string tickerHeader = "Ticker";
            const string problemHeader = "Problem";

            int tickerWidth = Math.Max(
                tickerHeader.Length,
                list.Max(x => (x.Ticker ?? "").Length));

            int problemWidth = Math.Max(
                problemHeader.Length,
                list.Max(x => (x.Problem ?? "").Length));

            string top =
                "┌" + new string('─', tickerWidth + 2) +
                "┬" + new string('─', problemWidth + 2) + "┐";

            string mid =
                "├" + new string('─', tickerWidth + 2) +
                "┼" + new string('─', problemWidth + 2) + "┤";

            string bottom =
                "└" + new string('─', tickerWidth + 2) +
                "┴" + new string('─', problemWidth + 2) + "┘";

            var sb = new StringBuilder();

            sb.AppendLine(top);
            sb.AppendLine(
                $"│ {tickerHeader.PadRight(tickerWidth)} │ {problemHeader.PadRight(problemWidth)} │");
            sb.AppendLine(mid);

            foreach (var item in list.OrderBy(x => x.Ticker))
            {
                sb.AppendLine(
                    $"│ {(item.Ticker ?? "").PadRight(tickerWidth)} │ {(item.Problem ?? "").PadRight(problemWidth)} │");
            }

            sb.AppendLine(bottom);

            return sb.ToString();
        }
    }
}
