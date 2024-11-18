using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using file_transfer;
// Đây là lớp chính của form
public partial class Main : Form
{
    // Biến này sẽ giữ listener. Chúng ta chỉ cần tạo một instance của nó.
    private Listener listener;
    // Biến này sẽ giữ client chuyển file.
    private TransferClient transferClient;
    // Biến này sẽ giữ thư mục đầu ra.
    private string outputFolder;
    // Biến này sẽ giữ timer tiến trình tổng thể.
    private Timer tmrOverallProg;
    // Biến này để xác định server có đang chạy hay không để chấp nhận kết nối khác nếu client của chúng ta
    // Ngắt kết nối
    private bool serverRunning;
    // Hàm khởi tạo của form
    public Main()
    {
        InitializeComponent();
        // Tạo listener và đăng ký sự kiện.
        listener = new Listener();
        listener.Accepted += listener_Accepted;
        // Tạo timer và đăng ký sự kiện.
        tmrOverallProg = new Timer();
        tmrOverallProg.Interval = 1000;
        tmrOverallProg.Tick += tmrOverallProg_Tick;
        // Đặt thư mục đầu ra mặc định.
        outputFolder = "Transfers";
        // Nếu thư mục không tồn tại, tạo nó.
        if (!Directory.Exists(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
        }
        // Đăng ký sự kiện cho các nút bấm.
        btnConnect.Click += new EventHandler(btnConnect_Click);
        btnStartServer.Click += new EventHandler(btnStartServer_Click);
        btnStopServer.Click += new EventHandler(btnStopServer_Click);
        btnSendFile.Click += new EventHandler(btnSendFile_Click);
        btnPauseTransfer.Click += new EventHandler(btnPauseTransfer_Click);
        btnStopTransfer.Click += new EventHandler(btnStopTransfer_Click);
        btnOpenDir.Click += new EventHandler(btnOpenDir_Click);
        btnClearComplete.Click += new EventHandler(btnClearComplete_Click);
        // Vô hiệu hóa nút dừng server.
        btnStopServer.Enabled = false;
    }
    // Hàm xử lý sự kiện khi form đóng
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Hủy đăng ký tất cả các sự kiện từ client nếu nó đang kết nối.
        deregisterEvents();
        base.OnFormClosing(e);
    }
    // Hàm xử lý sự kiện tick của timer
    void tmrOverallProg_Tick(object sender, EventArgs e)
    {
        if (transferClient == null)
            return;
        // Lấy và hiển thị tiến trình tổng thể.
        progressOverall.Value = transferClient.GetOverallProgress();
    }
    // Hàm xử lý sự kiện khi listener chấp nhận kết nối
    void listener_Accepted(object sender, SocketAcceptedEventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(new SocketAcceptedHandler(listener_Accepted), sender, e);
            return;
        }
        // Dừng listener
        listener.Stop();
        // Tạo client chuyển file dựa trên socket mới kết nối.
        transferClient = new TransferClient(e.Accepted);
        // Đặt thư mục đầu ra.
        transferClient.OutputFolder = outputFolder;
        // Đăng ký các sự kiện.
        registerEvents();
        // Chạy client
        transferClient.Run();
        // Bắt đầu timer tiến trình
        tmrOverallProg.Start();
        // Và đặt trạng thái kết nối mới.
        setConnectionStatus(transferClient.EndPoint.Address.ToString());
    }
    // Hàm xử lý sự kiện khi nút kết nối được bấm
    private void btnConnect_Click(object sender, EventArgs e)
    {
        if (transferClient == null)
        {
            // Tạo client chuyển file mới.
            // Và cố gắng kết nối
            transferClient = new TransferClient();
            transferClient.Connect(txtCntHost.Text.Trim(), int.Parse(txtCntPort.Text.Trim()), connectCallback);
            Enabled = false;
        }
        else
        {
            // Điều này có nghĩa là chúng ta đang cố gắng ngắt kết nối.
            transferClient.Close();
            transferClient = null;
        }
    }
    // Hàm callback khi kết nối
    private void connectCallback(object sender, string error)
    {
        if (InvokeRequired)
        {
            Invoke(new ConnectCallback(connectCallback), sender, error);
            return;
        }
        // Đặt form thành enabled.
        Enabled = true;
        // Nếu có lỗi, có điều gì đó đã sai.
        if (error != null)
        {
            transferClient.Close();
            transferClient = null;
            MessageBox.Show(error, "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        // Đăng ký các sự kiện
        registerEvents();
        // Đặt thư mục đầu ra
        transferClient.OutputFolder = outputFolder;
        // Chạy client
        transferClient.Run();
        // Đặt trạng thái kết nối
        setConnectionStatus(transferClient.EndPoint.Address.ToString());
        // Bắt đầu timer tiến trình.
        tmrOverallProg.Start();
        // Đặt text của nút kết nối thành "Disconnect"
        btnConnect.Text = "Disconnect";
    }
    // Hàm đăng ký các sự kiện
    private void registerEvents()
    {
        transferClient.Complete += transferClient_Complete;
        transferClient.Disconnected += transferClient_Disconnected;
        transferClient.ProgressChanged += transferClient_ProgressChanged;
        transferClient.Queued += transferClient_Queued;
        transferClient.Stopped += transferClient_Stopped;
    }
    // Hàm xử lý sự kiện khi một tiến trình chuyển file bị dừng
    void transferClient_Stopped(object sender, TransferQueue queue)
    {
        if (InvokeRequired)
        {
            // Nếu cần gọi lại từ thread khác, sử dụng Invoke
            Invoke(new TransferEventHandler(transferClient_Stopped), sender, queue);
            return;
        }
        // Xóa tiến trình chuyển file đã dừng khỏi danh sách hiển thị
        lstTransfers.Items[queue.ID.ToString()].Remove();
    }
    // Hàm xử lý sự kiện khi một tiến trình chuyển file được xếp hàng
    void transferClient_Queued(object sender, TransferQueue queue)
    {
        if (InvokeRequired)
        {
            // Nếu cần gọi lại từ thread khác, sử dụng Invoke
            Invoke(new TransferEventHandler(transferClient_Queued), sender, queue);
            return;
        }
        // Tạo ListViewItem cho tiến trình chuyển file mới
        ListViewItem i = new ListViewItem();
        i.Text = queue.ID.ToString();
        i.SubItems.Add(queue.Filename);
        // Nếu loại là download, sử dụng chuỗi "Download", nếu không, sử dụng "Upload"
        i.SubItems.Add(queue.Type == QueueType.Download ? "Download" : "Upload");
        i.SubItems.Add("0%");
        i.Tag = queue; // Đặt tag là queue để dễ dàng truy cập
        i.Name = queue.ID.ToString(); // Đặt tên của item là ID của tiến trình chuyển file để dễ dàng truy cập
        lstTransfers.Items.Add(i); // Thêm item vào danh sách
        i.EnsureVisible();

        // Nếu loại là download, thông báo cho uploader biết chúng ta đã sẵn sàng
        if (queue.Type == QueueType.Download)
        {
            transferClient.StartTransfer(queue);
        }
    }
    // Hàm xử lý sự kiện khi tiến trình chuyển file thay đổi tiến độ
    void transferClient_ProgressChanged(object sender, TransferQueue queue)
    {
        if (InvokeRequired)
        {
            // Nếu cần gọi lại từ thread khác, sử dụng Invoke
            Invoke(new TransferEventHandler(transferClient_ProgressChanged), sender, queue);
            return;
        }
        // Đặt ô tiến độ thành tiến độ hiện tại
        lstTransfers.Items[queue.ID.ToString()].SubItems[3].Text = queue.Progress + "%";
    }
    // Hàm xử lý sự kiện khi client chuyển file bị ngắt kết nối
    void transferClient_Disconnected(object sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            // Nếu cần gọi lại từ thread khác, sử dụng Invoke
            Invoke(new EventHandler(transferClient_Disconnected), sender, e);
            return;
        }
        // Hủy đăng ký các sự kiện của client chuyển file
        deregisterEvents();
        // Đóng tất cả các tiến trình chuyển file
        foreach (ListViewItem item in lstTransfers.Items)
        {
            TransferQueue queue = (TransferQueue)item.Tag;
            queue.Close();
        }
        // Xóa danh sách hiển thị
        lstTransfers.Items.Clear();
        progressOverall.Value = 0;
        // Đặt client thành null
        transferClient = null;
        // Đặt trạng thái kết nối thành không có gì
        setConnectionStatus("-");
        // Nếu server vẫn đang chạy, chờ kết nối khác
        if (serverRunning)
        {
            listener.Start(int.Parse(txtServerPort.Text.Trim()));
            setConnectionStatus("Waiting...");
        }
        else // Nếu chúng ta đã kết nối rồi ngắt kết nối, đặt text của nút thành "Connect"
        {
            btnConnect.Text = "Connect";
        }
    }
    // Hàm xử lý sự kiện khi tiến trình chuyển file hoàn thành
    void transferClient_Complete(object sender, TransferQueue queue)
    {
        // Phát âm thanh để thông báo rằng tiến trình chuyển file đã hoàn thành
        System.Media.SystemSounds.Asterisk.Play();
    }
    // Hàm hủy đăng ký các sự kiện từ client chuyển file
    private void deregisterEvents()
    {
        if (transferClient == null)
            return;
        // Hủy đăng ký sự kiện hoàn thành
        transferClient.Complete -= transferClient_Complete;
        // Hủy đăng ký sự kiện ngắt kết nối
        transferClient.Disconnected -= transferClient_Disconnected;
        // Hủy đăng ký sự kiện thay đổi tiến độ
        transferClient.ProgressChanged -= transferClient_ProgressChanged;
        // Hủy đăng ký sự kiện xếp hàng
        transferClient.Queued -= transferClient_Queued;
        // Hủy đăng ký sự kiện dừng
        transferClient.Stopped -= transferClient_Stopped;
    }
    // Hàm đặt trạng thái kết nối
    private void setConnectionStatus(string connectedTo)
    {
        lblConnected.Text = "Connection: " + connectedTo;
    }
    // Hàm xử lý sự kiện khi nút bắt đầu server được bấm
    private void btnStartServer_Click(object sender, EventArgs e)
    {
        // Chúng ta đã vô hiệu hóa nút, nhưng hãy kiểm tra nhanh
        if (serverRunning)
            return;
        serverRunning = true;
        try
        {
            // Cố gắng lắng nghe trên cổng mong muốn
            listener.Start(int.Parse(txtServerPort.Text.Trim()));
            // Đặt trạng thái kết nối thành "Waiting..."
            setConnectionStatus("Waiting...");
            // Vô hiệu hóa nút bắt đầu server và kích hoạt nút dừng server
            btnStartServer.Enabled = false;
            btnStopServer.Enabled = true;
        }
        catch
        {
            MessageBox.Show("Unable to listen on port " + txtServerPort.Text, "", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    // Hàm xử lý sự kiện khi nút dừng server được bấm
    private void btnStopServer_Click(object sender, EventArgs e)
    {
        if (!serverRunning)
            return;
        // Đóng client nếu nó đang hoạt động
        if (transferClient != null)
        {
            transferClient.Close();
            // Đặt client thành null
            transferClient = null;
        }
        // Dừng listener
        listener.Stop();
        // Dừng timer
        tmrOverallProg.Stop();
        // Đặt lại trạng thái kết nối
        setConnectionStatus("-");
        // Đặt lại các biến và vô hiệu hóa/kích hoạt các nút
        serverRunning = false;
        btnStartServer.Enabled = true;
        btnStopServer.Enabled = false;
    }
    // Hàm xử lý sự kiện khi nút xóa các tiến trình hoàn thành được bấm
    private void btnClearComplete_Click(object sender, EventArgs e)
    {
        // Lặp qua và xóa tất cả các tiến trình hoàn thành hoặc không hoạt động
        foreach (ListViewItem i in lstTransfers.Items)
        {
            TransferQueue queue = (TransferQueue)i.Tag;

            if (queue.Progress == 100 || !queue.Running)
            {
                i.Remove();
            }
        }
    }
    // Hàm xử lý sự kiện khi nút mở thư mục được bấm
    private void btnOpenDir_Click(object sender, EventArgs e)
    {
        // Lấy thư mục lưu trữ do người dùng định nghĩa
        using (FolderBrowserDialog fb = new FolderBrowserDialog())
        {
            if (fb.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                outputFolder = fb.SelectedPath;

                if (transferClient != null)
                {
                    transferClient.OutputFolder = outputFolder;
                }
                txtSaveDir.Text = outputFolder;
            }
        }
    }
    // Hàm xử lý sự kiện khi nút gửi file được bấm
    private void btnSendFile_Click(object sender, EventArgs e)
    {
        if (transferClient == null)
            return;
        // Lấy các file mà người dùng muốn gửi
        using (OpenFileDialog o = new OpenFileDialog())
        {
            o.Filter = "All Files (*.*)|*.*";
            o.Multiselect = true;
            if (o.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                foreach (string file in o.FileNames)
                {
                    transferClient.QueueTransfer(file);
                }
            }
        }
    }
    // Hàm xử lý sự kiện khi nút tạm dừng tiến trình được bấm
    private void btnPauseTransfer_Click(object sender, EventArgs e)
    {
        if (transferClient == null)
            return;
        // Lặp qua và tạm dừng/tái khởi động tất cả các tiến trình được chọn
        foreach (ListViewItem i in lstTransfers.SelectedItems)
        {
            TransferQueue queue = (TransferQueue)i.Tag;
            queue.Client.PauseTransfer(queue);
        }
    }
    // Hàm xử lý sự kiện khi nút dừng tiến trình được bấm
    private void btnStopTransfer_Click(object sender, EventArgs e)
    {
        if (transferClient == null)
            return;
        // Lặp qua và dừng tất cả các tiến trình được chọn
        foreach (ListViewItem i in lstTransfers.SelectedItems)
        {
            TransferQueue queue = (TransferQueue)i.Tag;
            queue.Client.StopTransfer(queue);
            i.Remove();
        }
        progressOverall.Value = 0;
    }
    // Hàm xử lý sự kiện open file muốn mở
    private void open_Click(object sender, EventArgs e)
    {
        if (lstTransfers.SelectedItems.Count == 0)
        {
            MessageBox.Show("Please select a file to open.", "No File Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        ListViewItem selectedItem = lstTransfers.SelectedItems[0];
        TransferQueue queue = (TransferQueue)selectedItem.Tag;
        string filePath = Path.Combine(outputFolder, queue.Filename);
        if (File.Exists(filePath))
        {
            try
            {
                System.Diagnostics.Process.Start(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open file: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        else
        {
            MessageBox.Show("File not found: " + filePath, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}