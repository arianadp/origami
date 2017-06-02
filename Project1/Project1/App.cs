using NXOpen;
using NXOpen.MenuBar;

namespace ME578_Lab7
{
    public class App
    {
        public static int Startup()
        {
            UI.GetUI().MenuBarManager.AddMenuAction("Thick_Origami_Generator", PartGen);
            return 0;
        }

        public static int GetUnloadOption(string unused)
        {
            return (int)Session.LibraryUnloadOption.AtTermination;
        }

        private static MenuBarManager.CallbackStatus PartGen(MenuButtonEvent ev)
        {
            TOMMT_UI dialog = new TOMMT_UI();
            dialog.Show();

            //grab values from dialog
            string saveLocation = dialog.getSaveLocation();
            Sketch borderSketch = dialog.getBorderSketch();
            Sketch patternSketch = dialog.getPatternSketch();
            double maxAngle = dialog.getMaxFoldAngle();
            double maxThickness = dialog.getMaxThickness();
            bool okPressed = dialog.getOKPressed();
            //call journal file
            if(okPressed)
                NXJournal.Generate(saveLocation, borderSketch, patternSketch, maxAngle, maxThickness);

            return MenuBarManager.CallbackStatus.Continue;
        }
    }
}
