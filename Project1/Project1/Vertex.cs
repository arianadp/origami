using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NXOpen;

namespace Project1
{
    public class Vertex
    {
        //member variables
        public string name;
        public NXOpen.Point3d point;
        public List<NXOpen.Line> lines;
        public NXOpen.Line oddLine;
        public List<double> angles;
        public bool refIsOdd;
        public List<myLine> dumbLines;
        public myPoint dumbPoint;

        public struct smartLine
        {
            public NXOpen.VectorArithmetic.Vector3 line;
            public double quadrant;
            public double angle;
            public int index;
        }

        public Vertex()
        {
            name = "";
            point = new NXOpen.Point3d(0,0,0);
            lines = new List<Line>();
            angles = new List<double>();
            refIsOdd = false;
            dumbLines = new List<myLine>();
        }


        public void linesToDumb()
        {
            foreach (Line line in lines)
            {
                myLine dumbLine = new myLine();
                dumbLine.startPoint.X = line.StartPoint.X;
                dumbLine.startPoint.Y = line.StartPoint.Y;
                dumbLine.startPoint.Z = line.StartPoint.Z;
                dumbLine.endPoint.X = line.EndPoint.X;
                dumbLine.endPoint.Y = line.EndPoint.Y;
                dumbLine.endPoint.Z = line.EndPoint.Z;
                dumbLine.isReference = line.IsReference;
                dumbLine.isTapered = true;

                dumbLines.Add(dumbLine);
            }
        }

        public void pointToDumb()
        {
             dumbPoint = new myPoint();
             dumbPoint.X = point.X;
             dumbPoint.Y = point.Y;
             dumbPoint.Z = point.Z;
            
        }

        public void findLines(List<NXOpen.Line> allLines)
        {
            foreach (Line line in allLines)
            {
                if (NXJournal.arePointsEqual(line.StartPoint,point) || NXJournal.arePointsEqual(line.EndPoint,point))
                {
                    lines.Add(line);
                }
            }

            findOddLine();
        }

        public void findOddLine()
        {
            int refCounter = 0;
            foreach (Line line in lines)
            {
                if (line.IsReference)
                {
                    refCounter++;
                }
            }

            if (refCounter == 1)
            {
                refIsOdd = true;
                oddLine = lines.Find(x => x.IsReference);
            }
            else
            {
                refIsOdd = false;
                oddLine = lines.Find(x => !(x.IsReference));
            }

            orderLines();
        }

        public void orderLines()
        {
            List<NXOpen.Line> tempLines = new List<Line>();
            foreach (Line line in lines)
            {
                tempLines.Add(line);
            }
            lines[0] = oddLine;
            int counter = 1;
            for (int i = 0; i < 4; i++)
            {
                if(!tempLines[i].Equals(oddLine))
                {
                    lines[counter] = tempLines[i];
                    counter++;
                }
            }
            //grab vector from origin to vertex to translate everything to origin
            NXOpen.VectorArithmetic.Vector3 vecOrigin = new NXOpen.VectorArithmetic.Vector3(point.X, point.Y, point.Z);
            List<NXOpen.VectorArithmetic.Vector3> vecs = new List<NXOpen.VectorArithmetic.Vector3>();

            foreach (Line line in lines)
            {
                NXOpen.VectorArithmetic.Vector3 vecLine;
                if (NXJournal.arePointsEqual(line.StartPoint,point))
                {
                    vecLine = new NXOpen.VectorArithmetic.Vector3(line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z);
                }
                else
                {
                    vecLine = new NXOpen.VectorArithmetic.Vector3(line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z);
                }

                vecs.Add(vecLine);
            }

            for (int i = 0; i < vecs.ToArray().Length; i++)
            {
                vecs[i] = vecs[i] - vecOrigin;
            }

            NXOpen.VectorArithmetic.Vector3 yAxis = new NXOpen.VectorArithmetic.Vector3(0, 1, 0);
            //find angle for rotation
            #region rotation angle
            double cosineAngle = vecs[0].Dot(yAxis) / (magnitude(vecs[0]) * magnitude(yAxis));
            double angle = Math.Abs(Math.Acos(cosineAngle));
            double quadOddLine = quadrant(vecs[0]);
            double theta;
            if (quadOddLine == 5) //quadrant 1
            {
                theta = angle;
            }
            else if (quadOddLine == 2)
            {
                theta = -angle;
            }
            else if (quadOddLine == 3)
            {
                theta = -angle;
            }
            else if (quadOddLine == 4)
            {
                theta = angle;
            }
            else
            {
                if (vecs[0].x < .0000001 && vecs[0].x > -.0000001)
                {
                    if (vecs[0].y > 0)
                    {
                        theta = 0;
                    }
                    else
                    {
                        theta = Math.PI;
                    }
                }
                else //y == 0
                {
                    if (vecs[0].x > 0)
                    {
                        theta = Math.PI / 2;
                    }
                    else
                    {
                        theta = -Math.PI / 2;
                    }
                }
            }
            #endregion
            //rotating vectors
            for (int i = 0; i < 4; i++)
            {
                double x = vecs[i].x;
                double y = vecs[i].y;
                double z = vecs[i].z;

                //I don't know why theta needs to be negative here, it makes no sense, but it doesn't work otherwise.
                vecs[i].x = x * Math.Cos(-theta) + y * Math.Sin(-theta);
                vecs[i].y = -x * Math.Sin(-theta) + y * Math.Cos(-theta);
            }

            //order lines by quadrant
            tempLines.Clear();
            foreach (Line line in lines)
            {
                tempLines.Add(line);
            }

            List<smartLine> smartLines = new List<smartLine>();

            for (int i = 0; i < 3; i++)
            {
                smartLine line1 = new smartLine();
                line1.line = vecs[i+1];
                line1.quadrant = quadrant(vecs[i+1]);
                
                double cosineAngle1 = vecs[i+1].Dot(yAxis) / (magnitude(vecs[i+1]) * magnitude(yAxis));
                double angle1 = Math.Abs(Math.Acos(cosineAngle1));
                line1.angle = angle1;
                line1.index = i + 1;

                smartLines.Add(line1);
            } 

            smartLines = smartLines.OrderBy(x => x.quadrant).ThenBy(x => x.angle).ToList();

            int index1 = smartLines[0].index;
            int index2 = smartLines[1].index;
            int index3 = smartLines[2].index;

            lines[1] = tempLines[index1];
            lines[2] = tempLines[index2];
            lines[3] = tempLines[index3];

            calculateAngles(smartLines);
        }

        public double quadrant(NXOpen.VectorArithmetic.Vector3 vec)
        {
            if (vec.x < .0000001 && vec.x > -.0000001 && vec.y > 0)
            {
                return 1.5;
            }
            else if (vec.x < .0000001 && vec.x > -.0000001 && vec.y < 0)
            {
                return 3.5;
            }
            else if (vec.y < .0000001 && vec.y > -.0000001 && vec.x > 0)
            {
                return 4.5;
            }
            else if (vec.y < .0000001 && vec.y > -.0000001 && vec.x < 0)
            {
                return 2.5;
            }
            else if (vec.x < 0 && vec.y < 0)
            {
                return 3;
            }
            else if (vec.x < 0 && vec.y > 0)
            {
                return 2;
            }
            else if (vec.x > 0 && vec.y > 0) //quadrant 1
            {
                return 5;
            }
            else if (vec.x > 0 && vec.y < 0)
            {
                return 4;
            }
            else
            {
                return -1;
            }
   
        }

        public double magnitude(NXOpen.VectorArithmetic.Vector3 vec)
        {
            double mag = Math.Pow(vec.x * vec.x + vec.y * vec.y + vec.z * vec.z,.5);
            return mag;
        }
        public void calculateAngles(List<smartLine> smartLines)
        {
            //angle 1
            if (smartLines[0].quadrant == 2)
            {
                angles.Add(smartLines[0].angle);
            }
            else if (smartLines[0].quadrant == 2.5)
            {
                angles.Add(Math.PI / 2);
            }
            else
            {
                angles.Add(smartLines[0].angle);
            }

            //angle 2
            if (smartLines[1].quadrant == 2)
            {
                angles.Add(smartLines[1].angle - angles[0]);
            }
            else if (smartLines[1].quadrant == 2.5)
            {
                angles.Add(Math.PI / 2 - angles[0]);
            }
            else if(smartLines[1].quadrant == 3)
            {
                angles.Add(smartLines[1].angle - angles[0]);
            }
            else if (smartLines[1].quadrant == 3.5)
            {
                angles.Add(Math.PI - angles[0]);
            }
            else if (smartLines[1].quadrant == 4)
            {
                angles.Add(2 * Math.PI - smartLines[1].angle - angles[0]);
            }
            else if (smartLines[1].quadrant == 4.5)
            {
                angles.Add(3 * Math.PI / 2 - angles[0]);
            }
            else
            {
                angles.Add(2 * Math.PI - smartLines[1].angle - angles[0]);
            }
            
            
        }

    }
}
