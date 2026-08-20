namespace SurumYakma
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.lblHello = new System.Windows.Forms.Label();
            this.driveList = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
            this.label2 = new System.Windows.Forms.Label();
            this.button2 = new System.Windows.Forms.Button();
            this.textBox2 = new System.Windows.Forms.TextBox();
            this.UKB1Yak = new System.Windows.Forms.Button();
            this.label3 = new System.Windows.Forms.Label();
            this.surumList = new System.Windows.Forms.ComboBox();
            this.projectList = new System.Windows.Forms.ComboBox();
            this.RelayBoxBaglantisiLabel = new System.Windows.Forms.Label();
            this.RelayBoxIPLabel = new System.Windows.Forms.Label();
            this.PowerBoxBaglantisiLabel = new System.Windows.Forms.Label();
            this.PowerBoxIPLabel = new System.Windows.Forms.Label();
            this.Sel = new System.Windows.Forms.Label();
            this.timer3 = new System.Windows.Forms.Timer(this.components);
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.label4 = new System.Windows.Forms.Label();
            this.UKB2Yak = new System.Windows.Forms.Button();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.textBox3 = new System.Windows.Forms.TextBox();
            this.SuspendLayout();
            // 
            // lblHello
            // 
            this.lblHello.AutoSize = true;
            this.lblHello.BackColor = System.Drawing.Color.LightGray;
            this.lblHello.Font = new System.Drawing.Font("Microsoft Sans Serif", 20F);
            this.lblHello.Location = new System.Drawing.Point(21, 65);
            this.lblHello.Name = "lblHello";
            this.lblHello.Size = new System.Drawing.Size(592, 31);
            this.lblHello.TabIndex = 0;
            this.lblHello.Text = "Sürüm Yakmak İçin Kullanacağınız Diski Seçiniz";
            this.lblHello.Click += new System.EventHandler(this.lblHello_Click);
            // 
            // driveList
            // 
            this.driveList.FormattingEnabled = true;
            this.driveList.Location = new System.Drawing.Point(27, 99);
            this.driveList.Name = "driveList";
            this.driveList.Size = new System.Drawing.Size(525, 21);
            this.driveList.TabIndex = 1;
            this.driveList.SelectedIndexChanged += new System.EventHandler(this.driveList_SelectedIndexChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.BackColor = System.Drawing.Color.LightGray;
            this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 20F);
            this.label1.Location = new System.Drawing.Point(21, 387);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(580, 31);
            this.label1.TabIndex = 2;
            this.label1.Text = "Sürüm Dosyalarının Bulunduğu Klasörü Seçiniz";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.BackColor = System.Drawing.Color.LightGray;
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 20F);
            this.label2.Location = new System.Drawing.Point(21, 147);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(229, 31);
            this.label2.TabIndex = 3;
            this.label2.Text = "Proje İsmi Seçiniz";
            // 
            // button2
            // 
            this.button2.Font = new System.Drawing.Font("Microsoft Sans Serif", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.button2.Location = new System.Drawing.Point(619, 387);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(142, 34);
            this.button2.TabIndex = 5;
            this.button2.Text = "Dosyayı Seç";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // textBox2
            // 
            this.textBox2.Location = new System.Drawing.Point(27, 421);
            this.textBox2.Name = "textBox2";
            this.textBox2.ReadOnly = true;
            this.textBox2.Size = new System.Drawing.Size(586, 20);
            this.textBox2.TabIndex = 8;
            // 
            // UKB1Yak
            // 
            this.UKB1Yak.Font = new System.Drawing.Font("Microsoft Sans Serif", 20.25F);
            this.UKB1Yak.Location = new System.Drawing.Point(27, 208);
            this.UKB1Yak.Name = "UKB1Yak";
            this.UKB1Yak.Size = new System.Drawing.Size(101, 38);
            this.UKB1Yak.TabIndex = 10;
            this.UKB1Yak.Text = "Başlat";
            this.UKB1Yak.UseVisualStyleBackColor = true;
            this.UKB1Yak.Click += new System.EventHandler(this.UKB1Yak_Click);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.BackColor = System.Drawing.Color.LightGray;
            this.label3.Font = new System.Drawing.Font("Microsoft Sans Serif", 20F);
            this.label3.Location = new System.Drawing.Point(21, 472);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(438, 31);
            this.label3.TabIndex = 11;
            this.label3.Text = "Yüklemek İstediğiniz Sürümü Seçin";
            // 
            // surumList
            // 
            this.surumList.FormattingEnabled = true;
            this.surumList.Location = new System.Drawing.Point(27, 506);
            this.surumList.Name = "surumList";
            this.surumList.Size = new System.Drawing.Size(525, 21);
            this.surumList.TabIndex = 12;
            // 
            // projectList
            // 
            this.projectList.FormattingEnabled = true;
            this.projectList.Location = new System.Drawing.Point(27, 181);
            this.projectList.Name = "projectList";
            this.projectList.Size = new System.Drawing.Size(525, 21);
            this.projectList.TabIndex = 13;
            this.projectList.SelectedIndexChanged += new System.EventHandler(this.projectList_SelectedIndexChanged);
            // 
            // RelayBoxBaglantisiLabel
            // 
            this.RelayBoxBaglantisiLabel.AutoSize = true;
            this.RelayBoxBaglantisiLabel.BackColor = System.Drawing.Color.Red;
            this.RelayBoxBaglantisiLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 20.25F);
            this.RelayBoxBaglantisiLabel.Location = new System.Drawing.Point(841, 113);
            this.RelayBoxBaglantisiLabel.Name = "RelayBoxBaglantisiLabel";
            this.RelayBoxBaglantisiLabel.Size = new System.Drawing.Size(262, 31);
            this.RelayBoxBaglantisiLabel.TabIndex = 14;
            this.RelayBoxBaglantisiLabel.Text = "Relay Box Connection";
            // 
            // RelayBoxIPLabel
            // 
            this.RelayBoxIPLabel.AutoSize = true;
            this.RelayBoxIPLabel.BackColor = System.Drawing.Color.Transparent;
            this.RelayBoxIPLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 20.25F);
            this.RelayBoxIPLabel.Location = new System.Drawing.Point(311, 800);
            this.RelayBoxIPLabel.Name = "RelayBoxIPLabel";
            this.RelayBoxIPLabel.Size = new System.Drawing.Size(0, 31);
            this.RelayBoxIPLabel.TabIndex = 15;
            // 
            // PowerBoxBaglantisiLabel
            // 
            this.PowerBoxBaglantisiLabel.AutoSize = true;
            this.PowerBoxBaglantisiLabel.BackColor = System.Drawing.Color.Red;
            this.PowerBoxBaglantisiLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 20.25F);
            this.PowerBoxBaglantisiLabel.Location = new System.Drawing.Point(841, 159);
            this.PowerBoxBaglantisiLabel.Name = "PowerBoxBaglantisiLabel";
            this.PowerBoxBaglantisiLabel.Size = new System.Drawing.Size(269, 31);
            this.PowerBoxBaglantisiLabel.TabIndex = 16;
            this.PowerBoxBaglantisiLabel.Text = "Power Box Connection";
            // 
            // PowerBoxIPLabel
            // 
            this.PowerBoxIPLabel.AutoSize = true;
            this.PowerBoxIPLabel.BackColor = System.Drawing.Color.Transparent;
            this.PowerBoxIPLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 20.25F);
            this.PowerBoxIPLabel.Location = new System.Drawing.Point(1401, 800);
            this.PowerBoxIPLabel.Name = "PowerBoxIPLabel";
            this.PowerBoxIPLabel.Size = new System.Drawing.Size(0, 31);
            this.PowerBoxIPLabel.TabIndex = 17;
            // 
            // Sel
            // 
            this.Sel.AutoSize = true;
            this.Sel.Font = new System.Drawing.Font("Microsoft Sans Serif", 20.25F);
            this.Sel.Location = new System.Drawing.Point(945, 208);
            this.Sel.Name = "Sel";
            this.Sel.Size = new System.Drawing.Size(86, 31);
            this.Sel.TabIndex = 18;
            this.Sel.Text = "label4";
            // 
            // progressBar1
            // 
            this.progressBar1.Location = new System.Drawing.Point(836, 73);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(338, 25);
            this.progressBar1.TabIndex = 19;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Font = new System.Drawing.Font("Microsoft Sans Serif", 20.25F);
            this.label4.Location = new System.Drawing.Point(945, 39);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(110, 31);
            this.label4.TabIndex = 20;
            this.label4.Text = "İlerleme";
            // 
            // UKB2Yak
            // 
            this.UKB2Yak.Font = new System.Drawing.Font("Microsoft Sans Serif", 20.25F);
            this.UKB2Yak.Location = new System.Drawing.Point(27, 533);
            this.UKB2Yak.Name = "UKB2Yak";
            this.UKB2Yak.Size = new System.Drawing.Size(101, 38);
            this.UKB2Yak.TabIndex = 21;
            this.UKB2Yak.Text = "Başlat";
            this.UKB2Yak.UseVisualStyleBackColor = true;
            this.UKB2Yak.Click += new System.EventHandler(this.UKB2Yak_Click);
            // 
            // textBox1
            // 
            this.textBox1.Location = new System.Drawing.Point(27, 350);
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(180, 20);
            this.textBox1.TabIndex = 22;
            this.textBox1.Text = "Flaş Belleğe Sürüm Yükleme";
            this.textBox1.TextChanged += new System.EventHandler(this.textBox1_TextChanged);
            // 
            // textBox3
            // 
            this.textBox3.Location = new System.Drawing.Point(27, 39);
            this.textBox3.Name = "textBox3";
            this.textBox3.Size = new System.Drawing.Size(180, 20);
            this.textBox3.TabIndex = 23;
            this.textBox3.Text = "Sürüm Yakma";
            this.textBox3.TextChanged += new System.EventHandler(this.textBox3_TextChanged);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1355, 599);
            this.Controls.Add(this.textBox3);
            this.Controls.Add(this.textBox1);
            this.Controls.Add(this.UKB2Yak);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.progressBar1);
            this.Controls.Add(this.Sel);
            this.Controls.Add(this.PowerBoxIPLabel);
            this.Controls.Add(this.PowerBoxBaglantisiLabel);
            this.Controls.Add(this.RelayBoxIPLabel);
            this.Controls.Add(this.RelayBoxBaglantisiLabel);
            this.Controls.Add(this.projectList);
            this.Controls.Add(this.surumList);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.UKB1Yak);
            this.Controls.Add(this.textBox2);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.driveList);
            this.Controls.Add(this.lblHello);
            this.Name = "Form1";
            this.Text = "Form1";
            this.TopMost = false;
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblHello;
        private System.Windows.Forms.ComboBox driveList;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.TextBox textBox2;
        private System.Windows.Forms.Button UKB1Yak;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ComboBox surumList;
        private System.Windows.Forms.ComboBox projectList;
        private System.Windows.Forms.Label RelayBoxBaglantisiLabel;
        private System.Windows.Forms.Label RelayBoxIPLabel;
        private System.Windows.Forms.Label PowerBoxBaglantisiLabel;
        private System.Windows.Forms.Label PowerBoxIPLabel;
        private System.Windows.Forms.Label Sel;
        private System.Windows.Forms.Timer timer3;
        private System.Windows.Forms.ProgressBar progressBar1;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Button UKB2Yak;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.TextBox textBox3;
    }
}

