using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LaundryLoadBookings
{
    class ItemModel
    {
        public string ItemNumber { get; set; }

        public int RepackFactor { get; set; }

        public string Buyer { get; set; }

        public string Vendor { get; set; }
    }
}
