using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.LinkLabel;
namespace DATN_TIN_HOANGVANHONG
{
    public partial class Form1 : System.Windows.Forms.Form
    {
        Document Doc;
        UIDocument UIDoc;
        private clsCot _clsCot;
        private ExternalEvent _suKienTaoCot;

        public bool dialogResult = true;
        private IList<Line> lines = new List<Line>();
        public List<Level> Levels = new List<Level>();
        private List<CADLinkType> _cadLinks = new List<CADLinkType>();
        public List<Family> ColumnFamilies { get; set; } = new List<Family>();
        public Form1(Document doc, UIDocument uIDocument)
        {
            InitializeComponent();
            Doc = doc;
            UIDoc = uIDocument;

            // Column
            LoadCadLinks();
            GetLineColumnName();
            GetAllColumnInstanceNames();
            GetLevelsColumn();
            _clsCot = new clsCot();
            _suKienTaoCot = ExternalEvent.Create(_clsCot);
        }
        #region lấy danh sách cad đã link
        private void LoadCadLinks()
        {
            _cadLinks = new FilteredElementCollector(Doc)
        .OfClass(typeof(CADLinkType))
        .Cast<CADLinkType>()
        .ToList();

            cbblinkcad.Items.Clear();

            foreach (var cad in _cadLinks)
            {
                if (!cad.Name.ToLower().Contains("text"))
                {
                    cbblinkcad.Items.Add(cad.Name);
                }
            }

            if (cbblinkcad.Items.Count > 0)
                cbblinkcad.SelectedIndex = 0;

        }
        private void cbblinkcad_SelectedValueChanged(object sender, EventArgs e)
        {
            if (cbblinkcad.SelectedItem != null)
            {
                GetLineColumnName();
            }
        }
        #endregion
        #region lấy layer từ file  cad
        public void GetLineColumnName()
        {
            lines.Clear();
            IList<string> layerNames = new List<string>();

            // 1. Tìm CAD Link Type tương ứng với lựa chọn hiện tại
            string selectedCadName = cbblinkcad.Text;
            CADLinkType currentType = _cadLinks.FirstOrDefault(cad => cad.Name == selectedCadName);

            if (currentType != null)
            {
                try
                {
                    // 2. Lấy đối tượng Category của CAD Link Type. 
                    // Category của CAD Link Type chính là Category cha chứa tất cả các Layer (SubCategories).
                    Category cadCategory = Doc.Settings.Categories.get_Item(currentType.Name);

                    if (cadCategory != null)
                    {
                        // 3. Duyệt qua TẤT CẢ SubCategories (chính là Layer CAD)
                        foreach (Category subCategory in cadCategory.SubCategories)
                        {
                            // Lấy tên của SubCategory, đây chính là tên Layer
                            string layerName = subCategory.Name;
                            layerNames.Add(layerName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi lấy Layer từ CAD: {ex.Message}");
                }
            }

            // 4. Gán kết quả vào ComboBox
            if (layerNames.Any())
            {
                // Loại bỏ trùng lặp (mặc dù SubCategories vốn đã không trùng) và sắp xếp
                cbbLineCadColumn.DataSource = layerNames.Distinct().OrderBy(name => name).ToList();
            }
            else
            {
                cbbLineCadColumn.DataSource = null;
            }
        }
        #endregion
        #region lấy family cột 
        public void GetAllColumnInstanceNames()
        {
            // 1. Lọc tất cả các Family Cột Kết cấu
            FilteredElementCollector familyCollector = new FilteredElementCollector(Doc);
            familyCollector.OfClass(typeof(Family)); // <-- Chỉ lọc Family

            // Filter by Category Structural Columns
            // Vì FilteredElementCollector.OfCategory không hoạt động trực tiếp trên Family
            // Ta lọc sau khi lấy
            ColumnFamilies = familyCollector
                .Cast<Family>()
                .Where(f => f.FamilyCategoryId.IntegerValue.Equals((int)BuiltInCategory.OST_StructuralColumns))
                .ToList();

            // 2. Lấy tên Family để hiển thị trên ComboBox
            List<string> familyNames = ColumnFamilies.Select(f => f.Name).ToList();

            // 3. Đưa tên lên ComboBox
            cbbFamilyColumn.Items.Clear();
            cbbFamilyColumn.DataSource = familyNames;
        }
        #endregion
        #region lấy level
        private void GetLevelsColumn()
        {
            // Lấy tất cả các phần tử level từ dự án
            FilteredElementCollector collector = new FilteredElementCollector(Doc);
            ICollection<Element> levelElements = collector.OfClass(typeof(Level)).ToElements();

            // Lặp qua từng phần tử và thêm chúng vào danh sách levels
            foreach (Element elem in levelElements)
            {
                if (elem is Level level)
                {
                    Levels.Add(level);
                }
            }

            // Xóa tất cả các mục có sẵn trong ComboBox trước khi thêm mới
            cbb_BaseLeverColumn.Items.Clear();
            cbb_TopLeverColumn.Items.Clear();

            // Thêm các level vào ComboBox
            foreach (Level level in Levels)
            {
                cbb_BaseLeverColumn.Items.Add(level.Name);
                cbb_TopLeverColumn.Items.Add(level.Name);
            }
        }
        #endregion

        private void cbbLineCadColumn_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void btn_RunColumn_Click(object sender, EventArgs e)
        {
            // 1.Kiểm tra và Thu thập dữ liệu
            string cadFileName = cbblinkcad.Text;
            string layerName = cbbLineCadColumn.Text;
            string familyName = cbbFamilyColumn.Text;
            string baseLevelName = cbb_BaseLeverColumn.Text;
            string topLevelName = cbb_TopLeverColumn.Text;

            // Đọc Offset (Chú ý: Revit API sử dụng đơn vị Feet)
            if (!double.TryParse(txt_BaseOffsetColumn.Text, out double baseOffset)) baseOffset = 0.0;
            if (!double.TryParse(txt_TopOffsetColumn.Text, out double topOffset)) topOffset = 0.0;

            // CHUYỂN ĐỔI ĐƠN VỊ (QUAN TRỌNG)

            double baseOffset_internal =
                UnitUtils.ConvertToInternalUnits(baseOffset, UnitTypeId.Millimeters);
            double topOffset_internal =
                UnitUtils.ConvertToInternalUnits(topOffset, UnitTypeId.Millimeters);

            // 2. Tìm Đối tượng Revit (Level, FamilySymbol)
            // Tìm Family đã chọn từ danh sách ColumnFamilies (được load ở GetAllColumnInstanceNames)
            Family selectedFamily = ColumnFamilies.FirstOrDefault(f => f.Name == familyName);
            FamilySymbol baseColumnType = null;
            if (selectedFamily != null)
            {
                // Lấy Family Symbol (Type) đầu tiên làm BASE TYPE để Duplicate sau này.
                FilteredElementCollector symbolCollector = new FilteredElementCollector(Doc);
                baseColumnType = symbolCollector
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs => fs.Family.Id == selectedFamily.Id); // Lọc theo ID Family
            }
            // b. Levels (Lấy đối tượng Level từ danh sách Levels đã load)
            Level baseLevel = Levels.FirstOrDefault(l => l.Name == baseLevelName);
            Level topLevel = Levels.FirstOrDefault(l => l.Name == topLevelName);

            // 3. Kiểm tra tính hợp lệ
            if (baseColumnType == null || baseLevel == null || string.IsNullOrEmpty(layerName) || string.IsNullOrEmpty(cadFileName))
            {
                MessageBox.Show("Vui lòng chọn đầy đủ CAD File, Layer, Family Type, và Base Level.", "Lỗi Thiếu Thông Tin");
                return;
            }
            // 4. Truyền dữ liệu vào Handler

            _clsCot.Doc = Doc;
            _clsCot.CadFileName = cadFileName;
            _clsCot.CadColumnLayerName = layerName;
            _clsCot.ColumnFamilyType = baseColumnType;
            _clsCot.BaseLevel = baseLevel;
            _clsCot.TopLevel = topLevel;
            _clsCot.BaseOffset = baseOffset_internal;
            _clsCot.TopOffset = topOffset_internal;

            _suKienTaoCot.Raise();
        }
    }
}
