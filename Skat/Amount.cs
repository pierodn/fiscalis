using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TaxEngine
{
    public struct Amount
    {
        private readonly decimal dkk;
        private readonly decimal fxRate; // DKK per 1 USD

        public Amount(decimal dkk, decimal fxRate)
        {
            this.dkk = dkk;
            this.fxRate = fxRate;
        }

        public decimal DKK { get { return dkk; } }
        public decimal FxRate { get { return fxRate; } }

        public decimal USD
        {
            get { return dkk / fxRate; }
        }

        public override string ToString()
        {
            return string.Format("{0:F2} DKK / {1:F2} USD (rate {2})", dkk, USD, fxRate);
        }
    }
}
