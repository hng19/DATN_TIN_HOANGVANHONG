using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DATN_TIN_HOANGVANHONG
{
    public class clsCot : IExternalEventHandler
    {
        public Document Doc { get; set; }
        public Level BaseLevel { get; set; }// Level cơ sở để đặt cột
        public Level TopLevel { get; set; }// Level đỉnh của cột (hoặc chiều cao)
        public double BaseOffset { get; set; } = 0;
        public double TopOffset { get; set; } = 0;
        public string CadFileName { get; set; }         // Tên file CAD (dùng để lọc ImportInstance)
        public string CadColumnLayerName { get; set; }  // Tên Layer chứa biên dạng cột
        public FamilySymbol ColumnFamilyType { get; set; } // Loại Family Cột cần tạo
        public void Execute(UIApplication app)
        {
            using (Transaction t = new Transaction(Doc, "Tạo Cột"))
            {
                try
                {
                    t.Start();
                    TaoCot();
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
        public void TaoCot()
        {

            if (ColumnFamilyType == null || BaseLevel == null)
                return;

            if (!ColumnFamilyType.IsActive)
            {
                ColumnFamilyType.Activate();
                Doc.Regenerate();
            }
            ImportInstance cad = new FilteredElementCollector(Doc)
                .OfClass(typeof(ImportInstance))
                .WhereElementIsNotElementType()
                .Cast<ImportInstance>()
                .FirstOrDefault(inst =>
                {
                    Element type = Doc.GetElement(inst.GetTypeId());
                    return type != null && type.Name == CadFileName;
                });
            if (cad == null)
            {
                TaskDialog.Show("Lỗi", "Không tìm thấy LinkCAD");
                return;
            }
            Options opt = new Options
            {
                IncludeNonVisibleObjects = true,
                ComputeReferences = true
            };
            GeometryElement geo = cad.get_Geometry(opt);
            Transform tf = cad.GetTransform();

            int count = 0;

            foreach (GeometryObject obj in geo)
                ProcessGeometry(obj, tf, ref count);
            // thông báo kết quả
            MessageBox.Show($"Đã tạo thành công {count} cột.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);

        }
        private void ProcessGeometry(
            GeometryObject obj,
            Transform tf,
            ref int count)
        {
            if (obj is GeometryInstance gi)
            {
                Transform newTf = tf.Multiply(gi.Transform);
                foreach (GeometryObject g in gi.GetInstanceGeometry())
                    ProcessGeometry(g, newTf, ref count);
                return;
            }

            if (!(obj is PolyLine poly)) return;
            GraphicsStyle gs = Doc.GetElement(poly.GraphicsStyleId) as GraphicsStyle;
            if (gs == null || !gs.GraphicsStyleCategory.Name.Contains(CadColumnLayerName)) return;
            IList<XYZ> pts = poly.GetCoordinates();
            if (pts.Count < 4) return;
            if (!pts.First().IsAlmostEqualTo(pts.Last())) return;

            CreateColumnFromPolyline(pts, tf);
            count++;
        }
        private void CreateColumnFromPolyline(IList<XYZ> pts, Transform tf)
        {
            // ---- 1. xác định trục chính từ cạnh dài nhất
            XYZ mainDir = XYZ.BasisX;
            double maxLen = 0;

            for (int i = 0; i < pts.Count - 1; i++)
            {
                XYZ v = pts[i + 1] - pts[i];
                double len = v.GetLength();
                if (len > maxLen)
                {
                    maxLen = len;
                    mainDir = v.Normalize();
                }
            }

            XYZ xAxis = mainDir;
            XYZ yAxis = new XYZ(-xAxis.Y, xAxis.X, 0);

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            foreach (XYZ p0 in pts)
            {
                XYZ p = tf.OfPoint(p0);
                double x = p.DotProduct(xAxis);
                double y = p.DotProduct(yAxis);

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }

            double b = maxX - minX;
            double h = maxY - minY;
            if (b < 0.01 || h < 0.01) return;
            // ---- 2. tâm
            double cx = (minX + maxX) / 2;
            double cy = (minY + maxY) / 2;
            XYZ center = xAxis.Multiply(cx)
                       + yAxis.Multiply(cy);
            center = tf.OfPoint(center);
            XYZ loc = new XYZ(center.X, center.Y, BaseLevel.Elevation);
            // ---- 3. DUPLICATE FAMILY TYPE
            FamilySymbol finalType = GetOrCreateColumnType(ColumnFamilyType, b, h);
            if (finalType == null) return;
            // ---- 4. tạo cột
            FamilyInstance col = Doc.Create.NewFamilyInstance(
                loc,
                finalType,
                BaseLevel,
                Autodesk.Revit.DB.Structure.StructuralType.Column);
            if (TopLevel != null)
                col.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.Set(TopLevel.Id);
            col.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)?.Set(BaseOffset);
            col.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)?.Set(TopOffset);
            // ---- 5. xoay theo polyline
            double angle = Math.Atan2(xAxis.Y, xAxis.X);
            Line axis = Line.CreateBound(loc, loc + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(Doc, col.Id, axis, angle);
        }
        private FamilySymbol GetOrCreateColumnType(
            FamilySymbol baseType,
            double b,
            double h)
        {
            int b_mm = (int)Math.Round(
                UnitUtils.ConvertFromInternalUnits(b, UnitTypeId.Millimeters) / 10) * 10;
            int h_mm = (int)Math.Round(
                UnitUtils.ConvertFromInternalUnits(h, UnitTypeId.Millimeters) / 10) * 10;
            string typeName = $"{baseType.Name} - {b_mm}x{h_mm}";
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
            pb.Set(b);
            ph.Set(h);
            return newType;
        }
        public string GetName()
        {
            return "Create Column From CAD";
        }
    }
}
