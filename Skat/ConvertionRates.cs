using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Nationalbanken
{
    public class ConvertionRates
    {
        // https://nationalbanken.statbank.dk
        // => Exchange rates => Daily exchange rate => (USD, Exchange rates, dates) => Show table => Comma sep. (csv)

        Dictionary<string, double> rates;

        public ConvertionRates(Dictionary<string, double> _rates)
        {
            rates = _rates;
        }

        public static ConvertionRates Load(string path)
        {
            var dict = new Dictionary<string, double>();

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

                    double rate;
                    if (double.TryParse(values[i],
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out rate))
                    {
                        dict[formatted] = rate;
                    }
                }
            }

            var convertionRates = new ConvertionRates(dict);
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

        // TODO double GetRate(string date)
    }

}
