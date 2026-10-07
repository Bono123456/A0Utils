using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace A0Utils.Wpf.Models
{
    public sealed class PriceModel
    {
        public string Name { get; set; }
        public List<(DateTime, DateTime)> Dates { get; set; } = [];
    }
}
