// NX 9.0.3.4
// Journal created by arianadp on Fri Feb 17 11:32:46 2017 Mountain Standard Time
//
using System;
using NXOpen;
using System.IO;
using System.Collections.Generic;
using Project1;

public class NXJournal
{
    public static void Generate(string fileName, Sketch sketchBorder, Sketch sketchInner, double maxFoldAngle, double maxThickness)
    {
        Session theSession = Session.GetSession();
        maxFoldAngle = maxFoldAngle * Math.PI / 180;

        //Grab current part as work part
        Part workPart = theSession.Parts.Work;
        Part displayPart = theSession.Parts.Display;

        //sort and separate sketch lines for each sketch
        List<NXOpen.Line> borderLines = new List<NXOpen.Line>();
        List<NXOpen.Line> mainLines = new List<NXOpen.Line>();
        getSketchLines(ref borderLines, ref mainLines, sketchBorder, sketchInner);

        //find inner points (points not on the border)
        NXOpen.UF.UFSession ufSession = NXOpen.UF.UFSession.GetUFSession();
        List<Point3d> points = getInnerPoints(ufSession, borderLines, mainLines);

        //create Vertex for each inner point
        List<Vertex> vertices = createVertices(points, mainLines);
        //create the panels
        List<Panel> panels = createPanels(ref vertices);
        
        //create dumb copies of lines and points so they can be recreated in new parts
        foreach (Panel p in panels)
        {
            p.linesToDumb();
            p.pointsToDumb();
            p.connectDots(workPart, borderLines, ufSession);
        }
        foreach (Vertex v in vertices)
        {
            v.linesToDumb();
            v.pointToDumb();
        }

        //remove duplicate panels
        panels = removeDuplicatePanels(panels);
        
        deletePreviousFiles(panels, fileName);

        areLinesTapered(ref panels);
        assignTaperLengths(ref panels, vertices, maxFoldAngle, maxThickness);

        //create new part, sketch, and extrude each panel
        createParts(theSession, workPart, fileName, maxThickness, maxFoldAngle, panels, vertices);

        assemble(theSession, workPart, fileName, panels.Count);
        
    }

    public static void areLinesTapered(ref List<Panel> panels)
    {
        foreach (Panel p in panels)
        {
            foreach (myLine line in p.dumbLines)
            {
                foreach (myPoint point in p.dumbVertices)
                {
                    if (arePointsEqual(line.startPoint, point))
                    {
                        line.isTapered = true;
                        break;
                    }
                    else if (arePointsEqual(line.endPoint, point))
                    {
                        line.isTapered = true;
                        break;
                    }
                    else
                    {
                        line.isTapered = false;
                    }
                }
            }
        }
        
    }

    public static void assignTaperLengths(ref List<Panel> panels, List<Vertex> vertices, double maxFoldAngle, double maxThickness)
    {
        List<Vertex> verticesTraveled = new List<Vertex>();
        //first vertex
        verticesTraveled.Add(vertices[0]);

        double gamma1 = maxFoldAngle;
        double gamma2 = 2 * Math.Atan(Math.Tan(.5 * gamma1) * (Math.Sin(.5 * (vertices[0].angles[0] + vertices[0].angles[1])))/ (Math.Sin(.5 * (vertices[0].angles[0] - vertices[0].angles[1]))));
        //double gamma2 = maxFoldAngle;
        //double gamma1 = 2 * Math.Atan2(Math.Tan(.5 * gamma2) * (Math.Sin(.5 * (vertices[0].angles[0] - vertices[0].angles[1]))), (Math.Sin(.5 * (vertices[0].angles[0] + vertices[0].angles[1]))));

        double del1 = 0;
        if (gamma1 < 0)
            del1 = Math.PI + gamma1;
        else
            del1 = Math.PI - gamma1;

        double del2 = 0;
        if (gamma2 > 0)
            del2 = Math.PI - gamma2;
        else
            del2 = Math.PI + gamma2;

        double t = maxThickness / 2;

        double taper1 = t / Math.Tan(del1 / 2);
        double taper2 = t / Math.Tan(del2 / 2);

        vertices[0].dumbLines[0].taperLength = taper1;
        vertices[0].dumbLines[2].taperLength = taper1;
        vertices[0].dumbLines[1].taperLength = taper2;
        vertices[0].dumbLines[3].taperLength = taper2;

        foreach (Panel p in panels)
        {
            foreach (myLine line in p.dumbLines)
            {
                if ((areLinesEqual(vertices[0].dumbLines[0], line) || areLinesEqual(vertices[0].dumbLines[2],line)) && line.isTapered && line.taperLength == 0)
                {
                    line.taperLength = taper1;
                }
                else if ((areLinesEqual(vertices[0].dumbLines[1], line) || areLinesEqual(vertices[0].dumbLines[3], line)) && line.isTapered && line.taperLength == 0)
                {
                    line.taperLength = taper2;
                }
            }
        }
        
        //now branching through other vertices
        int result = 0;
        Vertex currentVertex = vertices[result];

        do
        {
            int counter = 0;
            int sharedLineIndex = -1;
            
            do
            {
                int vertexIndex = vertices.FindIndex(x => x.Equals(currentVertex));

                result = connectedVertexFinder(currentVertex.dumbLines[counter],vertexIndex,vertices, ref sharedLineIndex);
                counter++;
                if (result != -1 && verticesTraveled.Contains(vertices[result]))
                {
                    result = -1;
                }
            
            } while(result == -1 && counter < 4);

            if (result != -1)
            {
                double existingTaperLength = currentVertex.dumbLines[counter-1].taperLength;
                currentVertex = vertices[result];
                verticesTraveled.Add(currentVertex);
                

                if (existingTaperLength == 0)
                {
                    //throw error
                }
                else
                {
                    backCalc(existingTaperLength, sharedLineIndex, ref panels, ref currentVertex, maxThickness);
                }
            }
            else
            {
                //continue? only one vertex
                if (vertices.ToArray().Length == 1)
                {
                    break;
                }
                else
                {
                    int currentVertexIndex = verticesTraveled.FindIndex(x => x.Equals(currentVertex));
                    currentVertex = verticesTraveled[currentVertexIndex - 1];
                }
            }

        } while(verticesTraveled.ToArray().Length != vertices.ToArray().Length);
        
    }

    public static void backCalc(double existingTaperLength, int index, ref List<Panel> panels, ref Vertex newVertex, double maxThickness)
    {
        double taper1, taper2, gamma1, gamma2;
        if (index == 0 || index == 2)
        {
            gamma1 = Math.PI - 2 * Math.Atan(maxThickness / (2 * existingTaperLength));
            gamma2 = 2 * Math.Atan(Math.Tan(.5 * gamma1) * (Math.Sin(.5 * (newVertex.angles[0] + newVertex.angles[1])))/ (Math.Sin(.5 * (newVertex.angles[0] - newVertex.angles[1]))));
        }
        else //index 1 or 3
        {
            gamma2 = Math.PI - 2 * Math.Atan(maxThickness / (2 * existingTaperLength));
            gamma1 = 2 * Math.Atan(Math.Tan(.5 * gamma2) * (Math.Sin(.5 * (newVertex.angles[0] - newVertex.angles[1])))/ (Math.Sin(.5 * (newVertex.angles[0] + newVertex.angles[1]))));
        }

        double del1 = 0;
        if (gamma1 < 0)
            del1 = Math.PI + gamma1;
        else
            del1 = Math.PI - gamma1;

        double del2 = 0;
        if (gamma2 < 0)
            del2 = Math.PI + gamma2;
        else
            del2 = Math.PI - gamma2;

        taper1 = maxThickness / (2 * Math.Tan(del1 / 2));
        taper2 = maxThickness / (2 * Math.Tan(del2 / 2));

        setTaperValues(ref panels, taper1, taper2, ref newVertex);
    }

    public static void setTaperValues(ref List<Panel> panels, double taper1, double taper2, ref Vertex newVertex)
    {
        newVertex.dumbLines[0].taperLength = taper1;
        newVertex.dumbLines[2].taperLength = taper1;
        newVertex.dumbLines[1].taperLength = taper2;
        newVertex.dumbLines[3].taperLength = taper2;
        foreach (Panel p in panels)
        {
            foreach (myLine line in p.dumbLines)
            {
                if ((areLinesEqual(newVertex.dumbLines[0], line) || areLinesEqual(newVertex.dumbLines[2], line)) && line.isTapered && line.taperLength == 0)
                {
                    line.taperLength = taper1;
                }
                else if ((areLinesEqual(newVertex.dumbLines[1], line) || areLinesEqual(newVertex.dumbLines[3], line)) && line.isTapered && line.taperLength == 0)
                {
                    line.taperLength = taper2;
                }
                else if((areLinesEqual(newVertex.dumbLines[0], line) || areLinesEqual(newVertex.dumbLines[2], line)) && line.isTapered)
                {
                    if (taper1 > line.taperLength)
                    {
                        line.taperLength = taper1;
                    }
                }
                else if ((areLinesEqual(newVertex.dumbLines[1], line) || areLinesEqual(newVertex.dumbLines[3], line)) && line.isTapered)
                {
                    if (taper2 > line.taperLength)
                    {
                        line.taperLength = taper2;
                    }
                }
            }
        }
    }

    public static int connectedVertexFinder(myLine findThisLine, int knownVertexIndex, List<Vertex> vertices, ref int sharedLineIndex)
    {
        int connectedVertexIndex = -1;
        
        for (int i = 0; i < vertices.ToArray().Length; i++)
        {
            if (i != knownVertexIndex)
            {
                for (int j = 0; j < 4; j++)
                {
                    if (areLinesEqual(vertices[i].dumbLines[j], findThisLine))
                    {
                        sharedLineIndex = j;
                        return i;
                    }
                }
            }
        }

        return connectedVertexIndex;
    }

    public static void assemble(Session theSession, Part workPart, string fileName, double numPanels)
    {
        FileNew fileNew2 = theSession.Parts.FileNew();
        fileNew2.TemplateFileName = "model-plain-1-mm-template.prt";
        fileNew2.ApplicationName = "ModelTemplate";
        fileNew2.Units = Part.Units.Millimeters;
        fileNew2.UsesMasterModel = "No";
        fileNew2.TemplateType = FileNewTemplateType.Item;

        //Name new file
        fileNew2.NewFileName = fileName + "assembly.prt";

        fileNew2.MasterFileName = "";
        fileNew2.UseBlankTemplate = false;
        string partFileName2 = fileNew2.NewFileName;

        //Display new part
        fileNew2.MakeDisplayedPart = true;
        NXObject nXObject2 = fileNew2.Commit();
        fileNew2.Destroy();
        workPart = theSession.Parts.Work;
        NXOpen.Session.UndoMarkId markId2;
        markId2 = theSession.SetUndoMark(NXOpen.Session.MarkVisibility.Visible, "Add Component");

        for (int i = 0; i < numPanels; i++)
        {
            PartLoadStatus partLoadStatus1;
            BasePart basePart1 = theSession.Parts.OpenBase(fileName + "testPart" + i + ".prt", out partLoadStatus1);
            partLoadStatus1.Dispose();
        }

        int nErrs1 = theSession.UpdateManager.DoUpdate(markId2);
        int nErrs2 = theSession.UpdateManager.DoUpdate(markId2);
        int nErrs3 = theSession.UpdateManager.DoUpdate(markId2);
        int nErrs4 = theSession.UpdateManager.DoUpdate(markId2);
        int nErrs5 = theSession.UpdateManager.DoUpdate(markId2);
        int nErrs6 = theSession.UpdateManager.DoUpdate(markId2);
        int nErrs7 = theSession.UpdateManager.DoUpdate(markId2);
        int nErrs8 = theSession.UpdateManager.DoUpdate(markId2);
        int nErrs9 = theSession.UpdateManager.DoUpdate(markId2);

        List<NXOpen.Assemblies.Component> components = new List<NXOpen.Assemblies.Component>();
        for (int i = 0; i < numPanels; i++)
        {
            Point3d basePoint1 = new Point3d(0.0, 0.0, 0.0);
            Matrix3x3 orientation1;
            orientation1.Xx = 1.0;
            orientation1.Xy = 0.0;
            orientation1.Xz = 0.0;
            orientation1.Yx = 0.0;
            orientation1.Yy = 1.0;
            orientation1.Yz = 0.0;
            orientation1.Zx = 0.0;
            orientation1.Zy = 0.0;
            orientation1.Zz = 1.0;
            PartLoadStatus partLoadStatus10;
            NXOpen.Assemblies.Component component1 = workPart.ComponentAssembly.AddComponent(fileName + "testPart" + i + ".prt", "MODEL", "TESTPART" + i, basePoint1, orientation1, -1, out partLoadStatus10, true);

            components.Add(component1);

            partLoadStatus10.Dispose();
        }

        NXObject[] objects1 = new NXObject[0];
        int nErrs10 = theSession.UpdateManager.AddToDeleteList(objects1);

        NXObject[] objects2 = new NXObject[0];
        int nErrs11 = theSession.UpdateManager.AddToDeleteList(objects2);

        NXObject[] objects3 = new NXObject[0];
        int nErrs12 = theSession.UpdateManager.AddToDeleteList(objects3);

        NXObject[] objects4 = new NXObject[0];
        int nErrs13 = theSession.UpdateManager.AddToDeleteList(objects4);

        NXObject[] objects5 = new NXObject[0];
        int nErrs14 = theSession.UpdateManager.AddToDeleteList(objects5);

        NXObject[] objects6 = new NXObject[0];
        int nErrs15 = theSession.UpdateManager.AddToDeleteList(objects6);

        NXObject[] objects7 = new NXObject[0];
        int nErrs16 = theSession.UpdateManager.AddToDeleteList(objects7);

        NXObject[] objects8 = new NXObject[0];
        int nErrs17 = theSession.UpdateManager.AddToDeleteList(objects8);

        NXObject[] objects9 = new NXObject[0];
        int nErrs18 = theSession.UpdateManager.AddToDeleteList(objects9);

        NXOpen.Positioning.ComponentPositioner componentPositioner1;
        componentPositioner1 = workPart.ComponentAssembly.Positioner;

        componentPositioner1.ClearNetwork();

        NXOpen.Assemblies.Arrangement arrangement1 = (NXOpen.Assemblies.Arrangement)workPart.ComponentAssembly.Arrangements.FindObject("Arrangement 1");
        componentPositioner1.PrimaryArrangement = arrangement1;

        componentPositioner1.BeginAssemblyConstraints();

        bool allowInterpartPositioning1;
        allowInterpartPositioning1 = theSession.Preferences.Assemblies.InterpartPositioning;

        NXOpen.Positioning.Network network1;
        network1 = componentPositioner1.EstablishNetwork();

        NXOpen.Positioning.ComponentNetwork componentNetwork1 = (NXOpen.Positioning.ComponentNetwork)network1;
        componentNetwork1.MoveObjectsState = true;

        NXOpen.Assemblies.Component nullAssemblies_Component = null;
        componentNetwork1.DisplayComponent = nullAssemblies_Component;

        componentNetwork1.NetworkArrangementsMode = NXOpen.Positioning.ComponentNetwork.ArrangementsMode.Existing;

        componentNetwork1.MoveObjectsState = true;

        NXOpen.Session.UndoMarkId markId6;
        markId6 = theSession.SetUndoMark(NXOpen.Session.MarkVisibility.Invisible, "Assembly Constraints Update");

        componentNetwork1.Solve();

        componentPositioner1.ClearNetwork();

        int nErrs19;
        nErrs19 = theSession.UpdateManager.AddToDeleteList(componentNetwork1);

        int nErrs20;
        nErrs20 = theSession.UpdateManager.DoUpdate(markId6);

        componentPositioner1.DeleteNonPersistentConstraints();

        int nErrs21;
        nErrs21 = theSession.UpdateManager.DoUpdate(markId6);

        NXOpen.Session.UndoMarkId markId9;
        markId9 = theSession.SetUndoMark(NXOpen.Session.MarkVisibility.Invisible, "Update After Redefine Constraints Dialog");

        int nErrs22;
        nErrs22 = theSession.UpdateManager.DoUpdate(markId9);
        theSession.DeleteUndoMark(markId6, null);
        theSession.DeleteUndoMark(markId9, null);

        PartSaveStatus partSaveStatus4 = workPart.Save(NXOpen.BasePart.SaveComponents.True, NXOpen.BasePart.CloseAfterSave.False);
        partSaveStatus4.Dispose();
    }

    public static void createParts(Session theSession, Part workPart, string fileName, double maxThickness, double maxFoldAngle, List<Panel> panels, List<Vertex> vertices)
    {
        int panelCounter = 0;
        foreach (Panel p in panels)
        {
            FileNew fileNew1 = theSession.Parts.FileNew();
            fileNew1.TemplateFileName = "model-plain-1-mm-template.prt";
            fileNew1.ApplicationName = "ModelTemplate";
            fileNew1.Units = Part.Units.Millimeters;
            fileNew1.UsesMasterModel = "No";
            fileNew1.TemplateType = FileNewTemplateType.Item;

            //Name new file
            fileNew1.NewFileName = fileName + "testPart" + panelCounter + ".prt";

            fileNew1.MasterFileName = "";
            fileNew1.UseBlankTemplate = false;
            string partFileName = fileNew1.NewFileName;

            //Display new part
            fileNew1.MakeDisplayedPart = true;
            NXObject nXObject1 = fileNew1.Commit();
            fileNew1.Destroy();
            workPart = theSession.Parts.Work;

            p.sketchPanel(theSession);
            p.extrude(theSession, maxThickness.ToString());


            NXOpen.Features.Feature[] feats = workPart.Features.GetFeatures();
            NXOpen.Features.Extrude extrude1 = null;
            foreach (NXOpen.Features.Feature feat in feats)
            {
                if (feat is NXOpen.Features.Extrude)
                {
                    extrude1 = (NXOpen.Features.Extrude)feat;
                    break;
                }
            }

            List<myLine> linesToTaper = new List<myLine>();
            linesToTaper = p.dumbLines.FindAll(x => x.isTapered);
            int numTapers = linesToTaper.Count;
            List<Edge> edgesToChamfer = new List<Edge>();
            Edge[] edges = extrude1.GetEdges();

            foreach (myLine line in linesToTaper)
            {
                foreach (Edge edge in edges)
                {
                    Point3d point1, point2;
                    edge.GetVertices(out point1, out point2);

                    if (arePointsEqual(point1, line.startPoint) && arePointsEqual(point2, line.endPoint))
                    {
                        if (line.isReference)
                        {
                            if (Math.Abs(point1.Z) > .0000001)
                            {
                                edgesToChamfer.Add(edge);
                                break;
                            }
                        }
                        else
                        {
                            if (Math.Abs(point1.Z) < .0000001)
                            {
                                edgesToChamfer.Add(edge);
                                break;
                            }
                        }

                    }
                    else if (arePointsEqual(point2, line.startPoint) && arePointsEqual(point1, line.endPoint))
                    {
                        if (line.isReference)
                        {
                            if (Math.Abs(point1.Z) > .0000001)
                            {
                                edgesToChamfer.Add(edge);
                                break;
                            }
                        }
                        else
                        {
                            if (Math.Abs(point1.Z) < .0000001)
                            {
                                edgesToChamfer.Add(edge);
                                break;
                            }
                        }
                    }
                }
            }

            

            for (int j = 0; j < edgesToChamfer.Count; j++)
            {
                NXOpen.Session.UndoMarkId markId1;
                markId1 = theSession.SetUndoMark(NXOpen.Session.MarkVisibility.Visible, "Start");


                NXOpen.Features.ChamferBuilder chamferBuilder1 = workPart.Features.CreateChamferBuilder(null);
                chamferBuilder1.Method = NXOpen.Features.ChamferBuilder.OffsetMethod.EdgesAlongFaces;
                chamferBuilder1.Tolerance = 0.01;

                ScCollector scCollector1 = workPart.ScCollectors.CreateCollector();

                Edge edgeToChamfer = edgesToChamfer[j];
                Edge[] currentEdges = extrude1.GetEdges();

                Edge nullEdge = null;
                Face nullFace = null;
                EdgeChainRule edgeChainRule1 = workPart.ScRuleFactory.CreateRuleEdgeChain(edgeToChamfer, nullEdge, true, nullFace, false);
                SelectionIntentRule[] rules1 = new SelectionIntentRule[1];
                rules1[0] = edgeChainRule1;
                scCollector1.ReplaceRules(rules1, false);
                chamferBuilder1.SmartCollector = scCollector1;
                chamferBuilder1.Option = NXOpen.Features.ChamferBuilder.ChamferOption.TwoOffsets;

                //set chamfer values
                double t = maxThickness / 2;
                chamferBuilder1.FirstOffsetExp.RightHandSide = t.ToString();
                chamferBuilder1.SecondOffsetExp.RightHandSide = linesToTaper[j].taperLength.ToString();
                NXOpen.Features.Feature feature1 = chamferBuilder1.CommitFeature();
                chamferBuilder1.Destroy();

                //check if it worked correctly
                Edge[] newEdges = extrude1.GetEdges();
                for (int k = 0; k < newEdges.Length; k++)
                {

                    foreach (Edge ed in currentEdges)
                    {
                        if (newEdges[k] == ed)
                        {
                            newEdges[k] = null;
                        }
                    }
                }
                bool correct = false;
                foreach (Edge e in newEdges)
                {
                    if (e != null)
                    {
                        Point3d p1, p2;
                        e.GetVertices(out p1, out p2);
                        if (Math.Abs(p1.Z - t) < .001 && Math.Abs(p2.Z - t) < .001)
                        {
                            correct = true;
                            break;
                        }
                    }
                }

                if (!correct)
                {
                    bool marksRecycled1;
                    bool undoUnavailable1;
                    theSession.UndoLastNVisibleMarks(1, out marksRecycled1, out undoUnavailable1);

                    NXOpen.Features.ChamferBuilder chamferBuilder2 = workPart.Features.CreateChamferBuilder(null);
                    chamferBuilder2.Method = NXOpen.Features.ChamferBuilder.OffsetMethod.EdgesAlongFaces;
                    chamferBuilder2.Tolerance = 0.01;

                    ScCollector scCollector2 = workPart.ScCollectors.CreateCollector();

                    rules1[0] = edgeChainRule1;
                    scCollector2.ReplaceRules(rules1, false);
                    chamferBuilder2.SmartCollector = scCollector2;
                    chamferBuilder2.Option = NXOpen.Features.ChamferBuilder.ChamferOption.TwoOffsets;

                    //set chamfer values
                    chamferBuilder2.FirstOffsetExp.RightHandSide = linesToTaper[j].taperLength.ToString();
                    chamferBuilder2.SecondOffsetExp.RightHandSide = t.ToString();
                    NXOpen.Features.Feature feature2 = chamferBuilder2.CommitFeature();
                    chamferBuilder2.Destroy();
                }
                else
                {
                    theSession.DeleteUndoMark(markId1, null);
                }
            }

            panelCounter++;

            PartSaveStatus partSaveStatus3 = workPart.Save(NXOpen.BasePart.SaveComponents.True, NXOpen.BasePart.CloseAfterSave.True);
            partSaveStatus3.Dispose();
        }
    }

    public static void deletePreviousFiles(List<Panel> panels, string fileName)
    {
        for (int i = 0; i < panels.Count; i++)
        {
            string name = fileName + "testPart" + i + ".prt";
            if (File.Exists(name))
            {
                File.Delete(name);
            }
        }
        if (File.Exists(fileName + "assembly.prt"))
        {
            string name = fileName + "assembly.prt";
            File.Delete(name);
        }
    }

    public static List<Panel> removeDuplicatePanels(List<Panel> panels)
    {
        for (int n = 0; n < panels.Count; n++)
        {

            // check each panel for matching lines
            int numVertices = panels[n].vertices.Count;
            List<Panel> duplicatePanels = new List<Panel>();

            if (numVertices == 2)
            {
                duplicatePanels = panels.FindAll(x => (x.lines.Contains(panels[n].lines[0]) && x.lines.Contains(panels[n].lines[1]) && x.lines.Contains(panels[n].lines[2])));
                duplicatePanels.RemoveAt(0);
            }
            else if (numVertices > 2)
            {
                foreach (Panel p in panels)
                {
                    List<myLine> duplicateLines = p.dumbLines.FindAll(x => areLinesEqual(x, panels[n].dumbLines[0]) || areLinesEqual(x, panels[n].dumbLines[1]) || areLinesEqual(x, panels[n].dumbLines[2]) || areLinesEqual(x, panels[n].dumbLines[3]));
                    if (duplicateLines.Count == 4)
                    {
                        duplicatePanels.Add(p);
                    }

                }

                duplicatePanels.RemoveAt(0);
            }

            foreach (Panel p in duplicatePanels)
            {
                panels.Remove(p);
            }
        }

        return panels;
    }

    public static List<Panel> createPanels(ref List<Vertex> vertices)
    {
        List<Panel> panels = new List<Panel>();
        for (int i = 0; i < vertices.Count; i++)
        {
            for (int k = 0; k < 4; k++)
            {
                Panel panel = new Panel();
                //add first vertex
                panel.vertices.Add(vertices[i]);
                //find first two lines
                if (k < 3)
                {
                    panel.lines.Add(vertices[i].lines[k]);
                    panel.lines.Add(vertices[i].lines[k + 1]);
                }
                else
                {
                    panel.lines.Add(vertices[i].lines[k]);
                    panel.lines.Add(vertices[i].lines[0]);
                }

                getPanelLines(ref panel, ref vertices);
                panels.Add(panel);
            }
        }
        return panels;
    }

    public static bool getPanelVertices(ref Panel panel, ref List<Vertex> vertices, int lineIndex)
    {
        bool gotAdded = false;
        foreach (Vertex ver in vertices)
        {
            if (ver.lines.Contains(panel.lines[lineIndex]) && !panel.vertices.Contains(ver))
            {
                panel.vertices.Add(ver);
                gotAdded = true;
                break;
            }
        }
        return gotAdded;
    }

    public static void getPanelLines(ref Panel panel, ref List<Vertex> vertices)
    {
        bool gotAdded = getPanelVertices(ref panel, ref vertices, 0);

        int lineNum = 0;

        while (gotAdded)
        {
            int commonLineIndex = 0;
            int lastIndex = panel.vertices.ToArray().Length - 1;
            for (int j = 0; j < 4; j++)
            {
                if (panel.vertices[lastIndex].lines[j].Equals(panel.lines[lineNum]))
                {
                    commonLineIndex = j;
                    break;
                }
            }

            if (commonLineIndex != 0)
            {
                Line newLine = panel.vertices[lastIndex].lines[commonLineIndex - 1];
                if (!panel.lines.Exists(x => areLinesEqual(x, newLine)))
                {
                    panel.lines.Add(newLine);
                }
                else
                    break;
            }
            else
            {
                Line newLine = panel.vertices[lastIndex].lines[3];
                if (!panel.lines.Exists(x => areLinesEqual(x, newLine)))
                {
                    panel.lines.Add(newLine);
                }
                else
                    break;
            }

            if (lineNum == 0)
                lineNum += 2;
            else
                lineNum++;

            gotAdded = getPanelVertices(ref panel, ref vertices, panel.lines.ToArray().Length - 1);
        }
        //switching to line 1 to check for vertices in the other direction
        int prevLineNum = lineNum;
        if (lineNum == 0)
            prevLineNum = 1;
        lineNum = 1;
        gotAdded = getPanelVertices(ref panel, ref vertices, 1);

        while (gotAdded)
        {
            int commonLineIndex = 0;
            int lastIndex = panel.vertices.ToArray().Length - 1;
            for (int j = 0; j < 4; j++)
            {
                if (panel.vertices[lastIndex].lines[j].Equals(panel.lines[lineNum]))
                {
                    commonLineIndex = j;
                    break;
                }
            }

            if (commonLineIndex != 3)
            {
                panel.lines.Add(panel.vertices[lastIndex].lines[commonLineIndex + 1]);
            }
            else
            {
                panel.lines.Add(panel.vertices[lastIndex].lines[0]);
            }

            prevLineNum++;
            lineNum = prevLineNum;

            gotAdded = getPanelVertices(ref panel, ref vertices, panel.lines.ToArray().Length - 1);
        }

    }

    public static List<Vertex> createVertices(List<Point3d> points, List<Line> mainLines)
    {
        List<Vertex> vertices = new List<Vertex>();
        int counter = 1;
        foreach (Point3d point in points)
        {
            Vertex vert = new Vertex();
            vert.name = "Vertex_ " + counter;
            vert.point = point;
            vert.findLines(mainLines);
            vertices.Add(vert);
            counter++;
        }
        return vertices;
    }
    
    public static void getSketchLines(ref List<Line> borderLines, ref List<Line> mainLines, Sketch sketchBorder, Sketch sketchInner)
    {
        NXObject[] geomBorder = sketchBorder.GetAllGeometry();
        //grab each line from the border sketch
        foreach (var item in geomBorder)
        {
            if (item is Line)
            {
                borderLines.Add((Line)item);
            }
        }
        
        NXObject[] geomMain = sketchInner.GetAllGeometry();
        //grab lines from the crease sketch that aren't on the border
        foreach (var item in geomMain)
        {
            if (item is Line && !borderLines.Exists(x => areLinesEqual((Line)x, (Line)item)))
            {
                mainLines.Add((Line)item);
            }
        }
    }

    public static List<Point3d> getInnerPoints(NXOpen.UF.UFSession ufSession, List<Line> borderLines, List<Line> mainLines)
    {
        List<Point3d> badPoints = new List<Point3d>();
        foreach (Line line in mainLines)
        {
            int numIntersections;
            double[] intersectData;
            foreach (Line bLine in borderLines)
            {
                ufSession.Modl.IntersectCurveToCurve(bLine.Tag, line.Tag, out numIntersections, out intersectData);
                if (numIntersections != 0)
                {
                    Point3d badPoint = new Point3d(intersectData[0], intersectData[1], intersectData[2]);
                    badPoints.Add(badPoint);
                }
            }
        }

        List<NXOpen.Point3d> points = new List<NXOpen.Point3d>();
        foreach (Line line in mainLines)
        {
            if (!badPoints.Exists(x => arePointsEqual(x, line.StartPoint)) && !points.Exists(x => arePointsEqual(x, line.StartPoint)))
            {
                points.Add(line.StartPoint);
            }

            if (!badPoints.Exists(x => arePointsEqual(x, line.EndPoint)) && !points.Exists(x => arePointsEqual(x, line.EndPoint)))
            {
                points.Add(line.EndPoint);
            }
        }
        return points;
    }

    public static bool arePointsEqual(Point3d point1, Point3d point2)
    {
        if (Math.Abs(point1.X - point2.X) < .000001 && Math.Abs(point1.Y - point2.Y) < .000001)
            return true;
        else
            return false;
    }
    public static bool arePointsEqual(Point3d point1, myPoint point2)
    {
        if (Math.Abs(point1.X - point2.X) < .000001 && Math.Abs(point1.Y - point2.Y) < .000001)
            return true;
        else
            return false;
    }
    public static bool arePointsEqual(myPoint point1, myPoint point2)
    {
        if (Math.Abs(point1.X - point2.X) < .000001 && Math.Abs(point1.Y - point2.Y) < .000001)
            return true;
        else
            return false;
    }
    public static bool arePointsEqual(myPoint point1, Point3d point2)
    {
        if (Math.Abs(point1.X - point2.X) < .000001 && Math.Abs(point1.Y - point2.Y) < .000001)
            return true;
        else
            return false;
    }

    public static bool areLinesEqual(Line line1, Line line2)
    {
        if ((arePointsEqual(line1.StartPoint, line2.StartPoint) && arePointsEqual(line1.EndPoint, line2.EndPoint)) || (arePointsEqual(line1.StartPoint, line2.EndPoint) && arePointsEqual(line1.EndPoint, line2.StartPoint)))
        {
            return true;
        }
        else
            return false;
    }
    public static bool areLinesEqual(Line line1, myLine line2)
    {
        if ((arePointsEqual(line1.StartPoint, line2.startPoint) && arePointsEqual(line1.EndPoint, line2.endPoint)) || (arePointsEqual(line1.StartPoint, line2.endPoint) && arePointsEqual(line1.EndPoint, line2.startPoint)))
        {
            return true;
        }
        else
            return false;
    }
    public static bool areLinesEqual(myLine line1, Line line2)
    {
        if ((arePointsEqual(line1.startPoint, line2.StartPoint) && arePointsEqual(line1.endPoint, line2.EndPoint)) || (arePointsEqual(line1.startPoint, line2.EndPoint) && arePointsEqual(line1.endPoint, line2.StartPoint)))
        {
            return true;
        }
        else
            return false;
    }
    public static bool areLinesEqual(myLine line1, myLine line2)
    {
        if ((arePointsEqual(line1.startPoint, line2.startPoint) && arePointsEqual(line1.endPoint, line2.endPoint)) || (arePointsEqual(line1.startPoint, line2.endPoint) && arePointsEqual(line1.endPoint, line2.startPoint)))
        {
            return true;
        }
        else
            return false;
    }

    public static int GetUnloadOption(string dummy) { return (int)Session.LibraryUnloadOption.Immediately; }
}
