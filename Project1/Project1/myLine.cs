using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Project1
{
    public class myLine
    {
        public myPoint startPoint;
        public myPoint endPoint;
        public bool isReference;
        public bool isTapered;
        public double taperLength;

        public myLine()
        {
            isReference = false;
            startPoint = new myPoint();
            endPoint = new myPoint();
        }

        public myLine(myPoint point1, myPoint point2, bool isRef = false)
        {
            startPoint = point1;
            endPoint = point2;
            isReference = isRef;
        }
    }
}
