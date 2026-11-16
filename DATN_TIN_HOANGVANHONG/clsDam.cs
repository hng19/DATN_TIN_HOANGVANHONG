using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DATN_TIN_HOANGVANHONG
{
    public class clsDam : IExternalEventHandler
    {
        public Document Doc { get; set; }
        public string CadFileName { get; set; }
        public string BeamLineLayer { get; set; }
        public string BeamTextLayer { get; set; }
        public FamilySymbol BeamFamilyType { get; set; }
        public Level BeamLevel { get; set; }
        public double BeamOffset { get; set; } // feet
        private List<Line> _beamLines = new List<Line>();
        private List<CadTextInfo> _beamTexts = new List<CadTextInfo>();
        public void Execute(UIApplication app)
        {
            using (Transaction t = new Transaction(Doc, "Tạo Dam"))
            {
                try
                {
                    t.Start();
                    TaoDam();
                    t.Commit();
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    // Sử dụng TaskDialog trong Revit API để hiển thị lỗi
                    TaskDialog.Show("Lỗi Transaction", $"Lỗi trong quá trình thực hiện tạo cột: {ex.Message}");
                }
            }
        }
        public void TaoDam()
        {
            List<TextNote> texts = GetBeamSizeTextNotes();
            List<clsTextDam> listTextDam = new List<clsTextDam>();

            foreach (TextNote tn in texts)
            {
                string text = tn.Text;
                XYZ point = tn.Coord;
                double b, h;
                if (TryParseSize(text, out b, out h))
                {
                    listTextDam.Add(new clsTextDam
                    {
                        B = b,
                        H = h,
                        ToaDo = point
                    });
                }
            }

            List<Line> listLineDam = GetLineBeamName();
            CreateBeam(listLineDam, listTextDam);
        }
        private bool TaoDam(Line line, FamilySymbol familySymbol)
        {
            if (BeamLevel == null)
                throw new InvalidOperationException("Chưa chọn Level cho dầm.");

            if (!familySymbol.IsActive)
            {
                familySymbol.Activate();
                Doc.Regenerate();
            }

            FamilyInstance beamInstance = Doc.Create.NewFamilyInstance(
                line,
                familySymbol,
                BeamLevel,
                StructuralType.Beam);

            if (beamInstance == null)
                return false;

            Parameter offsetParameter =
                beamInstance.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE);

            if (offsetParameter == null || offsetParameter.IsReadOnly)
            {
                throw new InvalidOperationException(
                    "Không thể đặt Z Offset cho Family dầm này.");
            }

            offsetParameter.Set(BeamOffset);
            return true;
        }
        private FamilySymbol GetOrCreatebeamType(
            FamilySymbol baseType,
            double b,
            double h)
        {
            double b_mm = b / 304.8;

            double h_mm = h / 304.8;

            string typeName = $"{baseType.Name} - {b}x{h}";

            FamilySymbol existed = new FilteredElementCollector(Doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs =>
                    fs.Family.Id == baseType.Family.Id &&
                    fs.Name == typeName);

            if (existed != null)
                return existed;

            FamilySymbol newType = baseType.Duplicate(typeName) as FamilySymbol;
            if (newType == null) return null;

            Parameter pb = newType.LookupParameter("b");
            Parameter ph = newType.LookupParameter("h");

            if (pb == null || ph == null)
            {
                TaskDialog.Show("Lỗi",
                    $"Family {baseType.Family.Name} không có parameter b / h");
                return baseType;
            }

            pb.Set(b_mm);
            ph.Set(h_mm);

            return newType;
        }

        private void CreateBeam(List<Line> listLineDam, List<clsTextDam> clsTextDams)
        {
            Dictionary<string, FamilySymbol> symbolCache = new Dictionary<string, FamilySymbol>();
            HashSet<Line> usedLines = new HashSet<Line>();

            bool beamCreated = false;

            foreach (clsTextDam td in clsTextDams)
            {
                Line nearestLine = FindNearestUnusedLine(td.ToaDo, listLineDam, usedLines);

                if (nearestLine == null)
                    continue;

                string key = $"{td.B}x{td.H}";

                if (!symbolCache.TryGetValue(key, out FamilySymbol fs))
                {
                    fs = GetOrCreatebeamType(BeamFamilyType, td.B, td.H);
                    symbolCache[key] = fs;
                }

                if (fs != null && TaoDam(nearestLine, fs))
                {
                    usedLines.Add(nearestLine);
                    beamCreated = true;
                }
            }

            if (!beamCreated)
            {
                TaskDialog.Show("Warning", "Không tạo được dầm nào.");
            }
            else
            {
                TaskDialog.Show("Warning", "Tạo dầm thành công");
            }
        }

        private Line FindNearestUnusedLine(
        XYZ textPoint,
        List<Line> lines,
        HashSet<Line> usedLines)
        {
            Line nearest = null;
            double minDist = double.MaxValue;

            foreach (Line ln in lines)
            {
                if (usedLines.Contains(ln))
                    continue;

                XYZ mid = (ln.GetEndPoint(0) + ln.GetEndPoint(1)) / 2.0;
                double dist = textPoint.DistanceTo(mid);

                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = ln;
                }
            }

            return nearest;
        }
        private ImportInstance FindCadImport()
        {
            return new FilteredElementCollector(Doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .FirstOrDefault(i =>
                {
                    Element type = Doc.GetElement(i.GetTypeId());
                    return type != null && type.Name == CadFileName;
                });
        }
        public List<Line> GetLineBeamName()
        {
            ImportInstance import = FindCadImport();
            if (import == null)
                throw new Exception("Không tìm thấy file CAD");

            List<Line> lines = new List<Line>();

            Options opt = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = true
            };

            GeometryElement geoEle = import.get_Geometry(opt);


            foreach (GeometryObject geometryObject in geoEle)
            {
                GeometryInstance geometryInstance = geometryObject as GeometryInstance;
                if (geometryInstance == null) continue;

                GeometryElement geometryElement = geometryInstance.GetInstanceGeometry();
                if (geometryElement == null) continue;

                foreach (GeometryObject obj in geometryElement)
                {
                    GraphicsStyle gStyle = Doc.GetElement(obj.GraphicsStyleId) as GraphicsStyle;
                    if (gStyle == null) continue;

                    string layer = gStyle.GraphicsStyleCategory.Name;
                    if (layer != BeamLineLayer) continue;

                    // =========================
                    // CASE 1: LINE
                    // =========================
                    if (obj is Line line)
                    {
                        lines.Add(line);
                    }
                    // =========================
                    // CASE 2: POLYLINE
                    // =========================
                    else if (obj is PolyLine poly)
                    {
                        IList<XYZ> pts = poly.GetCoordinates();

                        for (int i = 0; i < pts.Count - 1; i++)
                        {
                            Line seg = Line.CreateBound(pts[i], pts[i + 1]);
                            lines.Add(seg);
                        }
                    }
                }
            }
            return lines;
        }
        public List<TextNote> GetBeamSizeTextNotes()
        {
            Regex regex = new Regex(@"\d+\s*[xX]\s*\d+");

            return new FilteredElementCollector(Doc)
                .OfClass(typeof(TextNote))
                .Cast<TextNote>()
                .Where(t => regex.IsMatch(t.Text))
                .ToList();
        }
        public bool TryParseSize(string text, out double b, out double h)
        {
            b = h = 0;

            Match m = Regex.Match(text, @"(\d+)\s*[xX]\s*(\d+)");
            if (!m.Success) return false;

            b = double.Parse(m.Groups[1].Value);
            h = double.Parse(m.Groups[2].Value);
            return true;
        }
        public string GetName()
        {
            return "Create Column From CAD";
        }
    }
    public class clsTextDam
    {
        public double B { get; set; }
        public double H { get; set; }
        public XYZ ToaDo { get; set; }
    }
    public class CadTextInfo
    {
        public string Value;
        public XYZ Position;
    }
}
