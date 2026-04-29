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
        // lblFolder
        //
        lblFolder.AutoSize = true;
        lblFolder.Location = new Point(12, 15);
        lblFolder.Name = "lblFolder";
        lblFolder.Size = new Size(127, 15);
        lblFolder.Text = "Thư mục bài thi:";
        //
        // txtFolderPath
        //
        txtFolderPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtFolderPath.Location = new Point(150, 12);
        txtFolderPath.Name = "txtFolderPath";
        txtFolderPath.Size = new Size(568, 23);
        //
        // btnBrowseFolder
        //
        btnBrowseFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnBrowseFolder.Location = new Point(724, 11);
        btnBrowseFolder.Name = "btnBrowseFolder";
        btnBrowseFolder.Size = new Size(75, 25);
        btnBrowseFolder.Text = "Chọn...";
        btnBrowseFolder.UseVisualStyleBackColor = true;
        btnBrowseFolder.Click += BtnBrowseFolder_Click;
        //
        // lblApiKey
        //
        lblApiKey.AutoSize = true;
        lblApiKey.Location = new Point(12, 48);
        lblApiKey.Name = "lblApiKey";
        lblApiKey.Size = new Size(101, 15);
        lblApiKey.Text = "Gemini API key:";
        //
        // txtApiKey
        //
        txtApiKey.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtApiKey.Location = new Point(150, 45);
        txtApiKey.Name = "txtApiKey";
        txtApiKey.Size = new Size(649, 23);
        txtApiKey.UseSystemPasswordChar = true;
        //
        // lblModel
        //
        lblModel.AutoSize = true;
        lblModel.Location = new Point(12, 81);
        lblModel.Name = "lblModel";
        lblModel.Size = new Size(91, 15);
        lblModel.Text = "Model Gemini:";
        //
        // txtModel
        //
        txtModel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtModel.Location = new Point(150, 78);
        txtModel.Name = "txtModel";
        txtModel.Size = new Size(649, 23);
        //
        // lblMaxFiles
        //
        lblMaxFiles.AutoSize = true;
        lblMaxFiles.Location = new Point(12, 113);
        lblMaxFiles.Name = "lblMaxFiles";
        lblMaxFiles.Size = new Size(115, 15);
        lblMaxFiles.Text = "Tối đa số file:";
        //
        // numMaxFiles
        //
        numMaxFiles.Location = new Point(150, 110);
        numMaxFiles.Maximum = new decimal(new int[] { 999, 0, 0, 0 });
        numMaxFiles.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        numMaxFiles.Name = "numMaxFiles";
        numMaxFiles.Size = new Size(80, 23);
        numMaxFiles.Value = new decimal(new int[] { 2, 0, 0, 0 });
        //
        // btnCham
        //
        btnCham.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnCham.Location = new Point(652, 138);
        btnCham.Name = "btnCham";
        btnCham.Size = new Size(147, 32);
        btnCham.Text = "Chấm & xuất báo cáo";
        btnCham.UseVisualStyleBackColor = true;
        btnCham.Click += BtnCham_Click;
        //
        // txtLog
        //
        txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        txtLog.Location = new Point(12, 205);
        txtLog.Multiline = true;
        txtLog.Name = "txtLog";
        txtLog.ReadOnly = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        txtLog.Size = new Size(787, 244);
        txtLog.TabStop = false;
        //
        // progressBar
        //
        progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        progressBar.Location = new Point(12, 175);
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(787, 18);
        progressBar.Style = ProgressBarStyle.Marquee;
        progressBar.Visible = false;
        //
        // lblHint
        //
        lblHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblHint.Location = new Point(12, 138);
        lblHint.Name = "lblHint";
        lblHint.Size = new Size(620, 32);
        lblHint.Text = "Tiêu chí chấm đã được nhúng sẵn (I.1–II.4). Xuất Word (.docx) hoặc Excel (.xlsx).";
        //
        // Form1
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(811, 461);
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
        MinimumSize = new Size(640, 400);
        Name = "Form1";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Chấm điểm (Gemini) — Bữa cơm gia đình";
        ((System.ComponentModel.ISupportInitialize)numMaxFiles).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

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
