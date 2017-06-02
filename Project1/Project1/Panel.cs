using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NXOpen;
using Project1;

namespace Project1
{
    public class Panel
    {
        public Sketch sketch;
        public List<Vertex> vertices;
        public List<NXOpen.Line> lines;
        public List<myPoint> dumbVertices;
        public List<myLine> dumbLines;
        

        public Panel()
        {
            vertices = new List<Vertex>();
            lines = new List<Line>();
            dumbLines = new List<myLine>();
            dumbVertices = new List<myPoint>();
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

                dumbLines.Add(dumbLine);
            }
        }

        public void pointsToDumb()
        {
            foreach (Vertex ver in vertices)
            {
                myPoint dumbPoint = new myPoint();

                if (ver.point.X < .0000001 && ver.point.X > -.0000001)
                    dumbPoint.X = 0;
                else
                {
                    dumbPoint.X = ver.point.X;
                }

                if (ver.point.Y < .0000001 && ver.point.Y > -.0000001)
                    dumbPoint.Y = 0;
                else
                {
                    dumbPoint.Y = ver.point.Y;
                }
                dumbPoint.Z = ver.point.Z;

                

                dumbVertices.Add(dumbPoint);
            }
        }

        public void connectDots(NXOpen.Part workPart, List<Line> borderLines, NXOpen.UF.UFSession ufSession)
        {
            List<myPoint> points = new List<myPoint>();
            List<myPoint> endPoints = new List<myPoint>();

            foreach (myLine line in dumbLines)
            {
                points.Add(line.startPoint);
                points.Add(line.endPoint);
            }
            //find end points that still need to be connected
            foreach (myPoint point in points)
            {
                List<myPoint> tempPoints = points.FindAll(x => (NXJournal.arePointsEqual(x,point)));
                if (tempPoints.Count == 1)
                {
                    endPoints.Add(point);
                }
            }

            if (endPoints.ToArray().Length == 2)
            {
                if (vertices.Count > 1)
                {
                    myLine line1 = new myLine();

                    line1.startPoint.X = endPoints[0].X;
                    line1.startPoint.Y = endPoints[0].Y;
                    line1.startPoint.Z = endPoints[0].Z;
                    line1.endPoint.X = endPoints[1].X;
                    line1.endPoint.Y = endPoints[1].Y;
                    line1.endPoint.Z = endPoints[1].Z;

                    dumbLines.Add(line1);

                }
                else
                {
                    int numIntersections;
                    double[] intersectData;
                    List<Line> intersectLines1 = new List<Line>();
                    List<Line> intersectLines2 = new List<Line>();
                    foreach (Line bLine in borderLines)
                    {
                        ufSession.Modl.IntersectCurveToCurve(bLine.Tag, lines.ToArray()[0].Tag, out numIntersections, out intersectData);
                        if (numIntersections != 0)
                        {
                            intersectLines1.Add(bLine);
                        }

                        ufSession.Modl.IntersectCurveToCurve(bLine.Tag, lines.ToArray()[1].Tag, out numIntersections, out intersectData);
                        if (numIntersections != 0)
                        {
                            intersectLines2.Add(bLine);
                        }

                    }

                    //if intersect lines have duplicates between the two, just draw a line between the end points (if triangular shape)
                    List<Line> duplicateLines = intersectLines1.Intersect(intersectLines2).ToList();
                    if (duplicateLines.ToArray().Length != 0)
                    {
                        myLine line1 = new myLine();
                        line1.startPoint.X = endPoints[0].X;
                        line1.startPoint.Y = endPoints[0].Y;
                        line1.startPoint.Z = endPoints[0].Z;
                        line1.endPoint.X = endPoints[1].X;
                        line1.endPoint.Y = endPoints[1].Y;
                        line1.endPoint.Z = endPoints[1].Z;
                        dumbLines.Add(line1);
                    }
                    else
                    {
                        myPoint dumbIntersectPoint = new myPoint();
                        bool breakNow = false;
                        foreach (Line line in intersectLines1)
                        {
                            foreach (Line line2 in intersectLines2)
                            {
                                ufSession.Modl.IntersectCurveToCurve(line.Tag, line2.Tag, out numIntersections, out intersectData);
                                if (numIntersections != 0)
                                {
                                    dumbIntersectPoint.X = intersectData[0];
                                    dumbIntersectPoint.Y = intersectData[1];
                                    dumbIntersectPoint.Z = intersectData[2];
                                    breakNow = true;
                                    break;
                                }
                            }

                            if (breakNow)
                                break;
                        }

                        myLine lastLine1 = new myLine(dumbIntersectPoint, endPoints[0]);
                        myLine lastLine2 = new myLine(dumbIntersectPoint, endPoints[1]);

                        dumbLines.Add(lastLine1);
                        dumbLines.Add(lastLine2);
                    }
                }
            }
            
        }

        public void sketchPanel(Session theSession)
        { 
            //Grab current part as work part
            Part workPart = theSession.Parts.Work;
            Part displayPart = theSession.Parts.Display;

            //create empty sketch on plane
            SketchInPlaceBuilder sketchInPlaceBuilder1 = workPart.Sketches.CreateNewSketchInPlaceBuilder(null);

            DatumPlane datumPlane1 = (DatumPlane)workPart.Datums.FindObject("DATUM_CSYS(0) YZ plane");
            sketchInPlaceBuilder1.PlaneOrFace.SetValue(datumPlane1, workPart.ModelingViews.WorkView, new Point3d(0, 0, 0));

            NXOpen.Features.DatumCsys datumCsys1 = (NXOpen.Features.DatumCsys)workPart.Features.FindObject("DATUM_CSYS(0)");
            NXOpen.Point point = (NXOpen.Point)datumCsys1.FindObject("POINT 1");
            sketchInPlaceBuilder1.SketchOrigin = point;

            sketchInPlaceBuilder1.PlaneOrFace.Value = datumPlane1;

            DatumAxis datumAxis1 = (DatumAxis)workPart.Datums.FindObject("DATUM_CSYS(0) X axis");
            sketchInPlaceBuilder1.Axis.Value = datumAxis1;

            NXObject nXObject2 = sketchInPlaceBuilder1.Commit();

            sketch = (Sketch)nXObject2;
            NXOpen.Features.Feature feature1 = sketch.Feature;
            sketch.Activate(NXOpen.Sketch.ViewReorient.False);
            sketchInPlaceBuilder1.Destroy();

            theSession.ActiveSketch.Preferences.ContinuousAutoDimensioningSetting = false;

            foreach (myLine line in dumbLines)
            {
                Point3d point1 = new Point3d(line.startPoint.X, line.startPoint.Y, line.startPoint.Z);
                Point3d point2 = new Point3d(line.endPoint.X, line.endPoint.Y, line.endPoint.Z);
                Line nxLine = workPart.Curves.CreateLine(point1, point2);
                theSession.ActiveSketch.AddGeometry(nxLine, NXOpen.Sketch.InferConstraintsOption.InferNoConstraints);
            }

            sketch = theSession.ActiveSketch;
            theSession.ActiveSketch.Deactivate(NXOpen.Sketch.ViewReorient.False, NXOpen.Sketch.UpdateLevel.Model);
            return;
        }

        public NXOpen.Features.Feature extrude(Session theSession, string height)
        {
            Part workPart = theSession.Parts.Work;
            //Parameters for the start and end values of the extrude
            string startValue = "0";
            string endValue = height;

            //Create extrude builder using a null feature to create extrude
            NXOpen.Features.ExtrudeBuilder extrudeBuilder1 = workPart.Features.CreateExtrudeBuilder(null);

            //Create a random section
            NXOpen.Section section1 = workPart.Sections.CreateSection(0.0, 0.0, 0.0);
            extrudeBuilder1.Section = section1;

            //Set the boolean operation to Create
            extrudeBuilder1.BooleanOperation.Type = NXOpen.GeometricUtilities.BooleanOperation.BooleanType.Create;

            //Set target body of create to null
            Body[] targetBodies1 = new Body[1];
            targetBodies1[0] = null;
            extrudeBuilder1.BooleanOperation.SetTargetBodies(targetBodies1);

            //Set starting and ending values of the extrude
            extrudeBuilder1.Limits.StartExtend.Value.RightHandSide = startValue;
            extrudeBuilder1.Limits.EndExtend.Value.RightHandSide = endValue;

            //Only allow curves to be selected as the extruded section
            section1.SetAllowedEntityTypes(NXOpen.Section.AllowTypes.OnlyCurves);

            //Get all geometry from the sketch
            //Cast objects as curves (this is a rectangle and only has four lines in the sketch)
            NXObject[] objs = sketch.GetAllGeometry();
            Curve[] curves = new Curve[objs.Length];
            for (int i = 0; i < objs.Length; i++)
            {
                curves[i] = (Curve)objs[i];
            }
            CurveDumbRule rule = workPart.ScRuleFactory.CreateRuleCurveDumb((Curve[])curves);
            section1.AllowSelfIntersection(true);

            //Create a selection intent rule based on the selected curves
            SelectionIntentRule[] rules1 = new SelectionIntentRule[1];
            rules1[0] = rule;
            section1.AddToSection(rules1, null, null, null, new Point3d(0.0, 0.0, 0.0), NXOpen.Section.Mode.Create, false);

            //Set direction for the extrude
            Direction direction1 = workPart.Directions.CreateDirection(sketch, Sense.Forward, NXOpen.SmartObject.UpdateOption.WithinModeling);
            extrudeBuilder1.Direction = direction1;

            //Commit the builder and create the extrude feature
            NXOpen.Features.Feature feature2 = extrudeBuilder1.CommitFeature();

            return feature2;
        }


    }
}
