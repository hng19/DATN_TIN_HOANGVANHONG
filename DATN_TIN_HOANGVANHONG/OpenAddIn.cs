using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DATN_TIN_HOANGVANHONG
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class OpenAddIn : IExternalCommand
    {
        private static Form1 _form;

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (_form == null || _form.IsDisposed)
            {
                _form = new Form1(doc, uidoc);
                _form.Show();
            }
            else
            {
                _form.Activate();
            }

            return Result.Succeeded;
        }
    }
}
