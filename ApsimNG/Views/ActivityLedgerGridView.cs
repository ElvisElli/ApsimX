﻿// -----------------------------------------------------------------------
// <copyright file="DataStoreView.cs" company="APSIM Initiative">
//     Copyright (c) APSIM Initiative
// </copyright>
// -----------------------------------------------------------------------
namespace UserInterface.Views
{
    using Interfaces;
    using Gtk;
    using System.Data;
    using System;
    using Models.Core;
    using System.Drawing;
    using System.IO;
    using System.Drawing.Imaging;
    using System.Collections.Generic;

    public interface IActivityLedgerGridView
    {
        /// <summary>Provides the name of the report for data collection.</summary>
        string ModelName { get; set; }

        /// <summary>Grid for holding data.</summary>
        System.Data.DataTable DataSource { get; set; }

        void LockLeftMostColumns(int number);

        /// <summary>
        /// Gets or sets a value indicating whether the grid is read only
        /// </summary>
        bool ReadOnly { get; set; }
    }

    /// <summary>
    /// An activity ledger disply grid view
    /// </summary>
    public class ActivityLedgerGridView : ViewBase, IActivityLedgerGridView
    {
        /// <summary>
        /// The data table that is being shown on the grid.
        /// </summary>
        private DataTable table;

        /// <summary>
        /// The default numeric format
        /// </summary>
        private string defaultNumericFormat = "F2";

        /// <summary>
        /// Flag to keep track of whether a cursor move was initiated internally
        /// </summary>
        private bool selfCursorMove = false;

        private ScrolledWindow scrolledwindow1 = null;
        public TreeView gridview = null;
        public TreeView fixedcolview = null;
        private HBox hbox1 = null;
        private Gtk.Image image1 = null;

        private Gdk.Pixbuf imagePixbuf;

        private ListStore gridmodel = new ListStore(typeof(string));
        private Dictionary<CellRenderer, int> colLookup = new Dictionary<CellRenderer, int>();

        /// <summary>
        /// Initializes a new instance of the <see cref="GridView" /> class.
        /// </summary>
        public ActivityLedgerGridView(ViewBase owner) : base(owner)
        {
            Builder builder = BuilderFromResource("ApsimNG.Resources.Glade.GridView.glade");
            hbox1 = (HBox)builder.GetObject("hbox1");
            scrolledwindow1 = (ScrolledWindow)builder.GetObject("scrolledwindow1");
            gridview = (TreeView)builder.GetObject("gridview");
            fixedcolview = (TreeView)builder.GetObject("fixedcolview");
            image1 = (Gtk.Image)builder.GetObject("image1");
            _mainWidget = hbox1;
            gridview.Model = gridmodel;
            gridview.Selection.Mode = SelectionMode.Multiple;
            fixedcolview.Model = gridmodel;
            fixedcolview.Selection.Mode = SelectionMode.Multiple;
            gridview.EnableSearch = false;
            fixedcolview.EnableSearch = false;
            image1.Pixbuf = null;
            image1.Visible = false;
            _mainWidget.Destroyed += _mainWidget_Destroyed;
        }

        /// <summary>
        /// Gets or sets the data to use to populate the grid.
        /// </summary>
        public System.Data.DataTable DataSource
        {
            get
            {
                return this.table;
            }

            set
            {
                this.table = value;
                LockLeftMostColumns(0);
                this.PopulateGrid();
            }
        }

        /// <summary>
        /// Does cleanup when the main widget is destroyed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void _mainWidget_Destroyed(object sender, EventArgs e)
        {
            if (numberLockedCols > 0)
            {
                gridview.Vadjustment.ValueChanged -= Gridview_Vadjustment_Changed;
                gridview.Selection.Changed -= Gridview_CursorChanged;
                fixedcolview.Vadjustment.ValueChanged -= Fixedcolview_Vadjustment_Changed1;
                fixedcolview.Selection.Changed -= Fixedcolview_CursorChanged;
            }
            // It's good practice to disconnect the event handlers, as it makes memory leaks
            // less likely. However, we may not "own" the event handlers, so how do we 
            // know what to disconnect?
            // We can do this via reflection. Here's how it currently can be done in Gtk#.
            // Windows.Forms would do it differently.
            // This may break if Gtk# changes the way they implement event handlers.
            ClearGridColumns();
            gridmodel.Dispose();
            if (imagePixbuf != null)
                imagePixbuf.Dispose();
            if (image1 != null)
                image1.Dispose();
            if (table != null)
                table.Dispose();
            _mainWidget.Destroyed -= _mainWidget_Destroyed;
            _owner = null;
        }

        /// <summary>
        /// Removes all grid columns, and cleans up any associated event handlers
        /// </summary>
        private void ClearGridColumns()
        {
            while (gridview.Columns.Length > 0)
            {
                TreeViewColumn col = gridview.GetColumn(0);
                foreach (CellRenderer render in col.CellRenderers)
                {
                    if (render is CellRendererText)
                    {
                        CellRendererText textRender = render as CellRendererText;
                        col.SetCellDataFunc(textRender, (CellLayoutDataFunc)null);
                    }
                    else if (render is CellRendererPixbuf)
                    {
                        CellRendererPixbuf pixRender = render as CellRendererPixbuf;
                        col.SetCellDataFunc(pixRender, (CellLayoutDataFunc)null);
                    }
                    render.Destroy();
                }
                gridview.RemoveColumn(gridview.GetColumn(0));
            }
            while (fixedcolview.Columns.Length > 0)
            {
                TreeViewColumn col = fixedcolview.GetColumn(0);
                foreach (CellRenderer render in col.CellRenderers)
                    if (render is CellRendererText)
                    {
                        CellRendererText textRender = render as CellRendererText;
                        col.SetCellDataFunc(textRender, (CellLayoutDataFunc)null);
                    }
                fixedcolview.RemoveColumn(fixedcolview.GetColumn(0));
            }
        }

        /// <summary>
        /// Repsonds to selection changes in the "fixed" columns area by
        /// selecting corresponding rows in the main grid
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Fixedcolview_CursorChanged(object sender, EventArgs e)
        {
            if (!selfCursorMove)
            {
                selfCursorMove = true;
                TreeSelection fixedSel = fixedcolview.Selection;
                TreePath[] selPaths = fixedSel.GetSelectedRows();

                TreeSelection gridSel = gridview.Selection;
                gridSel.UnselectAll();
                foreach (TreePath path in selPaths)
                    gridSel.SelectPath(path);
                selfCursorMove = false;
            }
        }

        /// <summary>
        /// Repsonds to selection changes in the main grid by
        /// selecting corresponding rows in the "fixed columns" grid
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Gridview_CursorChanged(object sender, EventArgs e)
        {
            if (fixedcolview.Visible && !selfCursorMove)
            {
                selfCursorMove = true;
                TreeSelection gridSel = gridview.Selection;
                TreePath[] selPaths = gridSel.GetSelectedRows();

                TreeSelection fixedSel = fixedcolview.Selection;
                fixedSel.UnselectAll();
                foreach (TreePath path in selPaths)
                    fixedSel.SelectPath(path);
                selfCursorMove = false;
            }
        }

        private int numberLockedCols = 0;

        /// <summary>
        /// Populate the grid from the DataSource.
        /// Note that we don't statically set the contents of the grid cells, but rather do this 
        /// dynamically in OnSetCellData. However, we do set up appropriate attributes for 
        /// cell columns, and a set of cell renderers.
        /// </summary>
        private void PopulateGrid()
        {
            // WaitCursor = true;
            // Set the cursor directly rather than via the WaitCursor property, as the property setter
            // runs a message loop. This is normally desirable, but in this case, we have lots
            // of events associated with the grid data, and it's best to let them be handled in the 
            // main message loop. 

            if (mainWindow != null)
                mainWindow.Cursor = new Gdk.Cursor(Gdk.CursorType.Watch);
            ClearGridColumns();
            fixedcolview.Visible = false;
            colLookup.Clear();
            // Begin by creating a new ListStore with the appropriate number of
            // columns. Use the string column type for everything.
            int nCols = DataSource != null ? this.DataSource.Columns.Count : 0;
            Type[] colTypes = new Type[nCols];
            for (int i = 0; i < nCols; i++)
                colTypes[i] = typeof(string);
            gridmodel = new ListStore(colTypes);
            gridview.ModifyBase(StateType.Active, fixedcolview.Style.Base(StateType.Selected));
            gridview.ModifyText(StateType.Active, fixedcolview.Style.Text(StateType.Selected));
            fixedcolview.ModifyBase(StateType.Active, gridview.Style.Base(StateType.Selected));
            fixedcolview.ModifyText(StateType.Active, gridview.Style.Text(StateType.Selected));

            image1.Visible = false;
            // Now set up the grid columns
            for (int i = 0; i < nCols; i++)
            {
                /// Design plan: include renderers for text, toggles and combos, but hide all but one of them
                CellRendererText textRender = new Gtk.CellRendererText();
                CellRendererPixbuf pixbufRender = new CellRendererPixbuf();
                pixbufRender.Pixbuf = new Gdk.Pixbuf(null, "ApsimNG.Resources.MenuImages.Save.png");
                pixbufRender.Xalign = 0.5f;

                if (i == 0)
                {
                    colLookup.Add(textRender, i);
                }
                else
                {
                    colLookup.Add(pixbufRender, i);
                }

                textRender.FixedHeightFromFont = 1; // 1 line high
                pixbufRender.Height = 23;
                textRender.Editable = !isReadOnly;
                textRender.Xalign = ((i == 0) || (i == 1) && isPropertyMode) ? 0.0f : 1.0f; // For right alignment of text cell contents; left align the first column

                TreeViewColumn column = new TreeViewColumn();
                column.Title = this.DataSource.Columns[i].Caption;

                if (i==0)
                {
                    column.PackStart(textRender, true);     // 0
                }
                else
                {
                    column.PackStart(pixbufRender, false);  // 3
                }

                column.Sizing = TreeViewColumnSizing.Autosize;
                //column.FixedWidth = 100;
                column.Resizable = true;

                if (i == 0)
                {
                    column.SetCellDataFunc(textRender, OnSetCellData);
                }
                else
                {
                    column.SetCellDataFunc(pixbufRender, RenderActivityStatus);
                }
                if (i == 1 && isPropertyMode)
                    column.Alignment = 0.0f;
                else
                    column.Alignment = 0.5f; // For centered alignment of the column header
                gridview.AppendColumn(column);

                // Gtk Treeview doesn't support "frozen" columns, so we fake it by creating a second, identical, TreeView to display
                // the columns we want frozen
                // For now, these frozen columns will be treated as read-only text
                TreeViewColumn fixedColumn = new TreeViewColumn(this.DataSource.Columns[i].ColumnName, textRender, "text", i);
                fixedColumn.Sizing = TreeViewColumnSizing.Autosize;
                fixedColumn.Resizable = true;
                fixedColumn.SetCellDataFunc(textRender, OnSetCellData);
                fixedColumn.Alignment = 0.5f; // For centered alignment of the column header
                fixedColumn.Visible = false;
                fixedcolview.AppendColumn(fixedColumn);
            }

            if (!isPropertyMode)
            {
                // Add an empty column at the end; auto-sizing will give this any "leftover" space
                TreeViewColumn fillColumn = new TreeViewColumn();
                gridview.AppendColumn(fillColumn);
                fillColumn.Sizing = TreeViewColumnSizing.Autosize;
            }

            int nRows = DataSource != null ? this.DataSource.Rows.Count : 0;

            gridview.Model = null;
            fixedcolview.Model = null;
            for (int row = 0; row < nRows; row++)
            {
                // We could store data into the grid model, but we don't.
                // Instead, we retrieve the data from our datastore when the OnSetCellData function is called
                gridmodel.Append();

                //DataRow dataRow = this.DataSource.Rows[row];
                //gridmodel.AppendValues(dataRow.ItemArray);

            }
            gridview.Model = gridmodel;

            SetColumnHeaders(gridview);
            SetColumnHeaders(fixedcolview);

            gridview.EnableSearch = false;
            //gridview.SearchColumn = 0;
            fixedcolview.EnableSearch = false;
            //fixedcolview.SearchColumn = 0;

            gridview.Show();

            if (mainWindow != null)
                mainWindow.Cursor = null;
        }

        /// <summary>
        /// Sets the contents of a cell being display on a grid
        /// </summary>
        /// <param name="col"></param>
        /// <param name="cell"></param>
        /// <param name="model"></param>
        /// <param name="iter"></param>
        public void RenderActivityStatus(TreeViewColumn col, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            TreePath path = model.GetPath(iter);
            int rowNo = path.Indices[0];
            int colNo;
            string text = String.Empty;
            if (colLookup.TryGetValue(cell, out colNo) && rowNo < this.DataSource.Rows.Count && colNo < this.DataSource.Columns.Count)
            {
                object dataVal = this.DataSource.Rows[rowNo][colNo];
                cell.Visible = true;
                switch (dataVal.ToString())
                {
                    case "Success":
                    case "Partial":
                    case "Ignore":
                    case "Critical":
                    case "Timer":
                        (cell as CellRendererPixbuf).Pixbuf = new Gdk.Pixbuf(null, "ApsimNG.Resources.MenuImages."+dataVal.ToString()+".png");
                        break;
                    default:
                        (cell as CellRendererPixbuf).Pixbuf = new Gdk.Pixbuf(null, "ApsimNG.Resources.MenuImages.blank.png");
//                        cell.Visible = false;
                        break;
                }
            }
        }

        /// <summary>
        /// Sets the contents of a cell being display on a grid
        /// </summary>
        /// <param name="col"></param>
        /// <param name="cell"></param>
        /// <param name="model"></param>
        /// <param name="iter"></param>
        public void OnSetCellData(TreeViewColumn col, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            TreePath path = model.GetPath(iter);
            TreeView view = col.TreeView as TreeView;
            int rowNo = path.Indices[0];
            int colNo;
            string text = String.Empty;
            if (colLookup.TryGetValue(cell, out colNo) && rowNo < this.DataSource.Rows.Count && colNo < this.DataSource.Columns.Count)
            {
                object dataVal = this.DataSource.Rows[rowNo][colNo];
                text = AsString(dataVal);
            }
            cell.Visible = true;
            (cell as CellRendererText).Text = text;
        }

        /// <summary>
        /// Modify the settings of all column headers
        /// We apply center-justification to all the column headers, just for the heck of it
        /// Note that "justification" here refers to justification of wrapped lines, not 
        /// justification of the header as a whole, which is handled with column.Alignment
        /// We create new Labels here, and use markup to make them bold, since other approaches 
        /// don't seem to work consistently
        /// </summary>
        /// <param name="view">The treeview for which headings are to be modified</param>
        private void SetColumnHeaders(TreeView view)
        {
            int nCols = DataSource != null ? this.DataSource.Columns.Count : 0;
            for (int i = 0; i < nCols; i++)
            {
                Label newLabel = new Label();
                view.Columns[i].Widget = newLabel;
                newLabel.Wrap = true;
                newLabel.Justify = Justification.Center;
                if (i == 1 && isPropertyMode)  // Add a tiny bit of extra space when left-aligned
                    (newLabel.Parent as Alignment).LeftPadding = 2;
                newLabel.UseMarkup = true;
                newLabel.Markup = "<b>" + System.Security.SecurityElement.Escape(gridview.Columns[i].Title) + "</b>";
                if (this.DataSource.Columns[i].Caption != this.DataSource.Columns[i].ColumnName)
                    newLabel.Parent.Parent.Parent.TooltipText = this.DataSource.Columns[i].ColumnName;
                newLabel.Show();
            }
        }


        /// <summary>
        /// Handle vertical scrolling changes to keep the gridview and fixedcolview at the same scrolled position
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Fixedcolview_Vadjustment_Changed1(object sender, EventArgs e)
        {
            gridview.Vadjustment.Value = fixedcolview.Vadjustment.Value;
        }

        /// <summary>
        /// Handle vertical scrolling changes to keep the gridview and fixedcolview at the same scrolled position
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Gridview_Vadjustment_Changed(object sender, EventArgs e)
        {
            fixedcolview.Vadjustment.Value = gridview.Vadjustment.Value;
        }

        /// <summary>
        /// The name of the associated model.
        /// </summary>
        public string ModelName
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the number of rows in grid.
        /// </summary>
        public int RowCount
        {
            get
            {
                return gridmodel.IterNChildren();
            }

            set
            {
                // The main use of this will be to allow "empty" rows at the bottom of the grid to allow for
                // additional data to be entered (primarily soil profile stuff). 
                if (value > RowCount) // Add new rows
                {
                    for (int i = RowCount; i < value; i++)
                        gridmodel.Append(); // Will this suffice?
                }
                else if (value < RowCount) // Remove existing rows. But let's check first to be sure they're empty
                {
                    /// TBI
                }
                /// TBI this.Grid.RowCount = value;
            }
        }

        /// <summary>
        /// Gets or sets the numeric grid format e.g. N3
        /// </summary>
        public string NumericFormat
        {
            get
            {
                return this.defaultNumericFormat;
            }

            set
            {
                this.defaultNumericFormat = value;
            }
        }

        private bool isPropertyMode = false;

        /// <summary>
        /// Gets or sets a value indicating whether "property" mode is enabled
        /// </summary>
        public bool PropertyMode
        {
            get
            {
                return isPropertyMode;
            }
            set
            {
                if (value != isPropertyMode)
                {
                    this.PopulateGrid();
                }
                isPropertyMode = value;
            }
        }

        /// <summary>
        /// Stores whether our grid is readonly. Internal value.
        /// </summary>
        private bool isReadOnly = false;

        /// <summary>
        /// Gets or sets a value indicating whether the grid is read only
        /// </summary>
        public bool ReadOnly
        {
            get
            {
                return isReadOnly;
            }

            set
            {
                if (value != isReadOnly)
                {
                    foreach (TreeViewColumn col in gridview.Columns)
                        foreach (CellRenderer render in col.CellRenderers)
                            if (render is CellRendererText)
                                (render as CellRendererText).Editable = !value;
                }
                isReadOnly = value;
            }
        }

        /// <summary>
        /// Returns the string representation of an object. For most objects,
        /// this will be the same as "ToString()", but for Crops, it will give
        /// the crop name
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        private string AsString(object obj)
        {
            string result;
            if (obj is ICrop)
                result = (obj as IModel).Name;
            else
                result = obj.ToString();
            return result;
        }

        /// <summary>Lock the left most number of columns.</summary>
        /// <param name="number"></param>
        public void LockLeftMostColumns(int number)
        {
            if (number == numberLockedCols || !gridview.IsMapped)
                return;
            for (int i = 0; i < gridmodel.NColumns; i++)
            {
                if (fixedcolview.Columns.Length > i)
                    fixedcolview.Columns[i].Visible = i < number;
                if (gridview.Columns.Length > i)
                    gridview.Columns[i].Visible = i >= number;
            }
            if (number > 0)
            {
                if (numberLockedCols == 0)
                {
                    gridview.Vadjustment.ValueChanged += Gridview_Vadjustment_Changed;
                    gridview.Selection.Changed += Gridview_CursorChanged;
                    fixedcolview.Vadjustment.ValueChanged += Fixedcolview_Vadjustment_Changed1;
                    fixedcolview.Selection.Changed += Fixedcolview_CursorChanged;
                    Gridview_CursorChanged(this, EventArgs.Empty);
                    Gridview_Vadjustment_Changed(this, EventArgs.Empty);
                }
                fixedcolview.Model = gridmodel;
                fixedcolview.Visible = true;
            }
            else
            {
                gridview.Vadjustment.ValueChanged -= Gridview_Vadjustment_Changed;
                gridview.Selection.Changed -= Gridview_CursorChanged;
                fixedcolview.Vadjustment.ValueChanged -= Fixedcolview_Vadjustment_Changed1;
                fixedcolview.Selection.Changed -= Fixedcolview_CursorChanged;
                fixedcolview.Visible = false;
            }
            numberLockedCols = number;
        }

        /// <summary>Get screenshot of grid.</summary>
        public System.Drawing.Image GetScreenshot()
        {
            // Create a Bitmap and draw the DataGridView on it.
            int width;
            int height;
            Gdk.Window gridWindow = hbox1.GdkWindow;  // Should we draw from hbox1 or from gridview?
            gridWindow.GetSize(out width, out height);
            Gdk.Pixbuf screenshot = Gdk.Pixbuf.FromDrawable(gridWindow, gridWindow.Colormap, 0, 0, 0, 0, width, height);
            byte[] buffer = screenshot.SaveToBuffer("png");
            MemoryStream stream = new MemoryStream(buffer);
            System.Drawing.Bitmap bitmap = new Bitmap(stream);
            return bitmap;
        }

        /// <summary>
        /// Called when the window is resized to resize all grid controls.
        /// </summary>
        public void ResizeControls()
        {
            if (gridmodel.NColumns == 0)
                return;

            if (gridmodel.IterNChildren() == 0)
            {
                gridview.Visible = false;
            }
            else
                gridview.Visible = true;
        }

        private void GridView_Resize(object sender, EventArgs e)
        {
            ResizeControls();
        }

    }
}
