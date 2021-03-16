using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LaundryLoadBookings
{
    public class OrdersToProcess
    {
        public string customerNumber { get; set; }

        public string itemNumber { get; set; }

        public int qty { get; set; }
    }
}
