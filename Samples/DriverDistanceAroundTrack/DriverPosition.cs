using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DriverDistanceAroundTrack
{
    internal record DriverPosition
    {
        public DriverPosition(string carNumber, string driverName)
        {
            CarNumber = carNumber;
            DriverName = driverName;
        }
        public string CarNumber { get; init; }
        public string DriverName { get; init; }
        public float LapDistance { get; set; }

    }
}
