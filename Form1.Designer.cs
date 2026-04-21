namespace ChamDiemGrader;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        lblCriteria = new Label();
        txtCriteriaPath = new TextBox();
        btnBrowseCriteria = new Button();
        lblFolder = new Label();
        txtFolderPath = new TextBox();
        btnBrowseFolder = new Button();
        lblApiKey = new Label();
        txtApiKey = new TextBox();
        lblModel = new Label();
        txtModel = new TextBox();
        btnCham = new Button();
        txtLog = new TextBox();
        progressBar = new ProgressBar();
        lblHint = new Label();
        lblMaxFiles = new Label();
        numMaxFiles = new NumericUpDown();
        ((System.ComponentModel.ISupportInitialize)numMaxFiles).BeginInit();
        SuspendLayout();
        //
        // lblCriteria
        //
        lblCriteria.AutoSize = true;
        lblCriteria.Location = new Point(12, 15);
        lblCriteria.Name = "lblCriteria";
        lblCriteria.Size = new Size(130, 15);
        lblCriteria.Text = "File tiêu chí (docx):";
        //
        // txtCriteriaPath
        //
        txtCriteriaPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtCriteriaPath.Location = new Point(150, 12);
        txtCriteriaPath.Name = "txtCriteriaPath";
        txtCriteriaPath.Size = new Size(568, 23);
        //
        // btnBrowseCriteria
        //
        btnBrowseCriteria.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnBrowseCriteria.Location = new Point(724, 11);
        btnBrowseCriteria.Name = "btnBrowseCriteria";
        btnBrowseCriteria.Size = new Size(75, 25);
        btnBrowseCriteria.Text = "Chọn...";
        btnBrowseCriteria.UseVisualStyleBackColor = true;
        btnBrowseCriteria.Click += BtnBrowseCriteria_Click;
        //
        // lblFolder
        //
        lblFolder.AutoSize = true;
        lblFolder.Location = new Point(12, 48);
        lblFolder.Name = "lblFolder";
        lblFolder.Size = new Size(127, 15);
        lblFolder.Text = "Thư mục bài thi:";
        //
        // txtFolderPath
        //
        txtFolderPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtFolderPath.Location = new Point(150, 45);
        txtFolderPath.Name = "txtFolderPath";
        txtFolderPath.Size = new Size(568, 23);
        //
        // btnBrowseFolder
        //
        btnBrowseFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnBrowseFolder.Location = new Point(724, 44);
        btnBrowseFolder.Name = "btnBrowseFolder";
        btnBrowseFolder.Size = new Size(75, 25);
        btnBrowseFolder.Text = "Chọn...";
        btnBrowseFolder.UseVisualStyleBackColor = true;
        btnBrowseFolder.Click += BtnBrowseFolder_Click;
        //
        // lblApiKey
        //
        lblApiKey.AutoSize = true;
        lblApiKey.Location = new Point(12, 81);
        lblApiKey.Name = "lblApiKey";
        lblApiKey.Size = new Size(101, 15);
        lblApiKey.Text = "Gemini API key:";
        //
        // txtApiKey
        //
        txtApiKey.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtApiKey.Location = new Point(150, 78);
        txtApiKey.Name = "txtApiKey";
        txtApiKey.Size = new Size(649, 23);
        txtApiKey.UseSystemPasswordChar = true;
        //
        // lblModel
        //
        lblModel.AutoSize = true;
        lblModel.Location = new Point(12, 114);
        lblModel.Name = "lblModel";
        lblModel.Size = new Size(91, 15);
        lblModel.Text = "Model Gemini:";
        //
        // txtModel
        //
        txtModel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtModel.Location = new Point(150, 111);
        txtModel.Name = "txtModel";
        txtModel.Size = new Size(649, 23);
        //
        // lblMaxFiles
        //
        lblMaxFiles.AutoSize = true;
        lblMaxFiles.Location = new Point(12, 146);
        lblMaxFiles.Name = "lblMaxFiles";
        lblMaxFiles.Size = new Size(115, 15);
        lblMaxFiles.Text = "Tối đa số file:";
        //
        // numMaxFiles
        //
        numMaxFiles.Location = new Point(150, 143);
        numMaxFiles.Maximum = new decimal(new int[] { 999, 0, 0, 0 });
        numMaxFiles.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        numMaxFiles.Name = "numMaxFiles";
        numMaxFiles.Size = new Size(80, 23);
        numMaxFiles.Value = new decimal(new int[] { 2, 0, 0, 0 });
        //
        // btnCham
        //
        btnCham.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnCham.Location = new Point(652, 172);
        btnCham.Name = "btnCham";
        btnCham.Size = new Size(147, 32);
        btnCham.Text = "Chấm & xuất Excel";
        btnCham.UseVisualStyleBackColor = true;
        btnCham.Click += BtnCham_Click;
        //
        // txtLog
        //
        txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        txtLog.Location = new Point(12, 238);
        txtLog.Multiline = true;
        txtLog.Name = "txtLog";
        txtLog.ReadOnly = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        txtLog.Size = new Size(787, 211);
        txtLog.TabStop = false;
        //
        // progressBar
        //
        progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        progressBar.Location = new Point(12, 208);
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(787, 18);
        progressBar.Style = ProgressBarStyle.Marquee;
        progressBar.Visible = false;
        //
        // lblHint
        //
        lblHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblHint.Location = new Point(12, 172);
        lblHint.Name = "lblHint";
        lblHint.Size = new Size(620, 32);
        lblHint.Text = "Chỉ xử lý tối đa N file đầu tiên (sắp xếp tên). Excel có cột chi tiết điểm + lý do từng hạng 1.1…4.";
        //
        // Form1
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(811, 491);
        Controls.Add(numMaxFiles);
        Controls.Add(lblMaxFiles);
        Controls.Add(lblHint);
        Controls.Add(progressBar);
        Controls.Add(txtLog);
        Controls.Add(btnCham);
        Controls.Add(txtModel);
        Controls.Add(lblModel);
        Controls.Add(txtApiKey);
        Controls.Add(lblApiKey);
        Controls.Add(btnBrowseFolder);
        Controls.Add(txtFolderPath);
        Controls.Add(lblFolder);
        Controls.Add(btnBrowseCriteria);
        Controls.Add(txtCriteriaPath);
        Controls.Add(lblCriteria);
        MinimumSize = new Size(640, 430);
        Name = "Form1";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Chấm điểm (Gemini)";
        ((System.ComponentModel.ISupportInitialize)numMaxFiles).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label lblCriteria;
    private TextBox txtCriteriaPath;
    private Button btnBrowseCriteria;
    private Label lblFolder;
    private TextBox txtFolderPath;
    private Button btnBrowseFolder;
    private Label lblApiKey;
    private TextBox txtApiKey;
    private Label lblModel;
    private TextBox txtModel;
    private Button btnCham;
    private TextBox txtLog;
    private ProgressBar progressBar;
    private Label lblHint;
    private Label lblMaxFiles;
    private NumericUpDown numMaxFiles;
}
