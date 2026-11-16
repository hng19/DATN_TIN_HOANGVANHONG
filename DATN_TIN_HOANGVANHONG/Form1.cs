using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace DATN_TIN_HOANGVANHONG
{
    public partial class Form1 : System.Windows.Forms.Form
    {
        Document Doc;
        UIDocument UIDoc;

        private clsCot _clsCot;
        private ExternalEvent _suKienTaoCot;

        private clsDam _clsDam;
        private ExternalEvent _suKienTaoDam;

        private RefreshDataHandler _refreshDataHandler;
        private ExternalEvent _refreshDataEvent;
        private RefreshTarget _refreshTarget;

        public List<Family> BeamFamilies { get; set; } = new List<Family>();
        public List<Family> ColumnFamilies { get; set; } = new List<Family>();
        public List<Level> Levels { get; set; } = new List<Level>();

        public bool dialogResult = true;

        private IList<Line> lines = new List<Line>();
        private List<CADLinkType> _cadLinks = new List<CADLinkType>();

        #region Refresh Link

        private enum RefreshTarget
        {
            ColumnCad,
            BeamCad,
            ColumnFamily,
            BeamFamily,
            ColumnCadLayers,
            BeamCadLayers
        }

        private void RequestRefresh(RefreshTarget target)
        {
            _refreshTarget = target;

            ExternalEventRequest result = _refreshDataEvent.Raise();

            if (result == ExternalEventRequest.Denied)
            {
                MessageBox.Show(
                    "Revit không nhận được yêu cầu cập nhật danh sách.");
            }
        }

        private sealed class RefreshDataHandler : IExternalEventHandler
        {
            private readonly Form1 _form;

            public RefreshDataHandler(Form1 form)
            {
                _form = form;
            }

            public void Execute(UIApplication app)
            {
                UIDocument uidoc = app.ActiveUIDocument;

                if (uidoc == null || _form.IsDisposed)
                    return;

                try
                {
                    _form.RefreshData(uidoc, _form._refreshTarget);
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Lỗi cập nhật danh sách", ex.Message);
                }
            }

            public string GetName()
            {
                return "Refresh CAD and Family lists";
            }
        }

        private void RefreshData(UIDocument uidoc, RefreshTarget target)
        {
            UIDoc = uidoc;
            Doc = uidoc.Document;

            switch (target)
            {
                case RefreshTarget.ColumnCad:
                case RefreshTarget.BeamCad:
                    {
                        string oldColumnCad = cbblinkcad.Text;
                        string oldBeamCad = cbblinkCadBeam.Text;
                        string oldColumnLayer = cbbLineCadColumn.Text;
                        string oldBeamLayer = cbbLineBeam.Text;

                        LoadCadLinks();

                        RestoreSelection(cbblinkcad, oldColumnCad);
                        RestoreSelection(cbblinkCadBeam, oldBeamCad);

                        // Nạp lại layer cho cả hai tab theo CAD đang chọn ở từng tab.
                        GetLineColumnName();
                        RestoreSelection(cbbLineCadColumn, oldColumnLayer);

                        GetLineBeamName();
                        RestoreSelection(cbbLineBeam, oldBeamLayer);

                        break;
                    }

                case RefreshTarget.ColumnCadLayers:
                    GetLineColumnName();
                    break;

                case RefreshTarget.BeamCadLayers:
                    GetLineBeamName();
                    break;
            }
        }

        private static void RestoreSelection(
            System.Windows.Forms.ComboBox combo,
            string previousValue)
        {
            if (string.IsNullOrEmpty(previousValue))
                return;

            foreach (object item in combo.Items)
            {
                if (string.Equals(
                    item.ToString(),
                    previousValue,
                    StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
        }

        #endregion

        public Form1(Document doc, UIDocument uIDocument)
        {
            InitializeComponent();

            Doc = doc;
            UIDoc = uIDocument;

            _refreshDataHandler = new RefreshDataHandler(this);
            _refreshDataEvent = ExternalEvent.Create(_refreshDataHandler);

            // Mỗi lần mở danh sách, yêu cầu Revit nạp lại dữ liệu tương ứng.
            cbblinkcad.DropDown += (s, e) =>
                RequestRefresh(RefreshTarget.ColumnCad);

            cbblinkCadBeam.DropDown += (s, e) =>
                RequestRefresh(RefreshTarget.BeamCad);

            cbbFamilyColumn.DropDown += (s, e) =>
                RequestRefresh(RefreshTarget.ColumnFamily);

            cbbFamilyBeam.DropDown += (s, e) =>
                RequestRefresh(RefreshTarget.BeamFamily);

            cbblinkcad.SelectionChangeCommitted += (s, e) =>
                RequestRefresh(RefreshTarget.ColumnCadLayers);

            cbblinkCadBeam.SelectionChangeCommitted += (s, e) =>
                RequestRefresh(RefreshTarget.BeamCadLayers);

            // Column
            LoadCadLinks();
            GetLineColumnName();
            GetAllColumnInstanceNames();
            GetLevelsColumn();

            _clsCot = new clsCot();
            _suKienTaoCot = ExternalEvent.Create(_clsCot);

            // Beam
            GetLineBeamName();
            GetAllBeamInstanceNames();
            GetLevelsBeam();

            _clsDam = new clsDam();
            _suKienTaoDam = ExternalEvent.Create(_clsDam);
        }

        #region lấy danh sách cad đã link

        private void LoadCadLinks()
        {
            _cadLinks = new FilteredElementCollector(Doc)
                .OfClass(typeof(CADLinkType))
                .Cast<CADLinkType>()
                .ToList();

            cbblinkcad.Items.Clear();
            cbblinkCadBeam.Items.Clear();

            foreach (CADLinkType cad in _cadLinks)
            {
                if (cad.Name.IndexOf(
                    "text",
                    StringComparison.OrdinalIgnoreCase) < 0)
                {
                    cbblinkcad.Items.Add(cad.Name);
                    cbblinkCadBeam.Items.Add(cad.Name);
                }
            }

            if (cbblinkcad.Items.Count > 0)
                cbblinkcad.SelectedIndex = 0;

            if (cbblinkCadBeam.Items.Count > 0)
                cbblinkCadBeam.SelectedIndex = 0;
        }

        private void cbblinkcad_1_SelectedValueChanged(
            object sender,
            EventArgs e)
        {
            if (cbblinkcad.SelectedItem != null)
                GetLineColumnName();
        }

        #endregion

        #region lấy layer từ file cad

        public void GetLineColumnName()
        {
            lines.Clear();
            IList<string> layerNames = new List<string>();

            // Tìm CAD Link Type tương ứng với CAD đang chọn.
            string selectedCadName = cbblinkcad.Text;

            CADLinkType currentType =
                _cadLinks.FirstOrDefault(
                    cad => cad.Name == selectedCadName);

            if (currentType != null)
            {
                try
                {
                    // Category của CAD Link Type chứa các layer dưới dạng SubCategories.
                    Category cadCategory =
                        Doc.Settings.Categories.get_Item(currentType.Name);

                    if (cadCategory != null)
                    {
                        // Lấy tên các layer CAD.
                        foreach (Category subCategory in cadCategory.SubCategories)
                        {
                            layerNames.Add(subCategory.Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Lỗi khi lấy Layer từ CAD: {ex.Message}");
                }
            }

            // Đưa tên layer vào ComboBox.
            if (layerNames.Any())
            {
                cbbLineCadColumn.DataSource = layerNames
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList();
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
            // Lọc các Family thuộc Structural Columns.
            FilteredElementCollector familyCollector =
                new FilteredElementCollector(Doc);

            ColumnFamilies = familyCollector
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f =>
                    f.FamilyCategoryId.IntegerValue ==
                    (int)BuiltInCategory.OST_StructuralColumns)
                .ToList();

            // Đưa tên Family lên ComboBox.
            List<string> familyNames =
                ColumnFamilies.Select(f => f.Name).ToList();

            cbbFamilyColumn.DataSource = null;
            cbbFamilyColumn.Items.Clear();
            cbbFamilyColumn.DataSource = familyNames;
        }

        #endregion

        #region lấy level cột

        private void GetLevelsColumn()
        {
            FilteredElementCollector collector =
                new FilteredElementCollector(Doc);

            ICollection<Element> levelElements =
                collector.OfClass(typeof(Level)).ToElements();

            foreach (Element elem in levelElements)
            {
                if (elem is Level level)
                    Levels.Add(level);
            }

            cbb_BaseLeverColumn.Items.Clear();
            cbb_TopLeverColumn.Items.Clear();

            foreach (Level level in Levels)
            {
                cbb_BaseLeverColumn.Items.Add(level.Name);
                cbb_TopLeverColumn.Items.Add(level.Name);
            }
        }

        #endregion

        #region lấy layer dầm từ file cad

        public void GetLineBeamName()
        {
            lines.Clear();
            IList<string> layerNames = new List<string>();

            // Tìm CAD Link Type tương ứng với CAD đang chọn.
            string selectedCadName = cbblinkCadBeam.Text;

            CADLinkType currentType =
                _cadLinks.FirstOrDefault(
                    cad => cad.Name == selectedCadName);

            if (currentType != null)
            {
                try
                {
                    // Category của CAD Link Type chứa các layer dưới dạng SubCategories.
                    Category cadCategory =
                        Doc.Settings.Categories.get_Item(currentType.Name);

                    if (cadCategory != null)
                    {
                        // Lấy tên các layer CAD.
                        foreach (Category subCategory in cadCategory.SubCategories)
                        {
                            layerNames.Add(subCategory.Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Lỗi khi lấy Layer từ CAD: {ex.Message}");
                }
            }

            if (layerNames.Any())
            {
                cbbLineBeam.DataSource = layerNames
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList();
            }
            else
            {
                cbbLineBeam.DataSource = null;
            }

            // Tìm các TextNote có nội dung kích thước tiết diện.
            IEnumerable<TextNote> texts =
                new FilteredElementCollector(Doc)
                    .OfClass(typeof(TextNote))
                    .Cast<TextNote>();

            foreach (TextNote textNote in texts)
            {
                if (TryParseSection(
                    textNote.Text,
                    out double b,
                    out double h))
                {
                    // Có thể sử dụng b và h để xử lý kích thước dầm.
                }
            }
        }

        #endregion

        #region lấy level dầm

        private void GetLevelsBeam()
        {
            FilteredElementCollector collector =
                new FilteredElementCollector(Doc);

            ICollection<Element> levelElements =
                collector.OfClass(typeof(Level)).ToElements();

            Levels.Clear();
            cbb_SelectLevelBeam.Items.Clear();

            foreach (Element elem in levelElements)
            {
                if (elem is Level level)
                {
                    Levels.Add(level);
                    cbb_SelectLevelBeam.Items.Add(level.Name);
                }
            }
        }

        #endregion

        #region đọc kích thước tiết diện

        private static bool TryParseSection(
            string text,
            out double b,
            out double h)
        {
            b = 0;
            h = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            string cleaned = text
                .ToLowerInvariant()
                .Replace("×", "x")
                .Replace(" ", "");

            string[] parts = cleaned.Split('x');

            return parts.Length == 2
                && double.TryParse(parts[0], out b)
                && double.TryParse(parts[1], out h);
        }

        #endregion

        #region lấy family dầm

        public void GetAllBeamInstanceNames()
        {
            // Lấy các Family thuộc Structural Framing.
            FilteredElementCollector familyCollector =
                new FilteredElementCollector(Doc)
                    .OfClass(typeof(Family));

            BeamFamilies = familyCollector
                .Cast<Family>()
                .Where(f =>
                    f.FamilyCategoryId != null &&
                    f.FamilyCategoryId.IntegerValue ==
                    (int)BuiltInCategory.OST_StructuralFraming)
                .ToList();

            // Đưa tên Family lên ComboBox.
            List<string> familyNames =
                BeamFamilies.Select(f => f.Name).ToList();

            cbbFamilyBeam.DataSource = null;
            cbbFamilyBeam.Items.Clear();
            cbbFamilyBeam.DataSource = familyNames;
        }

        #endregion

        #region dựng cột

        private void btn_RunColumn_Click(object sender, EventArgs e)
        {
            string cadFileName = cbblinkcad.Text;
            string layerName = cbbLineCadColumn.Text;
            string familyName = cbbFamilyColumn.Text;
            string baseLevelName = cbb_BaseLeverColumn.Text;
            string topLevelName = cbb_TopLeverColumn.Text;

            // Revit API dùng đơn vị nội bộ; giá trị nhập trên form là milimét.
            if (!double.TryParse(
                txt_BaseOffsetColumn.Text,
                out double baseOffset))
            {
                baseOffset = 0.0;
            }

            if (!double.TryParse(
                txt_TopOffsetColumn.Text,
                out double topOffset))
            {
                topOffset = 0.0;
            }

            double baseOffsetInternal =
                UnitUtils.ConvertToInternalUnits(
                    baseOffset,
                    UnitTypeId.Millimeters);

            double topOffsetInternal =
                UnitUtils.ConvertToInternalUnits(
                    topOffset,
                    UnitTypeId.Millimeters);

            // Tìm Family đã chọn.
            Family selectedFamily =
                ColumnFamilies.FirstOrDefault(
                    f => f.Name == familyName);

            FamilySymbol baseColumnType = null;

            if (selectedFamily != null)
            {
                // Lấy FamilySymbol đầu tiên làm Type gốc.
                baseColumnType =
                    new FilteredElementCollector(Doc)
                        .OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>()
                        .FirstOrDefault(
                            fs => fs.Family.Id == selectedFamily.Id);
            }

            // Tìm Level đã chọn.
            Level baseLevel =
                Levels.FirstOrDefault(
                    level => level.Name == baseLevelName);

            Level topLevel =
                Levels.FirstOrDefault(
                    level => level.Name == topLevelName);

            if (baseColumnType == null ||
                baseLevel == null ||
                string.IsNullOrEmpty(layerName) ||
                string.IsNullOrEmpty(cadFileName))
            {
                MessageBox.Show(
                    "Vui lòng chọn đầy đủ CAD File, Layer, Family và Base Level.",
                    "Thiếu thông tin");
                return;
            }

            // Truyền lựa chọn từ form cho handler dựng cột.
            _clsCot.Doc = Doc;
            _clsCot.CadFileName = cadFileName;
            _clsCot.CadColumnLayerName = layerName;
            _clsCot.ColumnFamilyType = baseColumnType;
            _clsCot.BaseLevel = baseLevel;
            _clsCot.TopLevel = topLevel;
            _clsCot.BaseOffset = baseOffsetInternal;
            _clsCot.TopOffset = topOffsetInternal;

            _suKienTaoCot.Raise();
        }

        #endregion

        #region dựng dầm

        private void btn_RunBeam_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(cbbFamilyBeam.Text) ||
                string.IsNullOrEmpty(cbbLineBeam.Text) ||
                string.IsNullOrEmpty(cbb_SelectLevelBeam.Text))
            {
                MessageBox.Show("Vui lòng chọn đầy đủ thông tin!");
                return;
            }

            // Lấy Family dầm đã chọn.
            Family selectedFamily =
                BeamFamilies.FirstOrDefault(
                    family => family.Name == cbbFamilyBeam.Text);

            if (selectedFamily == null)
            {
                MessageBox.Show("Không tìm thấy Family dầm.");
                return;
            }

            // Lấy FamilySymbol đầu tiên làm Type gốc.
            FamilySymbol baseBeamType =
                new FilteredElementCollector(Doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(
                        symbol => symbol.Family.Id == selectedFamily.Id);

            if (baseBeamType == null)
            {
                MessageBox.Show("Family dầm không có Type.");
                return;
            }

            // Tìm Level đã chọn.
            Level level =
                Levels.FirstOrDefault(
                    item => item.Name == cbb_SelectLevelBeam.Text);

            if (level == null)
            {
                MessageBox.Show("Không tìm thấy Level đã chọn.");
                return;
            }

            // Offset nhập bằng milimét, sau đó đổi sang đơn vị nội bộ Revit.
            if (!double.TryParse(
                txt_HeightOffsetBeam.Text,
                out double offsetMm))
            {
                offsetMm = 0;
            }

            double offsetInternal =
                UnitUtils.ConvertToInternalUnits(
                    offsetMm,
                    UnitTypeId.Millimeters);

            // Truyền lựa chọn từ form cho handler dựng dầm.
            _clsDam.Doc = Doc;
            _clsDam.BeamLineLayer = cbbLineBeam.Text;
            _clsDam.BeamFamilyType = baseBeamType;
            _clsDam.BeamLevel = level;
            _clsDam.BeamOffset = offsetInternal;
            _clsDam.CadFileName = cbblinkCadBeam.Text;

            _suKienTaoDam.Raise();
        }

        #endregion
    }
}