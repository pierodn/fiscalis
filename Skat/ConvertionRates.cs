using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace Nationalbanken
{

    public class ConvertionRates
    {
        // https://nationalbanken.statbank.dk
        // => Exchange rates => Daily exchange rate => (USD, Exchange rates, dates) => Show table => Comma sep. (csv)

        const decimal QuotationRate = 100.0M; 

        Dictionary<string, decimal> rates;

        public ConvertionRates(Dictionary<string, decimal> _rates)
        {
            rates = _rates;
        }

        public decimal? GetRate(string dateStr)
        {
            const int MaxDaysBack = 9;

            DateTime date = DateTime.Parse(dateStr);

            for (int i = 0; i < MaxDaysBack; i++)
            {
                string key = date.ToString("yyyy-MM-dd");
                decimal rate;
                if (rates.TryGetValue(key, out rate))
                {
                    return rate;
                }
                date = date.AddDays(-1);
            }

            Trace.TraceError("Can't find the daily rate for " + dateStr);

            return null;
        }

        public static ConvertionRates Import(string path)
        {
            var dictionary = new Dictionary<string, decimal>();

            using (var reader = new StreamReader(path))
            {
                reader.ReadLine();
                reader.ReadLine();
                string dateLine = reader.ReadLine();

                reader.ReadLine();
                string usdLine = reader.ReadLine();

                string[] dates = dateLine.Split(',');
                string[] values = usdLine.Split(',');

                for (int i = 2; i < dates.Length && i < values.Length; i++)
                {
                    string raw = dates[i].Trim('"');   // e.g. 2023M01D02
                    string formatted = ConvertDate(raw); // e.g. 2023-01-02

                    decimal rate;
                    if (decimal.TryParse(values[i],
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out rate))
                    {
                        dictionary[formatted] = rate / QuotationRate;
                    }
                }
            }

            var convertionRates = new ConvertionRates(dictionary);
            return convertionRates;
        }

        // Convert from yyyyMmmDdd to yyyy-mm-dd
        private static string ConvertDate(string raw)
        {
            string year = raw.Substring(0, 4);
            string month = raw.Substring(5, 2);
            string day = raw.Substring(8, 2);

            return year + "-" + month + "-" + day;
        }
    }

}
