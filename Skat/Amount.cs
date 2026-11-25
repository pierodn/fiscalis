using System;
using System.Collections.Generic;

namespace Skat
{
    public class Amount
    {
        private Dictionary<string, decimal> _amounts =
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        public decimal this[string currencyCode]
        {
            get
            {
                if (currencyCode == null)
                {
                    throw new ArgumentNullException("currencyCode");
                }

                string key = currencyCode.ToUpperInvariant();
                decimal value;
                if (_amounts.TryGetValue(key, out value))
                {
                    return value;
                }
                return 0m;
            }
            set
            {
                if (currencyCode == null)
                {
                    throw new ArgumentNullException("currencyCode");
                }

                string key = currencyCode.ToUpperInvariant();
                _amounts[key] = value;
            }
        }
/*
        public void Add(string currencyCode, decimal delta)
        {
            this[currencyCode] = this[currencyCode] + delta;
        }
*/
/*
        public ICollection<string> Currencies
        {
            get { return _amounts.Keys; }
        }
*/
    }
}
